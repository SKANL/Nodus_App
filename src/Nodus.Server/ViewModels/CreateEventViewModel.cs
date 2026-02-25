using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nodus.Shared.Models;
using Nodus.Shared.Services;
using Nodus.Shared.Abstractions;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using QRCoder;

namespace Nodus.Server.ViewModels;

public partial class CreateEventViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly Services.BleServerService _bleService;
    private readonly Nodus.Infrastructure.Services.MongoDbService _mongoDb;
    private readonly ILogger<CreateEventViewModel> _logger;

    [ObservableProperty] private string _eventName = string.Empty;
    [ObservableProperty] private string _judgePassword = string.Empty;
    [ObservableProperty] private string _categories = "Software, Hardware, Innovation";
    [ObservableProperty] private string _webPortalUrl = GetLocalIpAddress();
    
    [ObservableProperty] private ImageSource? _judgeQrCode;
    [ObservableProperty] private ImageSource? _studentQrCode;
    [ObservableProperty] private bool _isGenerated;
    [ObservableProperty] private string _networkAddresses = string.Empty;

    public CreateEventViewModel(
        IDatabaseService db, 
        Services.BleServerService bleService,
        Infrastructure.Services.MongoDbService mongoDb,
        ILogger<CreateEventViewModel> logger)
    {
        _db = db;
        _bleService = bleService;
        _mongoDb = mongoDb;
        _logger = logger;
        NetworkAddresses = GetLocalIpAddress();
    }

    [RelayCommand]
    private async Task CreateAndGenerate()
    {
        _logger.LogDebug("CreateAndGenerate Command Triggered. Name='{Name}'", EventName);
        
        if (string.IsNullOrWhiteSpace(EventName) || string.IsNullOrWhiteSpace(JudgePassword))
        {
            _logger.LogWarning("Validation failed: missing name or password");
            var page = Application.Current?.Windows[0].Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Error de Validación", "Por favor ingrese el nombre del evento y la contraseña", "OK");
            }
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine("[DEBUG] Starting Generation Process...");
            // 1. Generate Security Artifacts
            // Salt: 16 bytes for PBKDF2
            var saltBytes = new byte[16];
            RandomNumberGenerator.Fill(saltBytes);
            var saltBase64 = Convert.ToBase64String(saltBytes);

            // Shared Key: 32 bytes (AES-256) actually used for the event
            var sharedKeyBytes = new byte[32];
            RandomNumberGenerator.Fill(sharedKeyBytes);
            var sharedKeyBase64 = Convert.ToBase64String(sharedKeyBytes);

            // 2. Encrypt Shared Key with Password
            // This ensures only judges with the password can unlock the Shared Key from the QR
            var derivedPasswordKey = Nodus.Shared.Security.CryptoHelper.DeriveKeyFromPassword(JudgePassword, saltBytes);
            var encryptedSharedKeyBlob = Nodus.Shared.Security.CryptoHelper.Encrypt(sharedKeyBytes, derivedPasswordKey);
            var encryptedSharedKeyBase64 = Convert.ToBase64String(encryptedSharedKeyBlob);
            
            // Payload: Salt|EncryptedKey
            var qrPayload = $"{saltBase64}|{encryptedSharedKeyBase64}";

            // 3. Save Event
            var newEvent = new Event
            {
                Name = EventName,
                GlobalSalt = saltBase64,
                RubricJson = Categories,
                SharedAesKeyEncrypted = sharedKeyBase64, // Persist for Admin use
                IsActive = true
            };

            // IMPORTANT: Capture the GUID Id BEFORE saving to the database.
            // LiteDB can replace string Ids with auto-incremented integers during Upsert,
            // which would corrupt the URL embedded in the QR code.
            var eventIdForQr = newEvent.Id;

            var saveResult = await _db.SaveEventAsync(newEvent);
            if (saveResult.IsFailure)
            {
                var page = Application.Current?.Windows[0].Page;
                if (page != null)
                {
                    await page.DisplayAlertAsync("Error", 
                        $"Error al guardar el evento localmente: {saveResult.Error}", "OK");
                }
                return;
            }

            // 4. Sync to Cloud immediately for Web Portal registration
            _ = Task.Run(async () => {
                var cloudResult = await _mongoDb.SaveEventAsync(newEvent);
                if (cloudResult.IsFailure)
                {
                    System.Diagnostics.Debug.WriteLine($"[CLOUD] Failed to push event to Atlas: {cloudResult.Error}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[CLOUD] Event {newEvent.Name} pushed to Atlas successfully.");
                }
            });

            // 5. Generate QR Codes using the pre-saved GUID Id
            JudgeQrCode = GenerateQrImage($"nodus://judge?eid={eventIdForQr}&data={System.Net.WebUtility.UrlEncode(qrPayload)}");
            var studentRegistrationUrl = $"{WebPortalUrl.TrimEnd('/')}/register?eid={eventIdForQr}";
            StudentQrCode = GenerateQrImage(studentRegistrationUrl);
            _logger.LogInformation("Generated student QR with URL: {Url}", studentRegistrationUrl);
            _logger.LogInformation("Event '{Name}' created with ID: {Id}", EventName, eventIdForQr);
            
            IsGenerated = true;

            // 5. Start Firefly Protocol (Advertising)
            await _bleService.StartAdvertisingAsync(EventName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event");
            var page = Application.Current?.Windows[0].Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Error", 
                    $"Error al crear el evento: {ex.Message}", "OK");
            }
        }
    }


    private ImageSource GenerateQrImage(string content)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrCodeAsBytes = qrCode.GetGraphic(20);
        return ImageSource.FromStream(() => new MemoryStream(qrCodeAsBytes));
    }

    /// <summary>Detects the machine's primary local IP for the student QR URL default.</summary>
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
        catch { /* Fallback below */ }
        return "http://localhost:5000";
    }
}
