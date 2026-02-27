using System.Security.Cryptography;
using Nodus.Shared.Security;

namespace Nodus.Shared.Services;

/// <summary>
/// Encapsulates all cryptographic operations needed when creating a Nodus event.
/// Keeps ViewModels thin: they call this service and receive ready-to-use artifacts.
/// </summary>
public sealed class EventSecurityService
{
    /// <summary>
    /// Generates all cryptographic artifacts for a new event.
    /// </summary>
    /// <param name="judgePassword">
    ///   Plain-text password shared with the judges. Used to derive an encryption key
    ///   that wraps the event's AES-256 shared key inside the QR code.
    /// </param>
    /// <returns>
    ///   A <see cref="EventSecurityArtifacts"/> value containing the salt, keys, and
    ///   the opaque payload string to embed in the judge QR code.
    /// </returns>
    public EventSecurityArtifacts GenerateArtifacts(string judgePassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(judgePassword);

        // 16-byte salt for PBKDF2
        Span<byte> saltBytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(saltBytes);
        var saltBase64 = Convert.ToBase64String(saltBytes);

        // 32-byte AES-256 shared event key
        Span<byte> sharedKeyBytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(sharedKeyBytes);
        var sharedKeyBase64 = Convert.ToBase64String(sharedKeyBytes);

        // Wrap the shared key with the password-derived key (PBKDF2 + AES-GCM)
        var derivedKey = CryptoHelper.DeriveKeyFromPassword(judgePassword, saltBytes.ToArray());
        var encryptedKeyBlob = CryptoHelper.Encrypt(sharedKeyBytes.ToArray(), derivedKey);
        var encryptedKeyBase64 = Convert.ToBase64String(encryptedKeyBlob);

        // Opaque payload format: "<saltBase64>|<encryptedKeyBase64>"
        var judgeQrPayload = $"{saltBase64}|{encryptedKeyBase64}";

        return new EventSecurityArtifacts(
            SaltBase64: saltBase64,
            SharedKeyBase64: sharedKeyBase64,
            JudgeQrPayload: judgeQrPayload);
    }
}

/// <summary>
/// Immutable value object holding the cryptographic artifacts produced for one event.
/// </summary>
/// <param name="SaltBase64">Base-64-encoded 16-byte PBKDF2 salt. Stored on the Event record.</param>
/// <param name="SharedKeyBase64">
///   Base-64-encoded 32-byte AES-256 key.
///   Stored on the Event record for admin use (already protected by the DB layer).
/// </param>
/// <param name="JudgeQrPayload">
///   Opaque string to embed in the judge QR code.
///   Format: "&lt;saltBase64&gt;|&lt;encryptedSharedKeyBase64&gt;"
/// </param>
public sealed record EventSecurityArtifacts(
    string SaltBase64,
    string SharedKeyBase64,
    string JudgeQrPayload);
