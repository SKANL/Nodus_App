using System.Text.Json;

namespace Nodus.Shared.Protocol;

public enum MessageType
{
    Handshake = 1,
    HandshakeAck = 2,
    Vote = 3,
    Sync = 4
}

public class NodusPacket
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public MessageType Type { get; set; }
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public string SenderId { get; set; } = string.Empty; // Judge ID or Server ID
    
    // Security Fields
    public byte[] Nonce { get; set; } = Array.Empty<byte>(); // 12 bytes for AES-GCM
    public byte[] Signature { get; set; } = Array.Empty<byte>(); // 64 bytes (Ed25519)
    public byte[] EncryptedPayload { get; set; } = Array.Empty<byte>(); // Ciphertext + Tag

    // Helper for serialization
    public string ToJson() => JsonSerializer.Serialize(this);
    public static NodusPacket? FromJson(string json) => JsonSerializer.Deserialize<NodusPacket>(json);
}

// Inner Payloads (Pre-Encryption)
public class HandshakePayload
{
    public string Name { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty; // Base64 export of Public Key
}

public class HandshakeAckPayload
{
    public bool Accepted { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}
