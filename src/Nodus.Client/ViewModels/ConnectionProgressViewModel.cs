using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Shared.Abstractions;
using Microsoft.Extensions.Logging;

namespace Nodus.Client.ViewModels;

[QueryProperty(nameof(JudgeName), "JudgeName")]
public partial class ConnectionProgressViewModel : ObservableObject
{
    private readonly IBleClientService _bleService;
    private readonly ILogger<ConnectionProgressViewModel> _logger;

    [ObservableProperty] private string _judgeName = string.Empty;
    [ObservableProperty] private string _statusMessage = "Buscando servidor...";
    [ObservableProperty] private string _detailMessage = "Asegúrate de estar cerca del Nodus Server.";
    [ObservableProperty] private bool _isConnecting = true;
    [ObservableProperty] private bool _isConnected = false;

    private IDisposable? _connectionSub;

    public ConnectionProgressViewModel(IBleClientService bleService, ILogger<ConnectionProgressViewModel> logger)
    {
        _bleService = bleService;
        _logger = logger;
    }

    public async void OnNavigatedTo()
    {
        try
        {
            await OnNavigatedToCoreAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in ConnectionProgressViewModel.OnNavigatedTo");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusMessage = "Error inesperado";
                DetailMessage = ex.Message;
                IsConnecting = false;
            });
        }
    }

    private async Task OnNavigatedToCoreAsync()
    {
        _connectionSub = _bleService.ConnectionState.Subscribe(state =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (state == "Connected")
                {
                    StatusMessage = "¡Conectado exitosamente!";
                    DetailMessage = string.IsNullOrEmpty(JudgeName) ? "Red de evaluación lista." : $"Red de evaluación lista. Hola, {Uri.UnescapeDataString(JudgeName)}!";
                    IsConnecting = false;
                    IsConnected = true;
                }
                else if (state == "Disconnected")
                {
                    StatusMessage = "La conexión se ha perdido";
                    DetailMessage = "Esperando reconexión...";
                }
            });
        });

        var startResult = await _bleService.StartScanningForServerAsync();
        if (startResult.IsFailure)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusMessage = "Error de Bluetooth";
                DetailMessage = startResult.Error ?? "No se puede iniciar el escanéo BLE. Verifica que el Bluetooth esté habilitado.";
                IsConnecting = false;
            });
            return;
        }

        // Timeout check — 15 seconds is generous for BLE discovery
        _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ =>
        {
            if (!IsConnected && IsConnecting)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    StatusMessage = "Tiempo de espera agotado";
                    DetailMessage = "No se pudo conectar con el servidor. Asegúrate de estar cerca.";
                    IsConnecting = false;
                });
            }
        }, TaskScheduler.Default);
    }

    public void OnNavigatedFrom()
    {
        _connectionSub?.Dispose();
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        _connectionSub?.Dispose();
        _bleService.StopScanning();
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        _connectionSub?.Dispose();
        // Go back two levels if opened from scan modal? 
        // Or go absolutely to Home 
        await Shell.Current.GoToAsync("//" + nameof(Views.HomePage)); // assuming Home is root
    }
}
