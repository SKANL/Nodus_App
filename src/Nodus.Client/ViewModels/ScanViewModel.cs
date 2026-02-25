using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Client.Views;
using Nodus.Shared.Common;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;
using Microsoft.Extensions.Logging;
using System.Web;

namespace Nodus.Client.ViewModels;

/// <summary>
/// Professional ScanViewModel with proper error handling and async patterns.
/// </summary>
public partial class ScanViewModel : ObservableObject
{
    private readonly ILogger<ScanViewModel> _logger;
    private readonly IDatabaseService _db;
    private readonly IBleClientService _bleService;

    [ObservableProperty]
    private bool _isScanning = true;

    public ScanViewModel(
        ILogger<ScanViewModel> logger,
        IDatabaseService db,
        IBleClientService bleService)
    {
        _logger = logger;
        _db = db;
        _bleService = bleService;
    }

    [RelayCommand]
    private async Task CancelAsync(CancellationToken ct)
    {
        try
        {
            IsScanning = false;
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate back");
        }
    }

    public async Task ProcessQrCodeAsync(string qrContent, CancellationToken ct = default)
    {
        if (!IsScanning) return;
        IsScanning = false;

        try
        {
            // Haptic Feedback (Optimistic Feedback)
            if (Vibration.Default.IsSupported)
                 Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(100));

            // 1. Project QR: "pid=PROJ-123&name=..."
            if (qrContent.Contains("pid="))
            {
                var result = await ProcessProjectQrAsync(qrContent, ct);
                if (result.IsSuccess) return;
                
                _logger.LogWarning("Failed to process project QR: {Error}", result.Error);
            }

            // 2. Judge/Event QR: nodus://judge?eid=...&data=...
            if (qrContent.StartsWith("nodus://judge"))
            {
                var result = await ProcessEventQrAsync(qrContent, ct);
                if (result.IsSuccess) return;
                
                _logger.LogWarning("Failed to process event QR: {Error}", result.Error);
            }

            // Unknown QR
            await ShowAlertAsync("Scan Result", $"Unknown QR: {qrContent}", "OK", ct: ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("QR processing cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing QR code");
            await ShowAlertAsync("Error", "Failed to process QR code", "OK", ct: ct);
        }
        finally
        {
            // Resume scanning if not navigated
            IsScanning = true;
        }
    }

    private async Task<Result> ProcessProjectQrAsync(string qrContent, CancellationToken ct)
    {
        try
        {
            var query = ParseQueryString(qrContent);
            if (!query.TryGetValue("pid", out var pid))
            {
                return Result.Failure("Missing project ID in QR");
            }

            await Shell.Current.GoToAsync($"{nameof(VotingPage)}?ProjectId={pid}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure("Failed to navigate to voting page", ex);
        }
    }

    private async Task<Result> ProcessEventQrAsync(string rawContent, CancellationToken ct)
    {
        try 
        {
            if (string.IsNullOrWhiteSpace(rawContent) || !rawContent.StartsWith("nodus://judge"))
            {
                return Result.Failure("Invalid QR format");
            }

            var uri = new Uri(rawContent);
            var query = HttpUtility.ParseQueryString(uri.Query);
            var encryptedData = query["data"];
            var eventId = query["eid"];

            if (string.IsNullOrEmpty(encryptedData) || string.IsNullOrEmpty(eventId))
            {
                return Result.Failure("Incomplete QR Data");
            }

            // Prompt for Event Password
            string? password = await ShowPromptAsync(
                "Security Check", 
                "Enter Event Password to decrypt credentials:", 
                maxLength: 50, 
                ct: ct);
            
            if (string.IsNullOrWhiteSpace(password))
            {
                return Result.Failure("Password required for decryption");
            }

            // Parse and Decrypt
            var parts = encryptedData.Split('|');
            if (parts.Length != 2) return Result.Failure("Invalid payload format");

            var saltBytes = Convert.FromBase64String(parts[0]);
            var encryptedKeyBlob = Convert.FromBase64String(parts[1]);

            // Derive key using PBKDF2 (expensive, run in background)
            var derivedKey = await Task.Run(() => 
                Nodus.Shared.Security.CryptoHelper.DeriveKeyFromPassword(password, saltBytes), ct);
            
            var sharedKeyBytes = Nodus.Shared.Security.CryptoHelper.Decrypt(encryptedKeyBlob, derivedKey);
            var sharedKeyBase64 = Convert.ToBase64String(sharedKeyBytes);

            // Generate My Identity
            var myKeys = Nodus.Shared.Security.CryptoHelper.GenerateSigningKeys();
            
            // Prompt for Judge Name
            string? judgeName = await ShowPromptAsync(
                "Identity", 
                "Enter your name (e.g. Dr. Brown):", 
                maxLength: 20,
                ct: ct);
                
            if (string.IsNullOrWhiteSpace(judgeName)) 
                judgeName = "Judge " + new Random().Next(100, 999);

            // Persistence (Secure Storage)
            await SecureStorage.Default.SetAsync(Nodus.Shared.NodusConstants.KEY_EVENT_ID, eventId);
            await SecureStorage.Default.SetAsync(Nodus.Shared.NodusConstants.KEY_SHARED_AES, sharedKeyBase64);
            await SecureStorage.Default.SetAsync(Nodus.Shared.NodusConstants.KEY_PRIVATE_KEY, myKeys.PrivateKeyBase64);
            await SecureStorage.Default.SetAsync(Nodus.Shared.NodusConstants.KEY_PUBLIC_KEY, myKeys.PublicKeyBase64);
            await SecureStorage.Default.SetAsync(Nodus.Shared.NodusConstants.KEY_JUDGE_NAME, judgeName);

            _logger.LogInformation("Successfully persisted keys for Event {EventId}", eventId);

            var nameForId = judgeName.Replace(" ", string.Empty);
            var judge = new Judge
            {
                Id = $"JUDGE-{nameForId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                Name = judgeName,
                EventId = eventId,
                PublicKey = myKeys.PublicKeyBase64,
                IsActive = true,
                RegisteredAtUtc = DateTime.UtcNow
            };

            var judgeResult = await _db.SaveJudgeAsync(judge, ct);
            if (judgeResult.IsFailure)
            {
                _logger.LogWarning("No se pudo registrar Judge {Name} en MongoDB: {Error}.", judge.Name, judgeResult.Error);
            }

            // Start BLE Protocol
            var startResult = await _bleService.StartScanningForServerAsync(ct);
            if (startResult.IsFailure)
            {
                _logger.LogWarning("Failed to start BLE scanning: {Error}", startResult.Error);
            }

            await ShowAlertAsync(
                "¡Sesión iniciada!",
                $"Hola, {judgeName} 👋\n\nEstás registrado en el evento. Ahora puedes escanear los QR de los proyectos para comenzar a evaluar.",
                "Empezar a evaluar",
                ct: ct);
            await Shell.Current.GoToAsync("..");

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("Registration cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during registration");
            return Result.Failure("Registration failed with an internal error", ex);
        }
    }

    private Dictionary<string, string> ParseQueryString(string text)
    {
        var dict = new Dictionary<string, string>();
        
        // Remove scheme if present
        var query = text.Contains('?') ? text.Split('?')[1] : text;
        
        var parts = query.Split('&');
        foreach (var part in parts)
        {
            var kv = part.Split('=');
            if (kv.Length == 2)
            {
                dict[kv[0]] = kv[1];
            }
        }
        return dict;
    }

    private async Task<bool> ShowAlertAsync(string title, string message, string accept, string? cancel = null, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested || Application.Current?.Windows.Count == 0)
            return false;

        try
        {
            var page = Application.Current.Windows[0].Page;
            if (page == null)
            {
                _logger.LogWarning("Cannot show alert: Page is null");
                return false;
            }

            if (cancel != null)
            {
                return await page.DisplayAlertAsync(title, message, accept, cancel);
            }
            else
            {
                await page.DisplayAlertAsync(title, message, accept);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show alert");
            return false;
        }
    }

    private async Task<string?> ShowPromptAsync(string title, string message, int maxLength = 50, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested || Application.Current?.Windows.Count == 0)
            return null;

        try
        {
            var page = Application.Current.Windows[0].Page;
            if (page == null)
            {
                _logger.LogWarning("Cannot show prompt: Page is null");
                return null;
            }

            return await page.DisplayPromptAsync(
                title, 
                message, 
                maxLength: maxLength, 
                keyboard: Keyboard.Text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show prompt");
            return null;
        }
    }
}
