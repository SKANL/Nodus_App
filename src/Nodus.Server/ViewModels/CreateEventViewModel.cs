using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nodus.Shared.Models;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Services;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using QRCoder;

namespace Nodus.Server.ViewModels;

public partial class CreateEventViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly Services.BleServerService _bleService;
    private readonly EventSecurityService _security;
    private readonly ILogger<CreateEventViewModel> _logger;

    // ── Observable State ───────────────────────────────────────────────────
    [ObservableProperty] private string _eventName = string.Empty;
    [ObservableProperty] private string _judgePassword = string.Empty;
    [ObservableProperty] private string _categories = "Software, Hardware, Innovación";
    [ObservableProperty] private string _webPortalUrl = string.Empty;
    [ObservableProperty] private string _networkAddresses = string.Empty;

    [ObservableProperty] private ImageSource? _judgeQrCode;
    [ObservableProperty] private ImageSource? _studentQrCode;
    [ObservableProperty] private bool _isGenerated;

    public CreateEventViewModel(
        IDatabaseService db,
        Services.BleServerService bleService,
        EventSecurityService security,
        ILogger<CreateEventViewModel> logger)
    {
        _db = db;
        _bleService = bleService;
        _security = security;
        _logger = logger;

        var localIp = GetLocalIpAddress();
        NetworkAddresses = localIp;
        WebPortalUrl = localIp;
    }

    [RelayCommand]
    private async Task CreateAndGenerate()
    {
        _logger.LogDebug("CreateAndGenerate triggered. EventName='{Name}'", EventName);

        if (string.IsNullOrWhiteSpace(EventName) || string.IsNullOrWhiteSpace(JudgePassword))
        {
            _logger.LogWarning("Validation failed: EventName or JudgePassword is empty");
            await ShowAlertAsync("Validación", "Ingresa el nombre del evento y la contraseña del juez.");
            return;
        }

        try
        {
            // 1. Delegate all crypto work to dedicated service (SRP)
            var artifacts = _security.GenerateArtifacts(JudgePassword);

            // 2. Persist event to MongoDB via IDatabaseService
            var newEvent = new Event
            {
                Name = EventName,
                GlobalSalt = artifacts.SaltBase64,
                RubricJson = Categories,
                SharedAesKeyEncrypted = artifacts.SharedKeyBase64,
                IsActive = true
            };
            var eventId = newEvent.Id; // Capture before save to prevent any ID mutation

            var saveResult = await _db.SaveEventAsync(newEvent);
            if (saveResult.IsFailure)
            {
                _logger.LogError("Failed to persist event: {Error}", saveResult.Error);

                // Provide an actionable error that explains the most common cause.
                var userMessage = saveResult.Error ?? "Error desconocido";
                if (userMessage.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
                    || userMessage.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
                    || userMessage.Contains("A timeout", StringComparison.OrdinalIgnoreCase)
                    || userMessage.Contains("ServerSelectionTimeout", StringComparison.OrdinalIgnoreCase))
                {
                    userMessage = "No se pudo conectar con MongoDB.\n\n"
                        + "• Desarrollo local: asegúrate de que MongoDB esté corriendo en localhost:27017\n"
                        + "• Producción: verifica el URI de MongoDB Atlas en AppSecrets.cs";
                }

                await ShowAlertAsync("Error al guardar", userMessage);
                return;
            }
            _logger.LogInformation("Event '{Name}' persisted with ID: {Id}", EventName, eventId);

            // 3. Generate QR codes
            var judgeDeepLink = $"nodus://judge?eid={eventId}&data={System.Net.WebUtility.UrlEncode(artifacts.JudgeQrPayload)}";
            JudgeQrCode = GenerateQrImage(judgeDeepLink);

            var studentUrl = $"{WebPortalUrl.TrimEnd('/')}/register?eid={eventId}";
            StudentQrCode = GenerateQrImage(studentUrl);
            _logger.LogInformation("Student registration URL: {Url}", studentUrl);

            IsGenerated = true;

            // 4. Start Firefly BLE advertising
            await _bleService.StartAdvertisingAsync(EventName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in CreateAndGenerate");
            await ShowAlertAsync("Error inesperado", ex.Message);
        }
    }

    [RelayCommand]
    private async Task PresentQrs()
    {
        if (!IsGenerated) return;
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is not null)
            await page.Navigation.PushModalAsync(new Nodus.Server.Views.QrProjectionWindow(this));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ImageSource GenerateQrImage(string content)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var code = new PngByteQRCode(data);
        var bytes = code.GetGraphic(20);
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    private static async Task ShowAlertAsync(string title, string message)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is not null)
            await page.DisplayAlertAsync(title, message, "OK");
    }

    /// <summary>Returns the primary LAN IPv4 address for the student portal QR default URL.</summary>
    private static string GetLocalIpAddress()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        return $"http://{ua.Address}:5000";
                }
            }
        }
        catch { /* fall through to default */ }
        return "http://localhost:5000";
    }
}

