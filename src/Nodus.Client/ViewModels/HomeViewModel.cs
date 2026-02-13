using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Client.Views;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Microsoft.Extensions.Logging;

namespace Nodus.Client.ViewModels;

/// <summary>
/// Professional ViewModel for Home page with proper async patterns,
/// error handling, and cancellation support.
/// </summary>
public partial class HomeViewModel : ObservableObject, IDisposable
{
    private readonly IDatabaseService _db;
    private readonly Nodus.Shared.Services.BleClientService _bleService;
    private readonly Nodus.Shared.Services.SwarmService _swarmService;
    private readonly ILogger<HomeViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCts = new();

    [ObservableProperty]
    private string _statusMessage = "Initializing...";
    
    [ObservableProperty]
    private Color _statusColor = Colors.Gray;

    [ObservableProperty]
    private string _judgeName = "";

    [ObservableProperty]
    private bool _isSyncing;

    public HomeViewModel(
        IDatabaseService db, 
        Nodus.Shared.Services.BleClientService bleService, 
        Nodus.Shared.Services.SwarmService swarmService,
        ILogger<HomeViewModel> logger)
    {
        _db = db;
        _bleService = bleService;
        _swarmService = swarmService;
        _logger = logger;
        
        // Subscribe to service events with proper error boundaries
        _bleService.ConnectionStatusChanged += OnConnectionStatusChanged;
        _swarmService.PropertyChanged += OnSwarmServicePropertyChanged;

        // Initialize asynchronously (fire-and-forget with error handling)
        _ = InitializeAsync();
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await UpdateStatusAsync(_lifetimeCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status on connection change");
            }
        });
    }

    private void OnSwarmServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Nodus.Shared.Services.SwarmService.IsMuleMode) || 
            e.PropertyName == nameof(Nodus.Shared.Services.SwarmService.CurrentState))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await UpdateStatusAsync(_lifetimeCts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating status on swarm property change");
                }
            });
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            // 1. Load Judge Identity
            var name = await SecureStorage.Default.GetAsync(Nodus.Shared.NodusConstants.KEY_JUDGE_NAME);
            if (!string.IsNullOrEmpty(name))
            {
                JudgeName = name;
            }
            else
            {
                StatusMessage = "Welcome. Please Scan Event QR.";
                StatusColor = Colors.Gray;
                return;
            }

            // 2. Verify Database
            var eventsResult = await _db.GetEventsAsync(_lifetimeCts.Token);
            if (eventsResult.IsFailure)
            {
                _logger.LogError("Failed to load events: {Error}", eventsResult.Error);
                StatusMessage = "Database Error";
                StatusColor = Colors.Red;
                return;
            }

            // 3. Update UI
            await UpdateStatusAsync(_lifetimeCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialization failed");
            StatusMessage = "Initialization Error";
            StatusColor = Colors.Red;
        }
    }

    /// <summary>
    /// Updates the status message and color based on current state.
    /// Called from UI lifecycle and service events.
    /// </summary>
    public async Task UpdateStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var pendingResult = await _db.GetPendingVotesAsync(ct);
            if (pendingResult.IsFailure)
            {
                _logger.LogWarning("Failed to get pending votes: {Error}", pendingResult.Error);
                return;
            }

            int count = pendingResult.Value?.Count ?? 0;

            // Traffic Light Logic (per UX spec)
            if (_swarmService.IsMuleMode)
            {
                StatusColor = Colors.Purple;
                StatusMessage = "Mule Mode: Network Unreachable. Walk to Admin.";
            }
            else if (_bleService.IsConnected)
            {
                StatusColor = Colors.Green;
                StatusMessage = "Synced: Connected to Server.";
                
                // OPTIMISTIC UI: Auto-Sync pending votes
                if (count > 0 && !IsSyncing)
                {
                    _ = Task.Run(async () => 
                    {
                        await Task.Delay(500, ct); // Stabilization delay
                        await SyncPendingVotesAsync(ct);
                    }, ct);
                }
            }
            else if (count > 0)
            {
                StatusColor = Colors.Orange; // Amber
                StatusMessage = $"Pending: {count} votes saved locally.";
            }
            else
            {
                StatusColor = Colors.Red;
                StatusMessage = "Scanning for Event...";
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Status update cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating status");
        }
    }

    [RelayCommand]
    private async Task NavigateToRegistrationAsync(CancellationToken ct)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(ScanPage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Navigation failed");
            await ShowErrorAsync("Navigation failed", ct);
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SyncPendingVotesAsync(CancellationToken ct)
    {
        if (IsSyncing)
        {
            _logger.LogDebug("Sync already in progress, skipping");
            return;
        }

        IsSyncing = true;
        try
        {
            _logger.LogInformation("Starting manual sync");

            var pendingResult = await _db.GetPendingVotesAsync(ct);
            if (pendingResult.IsFailure)
            {
                await ShowErrorAsync($"Failed to load votes: {pendingResult.Error}", ct);
                return;
            }

            var pending = pendingResult.Value ?? new();
            if (pending.Count == 0)
            {
                StatusMessage = "No pending votes to sync.";
                return;
            }

            int successCount = 0;
            foreach (var vote in pending)
            {
                ct.ThrowIfCancellationRequested();

                // Send via BLE using the public specialized method
                try
                {
                    var syncResult = await _bleService.SendVoteAsync(vote, ct);

                    if (syncResult.IsSuccess)
                    {
                        vote.Status = Nodus.Shared.Models.SyncStatus.Synced;
                        vote.SyncedAtUtc = DateTime.UtcNow;
                        
                        var saveResult = await _db.SaveVoteAsync(vote, ct);
                        if (saveResult.IsSuccess)
                        {
                            successCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync vote {VoteId}", vote.Id);
                }
            }

            StatusMessage = $"Synced {successCount}/{pending.Count} votes.";
            _logger.LogInformation("Sync completed: {Success}/{Total}", successCount, pending.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Sync cancelled by user");
            StatusMessage = "Sync cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed");
            await ShowErrorAsync("Sync failed. Please try again.", ct);
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private async Task ShowErrorAsync(string message, CancellationToken ct)
    {
        try
        {
            if (!ct.IsCancellationRequested && Application.Current?.Windows.Count > 0)
            {
                var page = Application.Current.Windows[0].Page;
                if (page != null)
                {
                    await page.DisplayAlertAsync("Error", message, "OK");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show error dialog");
        }
    }

    // Proper cleanup
    public void Dispose()
    {
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        _bleService.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _swarmService.PropertyChanged -= OnSwarmServicePropertyChanged;
    }
}
