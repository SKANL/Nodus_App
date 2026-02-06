using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Shared.Models;
using Nodus.Shared.Services;
using System.Security.Cryptography;
using System.Text;
using QRCoder;

namespace Nodus.Server.ViewModels;

public partial class CreateEventViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly Services.BleServerService _bleService;

    [ObservableProperty] private string _eventName = string.Empty;
    [ObservableProperty] private string _judgePassword = string.Empty;
    [ObservableProperty] private string _categories = "Software, Hardware, Innovation";
    
    [ObservableProperty] private ImageSource? _judgeQrCode;
    [ObservableProperty] private ImageSource? _studentQrCode;
    [ObservableProperty] private bool _isGenerated;

    public CreateEventViewModel(DatabaseService db, Services.BleServerService bleService)
    {
        _db = db;
        _bleService = bleService;
    }

    [RelayCommand]
    private async Task CreateAndGenerate()
    {
        if (string.IsNullOrWhiteSpace(EventName) || string.IsNullOrWhiteSpace(JudgePassword))
            return;

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
        await _db.SaveEventAsync(newEvent);

        // 4. Generate QR Codes
        JudgeQrCode = GenerateQrImage($"nodus://judge?eid={newEvent.Id}&data={System.Net.WebUtility.UrlEncode(qrPayload)}");
        StudentQrCode = GenerateQrImage($"http://192.168.1.1:5000/register?eid={newEvent.Id}");
        
        IsGenerated = true;

        // 5. Start Firefly Protocol (Advertising)
        await _bleService.StartAdvertisingAsync(EventName);
    }

    private ImageSource GenerateQrImage(string content)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrCodeAsAsBytes = qrCode.GetGraphic(20);
        return ImageSource.FromStream(() => new MemoryStream(qrCodeAsAsBytes));
    }
}
