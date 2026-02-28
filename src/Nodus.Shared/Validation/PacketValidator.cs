using Nodus.Shared.Common;
using Nodus.Shared.Protocol;
using Nodus.Shared.Security;

namespace Nodus.Shared.Validation;

/// <summary>
/// Professional packet validation layer with comprehensive security checks.
/// Implements anti-replay, timestamp validation, and signature verification
/// as per 04.Security.Identity.md specification.
/// </summary>
public static class PacketValidator
{
    private static readonly TimeSpan MaxPacketAge = TimeSpan.FromHours(2);
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Validates and parses a raw packet with comprehensive security checks.
    /// </summary>
    public static Result<NodusPacket> ValidateAndParse(
        byte[] data,
        PacketTracker tracker)
    {
        if (data == null || data.Length == 0)
        {
            return Result<NodusPacket>.Failure("Packet data is null or empty");
        }

        try
        {
            // 1. Deserialize
            var jsonString = System.Text.Encoding.UTF8.GetString(data);
            var packet = System.Text.Json.JsonSerializer.Deserialize<NodusPacket>(jsonString);
            if (packet == null)
            {
                return Result<NodusPacket>.Failure("Failed to deserialize packet");
            }

            // 2. Replay Detection
            if (!tracker.TryProcess(packet.Id))
            {
                return Result<NodusPacket>.Failure($"Replay detected: Packet {packet.Id} already processed");
            }

            // 3. Timestamp Validation
            var timestampResult = ValidateTimestamp(packet.Timestamp);
            if (timestampResult.IsFailure)
            {
                return Result<NodusPacket>.Failure(timestampResult.Error);
            }

            return Result<NodusPacket>.Success(packet);
        }
        catch (Exception ex)
        {
            return Result<NodusPacket>.Failure("Packet validation exception", ex);
        }
    }

    /// <summary>
    /// Validates packet timestamp against current time with clock skew tolerance.
    /// Implements anti-replay temporal checks per security spec.
    /// </summary>
    public static Result ValidateTimestamp(long unixTimestamp)
    {
        try
        {
            var packetTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
            var now = DateTimeOffset.UtcNow;

            // Check if packet is too old
            var age = now - packetTime;
            if (age > MaxPacketAge)
            {
                return Result.Failure($"Packet too old: {age.TotalHours:F1} hours (max: {MaxPacketAge.TotalHours})");
            }

            // Check if packet is from the future (clock skew)
            if (packetTime > now + MaxClockSkew)
            {
                return Result.Failure($"Packet timestamp in future: {(packetTime - now).TotalMinutes:F1} minutes ahead");
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure("Invalid timestamp format", ex);
        }
    }

    /// <summary>
    /// Verifies packet signature using Ed25519/ECDsa.
    /// </summary>
    public static Result VerifySignature(NodusPacket packet, string publicKeyBase64)
    {
        if (packet.Signature == null || packet.Signature.Length == 0)
        {
            return Result.Failure("Packet signature missing");
        }

        if (string.IsNullOrEmpty(publicKeyBase64))
        {
            return Result.Failure("Public key not provided");
        }

        try
        {
            // Reconstruct signable block
            var signable = ConstructSignableBlock(packet);

            // Verify signature
            var isValid = CryptoHelper.VerifyData(signable, packet.Signature, publicKeyBase64);

            return isValid
                ? Result.Success()
                : Result.Failure("Signature verification failed");
        }
        catch (Exception ex)
        {
            return Result.Failure("Signature verification exception", ex);
        }
    }

    /// <summary>
    /// Constructs the signable block from packet fields for integrity verification.
    /// </summary>
    private static byte[] ConstructSignableBlock(NodusPacket packet)
    {
        var idBytes = System.Text.Encoding.UTF8.GetBytes(packet.Id);
        var senderBytes = System.Text.Encoding.UTF8.GetBytes(packet.SenderId);
        var tsBytes = BitConverter.GetBytes(packet.Timestamp);

        var list = new List<byte>();
        list.AddRange(idBytes);
        list.AddRange(senderBytes);
        list.AddRange(tsBytes);
        list.AddRange(packet.EncryptedPayload);

        return list.ToArray();
    }

    /// <summary>
    /// Comprehensive validation including signature check.
    /// </summary>
    public static Result<NodusPacket> ValidateWithSignature(
        byte[] data,
        PacketTracker tracker,
        string publicKeyBase64)
    {
        var parseResult = ValidateAndParse(data, tracker);
        if (parseResult.IsFailure)
        {
            return parseResult;
        }

        var packet = parseResult.Value!;

        var signatureResult = VerifySignature(packet, publicKeyBase64);
        if (signatureResult.IsFailure)
        {
            return Result<NodusPacket>.Failure(signatureResult.Error);
        }

        return Result<NodusPacket>.Success(packet);
    }
}
