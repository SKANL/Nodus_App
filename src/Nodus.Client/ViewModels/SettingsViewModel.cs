using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Client.Services;
using Microsoft.Extensions.Logging;
using Nodus.Infrastructure.Services;
using Nodus.Shared.Abstractions;

namespace Nodus.Client.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly MediaSyncService _mediaSyncService;
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private string _statusMessage = "Listo";

    [ObservableProperty]
    private double _syncProgress;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _pendingVotesCount;

    [ObservableProperty]
    private int _pendingMediaCount;

    [ObservableProperty]
    private string _syncStatsText = "Cargando...";

    public SettingsViewModel(
        MediaSyncService mediaSyncService,
        IDatabaseService databaseService,
        ILogger<SettingsViewModel> logger)
    {
        _mediaSyncService = mediaSyncService;
        _databaseService = databaseService;
        _logger = logger;

        // Subscribe to events
        _mediaSyncService.SyncStatusChanged += OnSyncStatusChanged;
        _mediaSyncService.SyncProgressChanged += OnSyncProgressChanged;
        _mediaSyncService.SyncStateChanged += OnSyncStateChanged;

        // Load initial stats
        _ = LoadSyncStatsAsync();
    }

    private async Task LoadSyncStatsAsync()
    {
        try
        {
            var statsResult = await _databaseService.GetSyncStatsAsync();
            if (statsResult.IsSuccess && statsResult.Value != null)
            {
                var stats = statsResult.Value;
                PendingVotesCount = stats.PendingVotes;
                PendingMediaCount = stats.PendingMedia;
                SyncStatsText = $"{stats.SyncedVotes}/{stats.TotalVotes} sincronizados ({stats.SyncPercentage:F0}%)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sync stats");
            SyncStatsText = "Error al cargar estadísticas";
        }
    }

    private void OnSyncStatusChanged(object? sender, string status)
    {
        // Ensure UI update on MainThread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusMessage = status;
            // Reload stats when sync completes
            if (status.Contains("complete", StringComparison.OrdinalIgnoreCase))
            {
                _ = LoadSyncStatsAsync();
            }
        });
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
        StatusMessage = "Iniciando Sincronización Manual...";
        try
        {
            if (!_mediaSyncService.IsConnected)
            {
                StatusMessage = "Error: Bluetooth no conectado";
                await ShowAlertAsync("Error", "Primero conéctate a un Servidor Nodus.", "OK");
                return;
            }

            StatusMessage = "Iniciando Sincronización Manual...";
            // Using -90dBm for manual override to ensure it tries even with a weak signal.
            await _mediaSyncService.CheckAndSyncAsync(-90);

            StatusMessage = "Proceso de sincronización completado.";
            await ShowAlertAsync("Sincronización Completa", "El proceso de sincronización de archivos ha finalizado.", "OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            StatusMessage = $"Error: {ex.Message}";
            await ShowAlertAsync("Error", $"Sincronización Fallida: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
            IsSyncing = false;
            await LoadSyncStatsAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshStatsAsync()
    {
        await LoadSyncStatsAsync();
    }

    public void Dispose()
    {
        // Unsubscribe from events to prevent memory leaks
        _mediaSyncService.SyncStatusChanged -= OnSyncStatusChanged;
        _mediaSyncService.SyncProgressChanged -= OnSyncProgressChanged;
        _mediaSyncService.SyncStateChanged -= OnSyncStateChanged;
    }

    /// <summary>Safe page-getter: avoids Windows[0] index-out-of-range during transitions.</summary>
    private static Task ShowAlertAsync(string title, string message, string button)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page is not null
            ? page.DisplayAlertAsync(title, message, button)
            : Task.CompletedTask;
    }
}
