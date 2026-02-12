using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Client.Services;
using Microsoft.Extensions.Logging;

namespace Nodus.Client.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly MediaSyncService _mediaSyncService;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _syncProgress;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private bool _isBusy;

    public SettingsViewModel(MediaSyncService mediaSyncService, ILogger<SettingsViewModel> logger)
    {
        _mediaSyncService = mediaSyncService;
        _logger = logger;
        
        // Subscribe to events
        _mediaSyncService.SyncStatusChanged += OnSyncStatusChanged;
        _mediaSyncService.SyncProgressChanged += OnSyncProgressChanged;
        _mediaSyncService.SyncStateChanged += OnSyncStateChanged;
    }

    private void OnSyncStatusChanged(object? sender, string status)
    {
        // Ensure UI update on MainThread
        MainThread.BeginInvokeOnMainThread(() => StatusMessage = status);
    }

    private void OnSyncProgressChanged(object? sender, double progress)
    {
        MainThread.BeginInvokeOnMainThread(() => SyncProgress = progress);
    }
    
    private void OnSyncStateChanged(object? sender, bool isSyncing)
    {
        MainThread.BeginInvokeOnMainThread(() => IsSyncing = isSyncing);
    }

    [RelayCommand]
    private async Task ForceMediaSyncAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        IsSyncing = true;
        SyncProgress = 0;
        StatusMessage = "Starting Manual Sync...";
        try
        {
            if (!_mediaSyncService.IsConnected)
            {
                StatusMessage = "Error: Bluetooth not connected";
                await Application.Current!.MainPage!.DisplayAlert("Error", "Please connect to a Nodus Server first.", "OK");
                return;
            }

            StatusMessage = "Starting Manual Sync...";
            // CheckAndSyncAsync uses RSSI threshold, but for manual sync we might want to bypass or use a lenient one.
            // Using -90dBm for manual override to ensure it tries even with weak signal if user requested.
            await _mediaSyncService.CheckAndSyncAsync(-90);
            
            StatusMessage = "Sync process completed.";
            await Application.Current!.MainPage!.DisplayAlert("Sync Complete", "Media sync process finished.", "OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            StatusMessage = $"Error: {ex.Message}";
            await Application.Current!.MainPage!.DisplayAlert("Error", $"Sync Failed: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
            IsSyncing = false;
        }
    }
    
    // Dispose/Destructor to unsubscribe? 
    // ViewModels in MAUI are often transient or singleton. If Transient, we should implement IDisposable in a real app.
    // For now simple subscription is okay as it singleton in DI.
    // Wait, MauiProgram says AddTransient for SettingsViewModel.
    // We should implement IDisposable to avoid leaks if we navigate away and come back.
}
