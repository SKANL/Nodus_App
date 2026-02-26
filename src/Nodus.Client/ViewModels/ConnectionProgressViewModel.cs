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
        
        // Start scanning (triggered from here or previous page, ensure it starts)
        var startResult = await _bleService.StartScanningForServerAsync();
        if (startResult.IsFailure)
        {
            MainThread.BeginInvokeOnMainThread(() => 
            {
                StatusMessage = "Error de Bluetooth";
                DetailMessage = startResult.Error;
                IsConnecting = false;
            });
        }

        // Timeout check after 15s
        _ = Task.Run(async () => 
        {
            await Task.Delay(15000);
            if (!IsConnected && IsConnecting)
            {
                MainThread.BeginInvokeOnMainThread(() => 
                {
                    StatusMessage = "Tiempo de espera agotado";
                    DetailMessage = "No se pudo conectar con el servidor.";
                    IsConnecting = false;
                });
            }
        });
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
