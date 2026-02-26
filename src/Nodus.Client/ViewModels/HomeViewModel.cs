using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Client.Views;
using Nodus.Infrastructure.Services;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Microsoft.Extensions.Logging;

namespace Nodus.Client.ViewModels;

/// <summary>
/// ViewModel for the Home/Dashboard page.
/// Implements traffic-light status, judge registration state, and optimistic sync.
/// </summary>
public partial class HomeViewModel : ObservableObject, IDisposable
{
    private readonly IDatabaseService _db;
    private readonly BleClientService _bleService;
    private readonly SwarmService _swarmService;
    private readonly Nodus.Client.Services.CloudProjectSyncService _cloudSync;
    private readonly ILogger<HomeViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCts = new();

    // ── Status Bar (Traffic Light) ─────────────────────────────────────────
    [ObservableProperty] private string _statusMessage = "Initializing...";
    [ObservableProperty] private Color _statusColor = Colors.Gray;
    [ObservableProperty] private string _statusIcon = "⚪";

    // ── Judge Identity ─────────────────────────────────────────────────────
    [ObservableProperty] private string _judgeName = "";
    [ObservableProperty] private string _judgeInitial = "?";
    [ObservableProperty] private bool _isJudgeRegistered;
    [ObservableProperty] private string _eventName = "";

    // ── Stats Row ──────────────────────────────────────────────────────────
    [ObservableProperty] private int _pendingVoteCount;
    [ObservableProperty] private int _syncedVoteCount;
    [ObservableProperty] private bool _isSyncing;

    public HomeViewModel(
        IDatabaseService db,
        BleClientService bleService,
        SwarmService swarmService,
        Nodus.Client.Services.CloudProjectSyncService cloudSync,
        ILogger<HomeViewModel> logger)
    {
        _db = db;
        _bleService = bleService;
        _swarmService = swarmService;
        _cloudSync = cloudSync;
        _logger = logger;

        _bleService.ConnectionStatusChanged += OnConnectionStatusChanged;
        _swarmService.PropertyChanged += OnSwarmServicePropertyChanged;

        _ = RefreshAsync();
    }

    /// <summary>
    /// Reloads judge identity and status. Called on page Appearing and after QR scan.
    /// </summary>
    public async Task RefreshAsync()
    {
        try
        {
            var name = await SecureStorage.Default.GetAsync(Nodus.Shared.NodusConstants.KEY_JUDGE_NAME);
            var eventId = await SecureStorage.Default.GetAsync(Nodus.Shared.NodusConstants.KEY_EVENT_ID);

            IsJudgeRegistered = !string.IsNullOrEmpty(name);

            if (IsJudgeRegistered)
            {
                JudgeName = name!;
                JudgeInitial = name![0].ToString().ToUpper();
            }
            else
            {
                JudgeName = "";
                JudgeInitial = "?";
                StatusMessage = "Scan the Event QR to get started";
                StatusColor = Colors.Gray;
                StatusIcon = "⚪";
                return;
            }

            // Load event name from DB
            if (!string.IsNullOrEmpty(eventId))
            {
                // Trigger cloud sync if possible
                _ = Task.Run(async () => 
                {
                    await _cloudSync.SyncProjectsAsync(eventId, _lifetimeCts.Token);
                });

                var eventResult = await _db.GetEventAsync(eventId, _lifetimeCts.Token);
                EventName = eventResult.IsSuccess ? eventResult.Value.Name : "Active Event";
            }

            await UpdateStatusAsync(_lifetimeCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed");
            StatusMessage = "Error loading data";
            StatusColor = Colors.Red;
        }
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await UpdateStatusAsync(_lifetimeCts.Token); }
            catch (Exception ex) { _logger.LogError(ex, "Error updating status on connection change"); }
        });
    }

    private void OnSwarmServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SwarmService.IsMuleMode) or nameof(SwarmService.CurrentState))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try { await UpdateStatusAsync(_lifetimeCts.Token); }
                catch (Exception ex) { _logger.LogError(ex, "Error updating status on swarm change"); }
            });
        }
    }

    public async Task UpdateStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var pendingResult = await _db.GetPendingVotesAsync(ct);
            int pending = pendingResult.IsSuccess ? pendingResult.Value?.Count ?? 0 : 0;
            PendingVoteCount = pending;

            // Update synced count via SyncStats
            var statsResult = await _db.GetSyncStatsAsync(ct);
            SyncedVoteCount = statsResult.IsSuccess ? statsResult.Value.SyncedVotes : 0;

            // Traffic Light
            if (_swarmService.IsMuleMode)
            {
                StatusIcon = "🟣";
                StatusColor = Colors.Purple;
                StatusMessage = "Mule Mode: Walk to the Admin Desk to sync";
            }
            else if (_bleService.IsConnected)
            {
                StatusIcon = "🟢";
                StatusColor = Color.FromArgb("#22C55E");
                StatusMessage = pending > 0 ? $"Connected  —  Syncing {pending} votes..." : "Connected  —  All synced ✓";

                if (pending > 0 && !IsSyncing)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500, ct);
                        await SyncPendingVotesAsync(ct);
                    }, ct);

                // Trigger true offline Project sync (BLE via GATT stream)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var projs = await _db.GetProjectsAsync(this.EventName, ct); // just grab all
                        var allProjsResult = await _db.GetAllProjectsAsync(ct);
                        if (allProjsResult.IsSuccess && allProjsResult.Value.Count == 0)
                        {
                            var bleResult = await _bleService.GetProjectsFromServerAsync(ct);
                            if (bleResult.IsSuccess && bleResult.Value != null)
                            {
                                foreach(var p in bleResult.Value) await _db.SaveProjectAsync(p, ct);
                            }
                        }
                    }
                    catch (Exception ex) { _logger.LogError(ex, "BLE Project Sync Trigger failed"); }
                }, ct);
            }
            else if (pending > 0)
            {
                StatusIcon = "🟡";
                StatusColor = Color.FromArgb("#F59E0B");
                StatusMessage = $"{pending} vote{(pending == 1 ? "" : "s")} saved locally — waiting for connection";
            }
            else
            {
                StatusIcon = "🔴";
                StatusColor = Color.FromArgb("#EF4444");
                StatusMessage = "Scanning for Nodus Server...";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Error updating status"); }
    }

    // ── Navigation Commands ────────────────────────────────────────────────

    /// <summary>Scan Event QR to register as judge (first time).</summary>
    [RelayCommand]
    private async Task NavigateToRegistrationAsync(CancellationToken ct)
    {
        try
        {
            await Shell.Current.GoToAsync($"{nameof(Views.ScanPage)}?Mode=JudgeRegistration");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to ScanPage for JudgeRegistration");
            if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlert("Navigation Error", "Could not start judge registration.", "OK");
            }
        }
    }

    /// <summary>Scan Project QR to vote (after judge is registered).</summary>
    [RelayCommand]
    private async Task NavigateToScanAsync(CancellationToken ct)
    {
        try
        {
            await Shell.Current.GoToAsync($"{nameof(Views.ScanPage)}?Mode=ProjectScan");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to ScanPage for ProjectScan");
            if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlert("Navigation Error", "Could not start project scanning.", "OK");
            }
        }
    }

    [RelayCommand]
    private async Task ShowConnectionMetricsAsync(CancellationToken ct)
    {
        var role = "Client";
#if ANDROID
        role = _swarmService.CurrentState == Nodus.Infrastructure.Services.SwarmState.Link ? "Router (Firefly)" : "Client";
#endif
        var signal = _bleService.LastRssi != 0 ? $"{_bleService.LastRssi} dBm" : "N/A";
        var peers = _swarmService.NeighborLinkCount;

        var message = $"Rol de Nodus: {role}\nSeñal del Servidor: {signal}\nNodos Cercanos: {peers}";
        
        var page = Application.Current?.Windows[0].Page;
        if (page != null)
        {
            await page.DisplayAlertAsync("Métricas de Conexión", message, "Cerrar");
        }
    }

    // ── Sync ───────────────────────────────────────────────────────────────

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SyncPendingVotesAsync(CancellationToken ct)
    {
        if (IsSyncing) return;
        IsSyncing = true;
        try
        {
            var pendingResult = await _db.GetPendingVotesAsync(ct);
            if (pendingResult.IsFailure || pendingResult.Value?.Count == 0) return;

            int successCount = 0;
            foreach (var vote in pendingResult.Value!)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var syncResult = await _bleService.SendVoteAsync(vote, ct);
                    if (syncResult.IsSuccess)
                    {
                        vote.Status = Nodus.Shared.Models.SyncStatus.Synced;
                        vote.SyncedAtUtc = DateTime.UtcNow;
                        await _db.SaveVoteAsync(vote, ct);
                        successCount++;
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to sync vote {VoteId}", vote.Id); }
            }

            _logger.LogInformation("Sync completed: {Success}/{Total}", successCount, pendingResult.Value.Count);
            await UpdateStatusAsync(ct);
        }
        catch (OperationCanceledException) { StatusMessage = "Sync cancelled"; }
        catch (Exception ex) { _logger.LogError(ex, "Sync failed"); }
        finally { IsSyncing = false; }
    }

    public void Dispose()
    {
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        _bleService.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _swarmService.PropertyChanged -= OnSwarmServicePropertyChanged;
    }
}
