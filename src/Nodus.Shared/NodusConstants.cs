namespace Nodus.Shared;

/// <summary>
/// Centralized constants for the Nodus Firefly Protocol.
/// Derived from: docs/02.Network.Swarm_Protocol.md
/// </summary>
public static class NodusConstants
{
    // BLE Service & Characteristic UUIDs
    // Using a consistent deterministic UUID generation for the project.
    // Base: "d2b37f23-365d-48d6-bd81-37f23e206000"
    public const string SERVICE_UUID = "d2b37f23-365d-48d6-bd81-37f23e206001";
    public const string CHARACTERISTIC_UUID = "d2b37f23-365d-48d6-bd81-37f23e206002";

    // Packet Types
    public const byte PACKET_TYPE_JSON = 0x01;
    public const byte PACKET_TYPE_MEDIA = 0x02;
    public const byte PACKET_TYPE_PROJECTS = 0x03;
    public const byte PACKET_TYPE_ACK = 0xA1;

    // Protocol Constraints
    public const int MTU_SIZE = 180; // Safe minimum for Application Data
    public const int MAX_HOPS_TTL = 2; // Prevent loops

    // Manufacturer Data IDs
    public const byte MAN_DATA_RELAY_ID = 0x02;

    // Crypto Defaults
    public const int SALT_SIZE = 16;
    public const int NONCE_SIZE = 12; // AES-GCM standard
    public const int TAG_SIZE = 16;   // AES-GCM standard

    // Keys
    public const string KEY_EVENT_ID = "current_event_id";
    public const string KEY_SHARED_AES = "shared_aes_key";
    public const string KEY_PRIVATE_KEY = "judge_private_key";
    public const string KEY_PUBLIC_KEY = "judge_public_key";
    public const string KEY_JUDGE_NAME = "judge_name";
}
