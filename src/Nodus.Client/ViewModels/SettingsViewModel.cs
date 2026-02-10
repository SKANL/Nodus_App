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
    private bool _isBusy;

    public SettingsViewModel(MediaSyncService mediaSyncService, ILogger<SettingsViewModel> logger)
    {
        _mediaSyncService = mediaSyncService;
        _logger = logger;
    }

    [RelayCommand]
    private async Task ForceMediaSyncAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Starting Manual Sync...";
        try
        {
            // Simulate a strong signal (-40 RSSI) to bypass the -60 threshold
            await _mediaSyncService.CheckAndSyncAsync(-40);
            StatusMessage = "Sync triggered. Check logs.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
