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

    // Routing Fields (Firefly Protocol - doc 02, doc 12)
    /// <summary>
    /// Hop counter. Decremented by each relay. Packet is dropped when it reaches 0.
    /// Initialized to NodusConstants.MAX_HOPS_TTL (2) on creation.
    /// </summary>
    public byte Ttl { get; set; } = NodusConstants.MAX_HOPS_TTL;

    /// <summary>
    /// Ordered list of relay node IDs that have forwarded this packet.
    /// Used for loop detection: a relay drops the packet if its own ID is already in this list.
    /// </summary>
    public List<string> Hops { get; set; } = new();

    // Security Fields
    public byte[] Nonce { get; set; } = Array.Empty<byte>(); // 12 bytes for AES-GCM
    public byte[] Signature { get; set; } = Array.Empty<byte>(); // 64 bytes (Ed25519/ECDsa P-256)
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
