using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Client.Views;
using Nodus.Shared.Common;
using Microsoft.Extensions.Logging;

namespace Nodus.Client.ViewModels;

/// <summary>
/// Professional ScanViewModel with proper error handling and async patterns.
/// </summary>
public partial class ScanViewModel : ObservableObject
{
    private readonly ILogger<ScanViewModel> _logger;

    [ObservableProperty]
    private bool _isScanning = true;

    public ScanViewModel(ILogger<ScanViewModel> logger)
    {
        _logger = logger;
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

            // 2. Judge/Event QR: nodus://setup?id=...&name=...&salt=...
            if (qrContent.StartsWith("nodus://setup") || qrContent.Contains("salt="))
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

    private async Task<Result> ProcessEventQrAsync(string qrContent, CancellationToken ct)
    {
        try
        {
            var query = ParseQueryString(qrContent);
            if (!query.TryGetValue("name", out var eventName) || 
                !query.TryGetValue("salt", out var saltHex))
            {
                return Result.Failure("Invalid event QR format");
            }

            bool accept = await ShowAlertAsync(
                "Join Event", 
                $"Do you want to join '{eventName}' as a Judge?", 
                "Yes", "Cancel", ct);

            if (!accept) return Result.Failure("User cancelled");

            // Prompt for Event Password
            string? password = await ShowPromptAsync(
                "Security", 
                $"Enter password for {eventName}:", 
                maxLength: 20, 
                ct: ct);

            if (string.IsNullOrWhiteSpace(password))
            {
                return Result.Failure("Password required");
            }

            // Derive Shared AES Key
            var salt = Convert.FromBase64String(saltHex);
            var sharedKey = Nodus.Shared.Security.CryptoHelper.DeriveKeyFromPassword(password, salt);
            var sharedKeyBase64 = Convert.ToBase64String(sharedKey);

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

            // Save to Secure Storage
            await SecureStorage.Default.SetAsync(Nodus.Shared.NodusConstants.KEY_EVENT_ID, query.GetValueOrDefault("id", "unknown"));
            await SecureStorage.Default.SetAsync(Nodus.Shared.NodusConstants.KEY_SHARED_AES, sharedKeyBase64);
            await SecureStorage.Default.SetAsync(Nodus.Shared.NodusConstants.KEY_PRIVATE_KEY, myKeys.PrivateKeyBase64);
            await SecureStorage.Default.SetAsync(Nodus.Shared.NodusConstants.KEY_PUBLIC_KEY, myKeys.PublicKeyBase64);
            await SecureStorage.Default.SetAsync(Nodus.Shared.NodusConstants.KEY_JUDGE_NAME, judgeName);

            _logger.LogInformation("Judge {JudgeName} registered successfully", judgeName);

            await ShowAlertAsync("Success", "You are now registered!", "OK", ct: ct);
            await Shell.Current.GoToAsync("..");
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event QR processing failed");
            return Result.Failure("Failed to process event QR", ex);
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
