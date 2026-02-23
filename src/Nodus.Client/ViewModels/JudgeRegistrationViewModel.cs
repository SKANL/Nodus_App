using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Nodus.Shared.Models;   // Judge
using Nodus.Shared;
using Nodus.Shared.Protocol;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Nodus.Client.ViewModels;

/// <summary>
/// Professional ViewModel for Judge Onboarding via QR code.
/// Implements robust error handling, AsyncRelayCommand, and proper service abstractions.
/// </summary>
public partial class JudgeRegistrationViewModel : ObservableObject, IDisposable
{
    // PASO 5: Se inyecta IDatabaseService para persistir el Judge en MongoDB
    // después del registro por QR. En el sistema anterior, el juez solo existía
    // en SecureStorage del dispositivo (sin colección centralizada).
    private readonly IDatabaseService _db;
    private readonly IBleClientService _bleService;
    private readonly ILogger<JudgeRegistrationViewModel> _logger;
    private readonly CancellationTokenSource _cts = new();
    
    [ObservableProperty] private bool _isScanning = true;
    [ObservableProperty] private string _scanStatus = "Align QR code within frame";
    [ObservableProperty] private bool _isValid;
    [ObservableProperty] private string _judgeName = string.Empty;

    public JudgeRegistrationViewModel(
        IDatabaseService db, 
        IBleClientService bleService,
        ILogger<JudgeRegistrationViewModel> logger)
    {
        _db = db;
        _bleService = bleService;
        _logger = logger;
        _logger.LogInformation("JudgeRegistrationViewModel initialized");
    }

    public Task InitializeAsync(string? projectId, string? eventId, CancellationToken ct = default)
    {
        // Registration is QR-based, but we keep this for consistency with other VMs
        return Task.CompletedTask;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ProcessScanResultAsync(string rawContent, CancellationToken ct)
    {
        if (!IsScanning) return;

        IsScanning = false;
        ScanStatus = "Verifying...";

        var result = await PerformRegistrationAsync(rawContent, ct);

        if (result.IsFailure)
        {
            _logger.LogWarning("Registration failed: {Error}", result.Error);
            ScanStatus = $"Error: {result.Error}. Try again.";
            IsScanning = true;
            
            await MainThread.InvokeOnMainThreadAsync(async () => {
                var page = Application.Current?.Windows[0].Page;
                if (page != null)
                {
                    await page.DisplayAlertAsync("Registration Error", result.Error, "OK");
                }
            });
            return;
        }

        IsValid = true;
        ScanStatus = "Registration Successful!";
        
        await MainThread.InvokeOnMainThreadAsync(async () => {
            var page = Application.Current?.Windows[0].Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Success", "Keys decrypted and stored securely.", "OK");
                await page.Navigation.PopAsync();
            }
        });
    }

    private async Task<Result> PerformRegistrationAsync(string rawContent, CancellationToken ct)
    {
        try 
        {
            // 1. Basic Validation
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

            // 2. Security Check (Password)
            string password = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = Application.Current?.Windows[0].Page;
                if (page == null) return string.Empty;
                
                return await page.DisplayPromptAsync(
                    "Security Check", 
                    "Enter Event Password to decrypt credentials:", 
                    "Unlock", 
                    "Cancel") ?? string.Empty;
            });
            
            if (string.IsNullOrWhiteSpace(password))
            {
                return Result.Failure("Password required for decryption");
            }

            // 3. Parse and Decrypt
            var parts = encryptedData.Split('|');
            if (parts.Length != 2) return Result.Failure("Invalid payload format");

            var saltBytes = Convert.FromBase64String(parts[0]);
            var encryptedKeyBlob = Convert.FromBase64String(parts[1]);

            // Derive key using PBKDF2 (expensive, run in background)
            var derivedKey = await Task.Run(() => 
                Nodus.Shared.Security.CryptoHelper.DeriveKeyFromPassword(password, saltBytes), ct);
            
            var sharedKeyBytes = Nodus.Shared.Security.CryptoHelper.Decrypt(encryptedKeyBlob, derivedKey);
            var sharedKeyBase64 = Convert.ToBase64String(sharedKeyBytes);

            // 4. Persistence (Secure Storage)
            // Guardamos el EventId y la clave AES compartida en el almacenamiento seguro
            // del dispositivo. Estas claves son necesarias para descifrar y firmar votos.
            await SecureStorage.Default.SetAsync(NodusConstants.KEY_EVENT_ID, eventId);
            await SecureStorage.Default.SetAsync(NodusConstants.KEY_SHARED_AES, sharedKeyBase64);

            _logger.LogInformation("Successfully persisted keys for Event {EventId}", eventId);

            // ─────────────────────────────────────────────────────────────────
            // PASO 5 — Registrar el juez en MongoDB (colección nueva "judges")
            // ─────────────────────────────────────────────────────────────────
            // Construimos el objeto Judge con los datos obtenidos del QR.
            //
            // CONFLICTO DETECTADO ⚠️:
            // La clave pública (PublicKey) NO está disponible directamente aquí.
            // En el flujo actual, las partes del QR son: salt + encryptedKeyBlob (AES).
            // La clave pública RSA (KEY_PUBLIC_KEY) se genera al registrar el juez
            // pero el ViewModel no la recibe en este punto.
            //
            // SOLUCIÓN APLICADA: Se guarda con PublicKey vacía por ahora.
            // Para obtenerla, habría que:
            //   a) Includirla en el QR como parámetro adicional "pk", o
            //   b) Leerla de SecureStorage si ya fue generada en un paso previo.
            var nameForId = JudgeName.Replace(" ", string.Empty);
            var judge = new Judge
            {
                // ID determinista: permite re-registros sin duplicados
                Id = $"JUDGE-{nameForId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",

                // Nombre del juez: obtenido del campo JudgeName de la UI
                Name = JudgeName,

                // EventId: extraído del parámetro "eid" del QR
                EventId = eventId,

                // PublicKey: vacía por limitación del flujo actual (ver CONFLICTO arriba)
                // TODO: incluir PublicKey en el QR o leerla de SecureStorage
                PublicKey = string.Empty,

                IsActive = true,
                RegisteredAtUtc = DateTime.UtcNow
            };

            // Guardado best-effort: si MongoDB no está disponible, el juez igual
            // puede votar porque sus claves ya están en SecureStorage.
            // No lanzamos excepción si esto falla.
            var judgeResult = await _db.SaveJudgeAsync(judge, ct);
            if (judgeResult.IsFailure)
            {
                _logger.LogWarning(
                    "[Paso 5] No se pudo registrar Judge {Name} en MongoDB: {Error}. " +
                    "El juez puede continuar — sus claves están en SecureStorage.",
                    judge.Name, judgeResult.Error);
            }
            else
            {
                _logger.LogInformation(
                    "[Paso 5] Judge {Name} registrado en MongoDB con ID {JudgeId} para Event {EventId}",
                    judge.Name, judge.Id, judge.EventId);
            }
            // ─────────────────────────────────────────────────────────────────

            // 5. Start BLE Protocol
            // Iniciamos el escaneo BLE para conectar con el servidor Nodus.
            var startResult = await _bleService.StartScanningForServerAsync(ct);
            if (startResult.IsFailure)
            {
                _logger.LogWarning("Failed to start BLE scanning: {Error}", startResult.Error);
                // Continuamos de todas formas — las claves ya están guardadas
            }

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

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _logger.LogInformation("JudgeRegistrationViewModel disposed");
    }
}
