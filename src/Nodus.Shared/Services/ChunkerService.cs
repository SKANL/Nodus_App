using System.Runtime.InteropServices;

using Nodus.Shared.Abstractions;

namespace Nodus.Shared.Services;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ChunkHeader
{
    public byte MessageId;       // Unique ID for this transfer (0-255)
    public byte ChunkIndex;      // 0 = Header, 1..N = Data
    public byte TotalChunks;     // Total count of DATA chunks
    public ushort PayloadLength; // Total bytes of the full payload
}

public class ChunkerService : IChunkerService
{
    // Safe maximum payload for BLE characteristics to ensure high compatibility
    // Android can request 512, iOS ~185. We use 180 to stay safe.
    public const int MaxMtu = 180;

    /// <summary>
    /// Splits a payload into chunks tailored for BLE transfer.
    /// Format:
    /// Packet 0: [Header] (5 bytes)
    /// Packet 1..N: [MessageId][ChunkIndex][Data...] (2 + Data bytes)
    /// </summary>
    public List<byte[]> Split(byte[] payload, byte messageId)
    {
        if (payload == null || payload.Length == 0)
            throw new ArgumentException("Payload cannot be empty");

        var chunks = new List<byte[]>();
        
        // Calculate chunks
        // Header packet is separate (Index 0)
        // Data packets (Index 1..N) carry payload
        // Max data per packet = MaxMtu - 2 bytes header overhead (MessageId + ChunkIndex)
        int maxDataPerChunk = MaxMtu - 2;
        int totalPayloadBytes = payload.Length;
        int totalDataChunks = (int)Math.Ceiling((double)totalPayloadBytes / maxDataPerChunk);

        if (totalDataChunks > 255)
            throw new ArgumentException($"Payload too large. Max chunks: 255. Current required: {totalDataChunks} (~45KB limit)");

        // 1. Create Header Packet (Index 0)
        var header = new ChunkHeader
        {
            MessageId = messageId,
            ChunkIndex = 0,
            TotalChunks = (byte)totalDataChunks,
            PayloadLength = (ushort)totalPayloadBytes
        };
        chunks.Add(StructureToBytes(header));

        // 2. Create Data Packets (Index 1..N)
        for (int i = 0; i < totalDataChunks; i++)
        {
            int offset = i * maxDataPerChunk;
            int length = Math.Min(maxDataPerChunk, totalPayloadBytes - offset);
            
            var chunkBytes = new byte[length + 2];
            chunkBytes[0] = messageId;
            chunkBytes[1] = (byte)(i + 1); // 1-based index for data chunks
            Array.Copy(payload, offset, chunkBytes, 2, length);
            
            chunks.Add(chunkBytes);
        }

        return chunks;
    }

    private byte[] StructureToBytes(ChunkHeader str)
    {
        // Manual serialization to avoid Marshalling issues on some platforms
        var bytes = new byte[5];
        bytes[0] = str.MessageId;
        bytes[1] = str.ChunkIndex;
        bytes[2] = str.TotalChunks;
        
        var lenBytes = BitConverter.GetBytes(str.PayloadLength);
        if (!BitConverter.IsLittleEndian) Array.Reverse(lenBytes); // BLE usually LE? Actually custom protocol. Let's stick to LE.
        
        bytes[3] = lenBytes[0];
        bytes[4] = lenBytes[1];
        
        return bytes;
    }

    /// <summary>
    /// Helper to reassemble chunks.
    /// </summary>
    public class ChunkAssembler
    {
        private byte[]? _buffer;
        private int _totalDataChunks;
        private int _receivedChunksCount;
        private bool[]? _receivedChunksMask; // Tracks which 1-based index chunks we have
        
        public byte MessageId { get; private set; }

        public bool IsComplete 
        {
            get 
            {
                if (_buffer == null || _receivedChunksMask == null) return false;
                return _receivedChunksCount == _totalDataChunks;
            } 
        }

        public byte[]? GetPayload() => IsComplete ? _buffer : null;

        /// <summary>
        /// Process an incoming packet. Returns true if packet was accepted and processed.
        /// </summary>
        public bool Add(byte[] packet)
        {
            if (packet == null || packet.Length < 2) return false;

            byte msgId = packet[0];
            byte chunkIndex = packet[1];

            // Case A: Header Packet (Index 0)
            if (chunkIndex == 0)
            {
                // Header struct is 5 bytes + potential alignment padding, 
                // but we packed it = 1. So 5 bytes.
                if (packet.Length < 5) return false;

                // If we already started assembling a different message, reset?
                // Or if it's the same message ID, maybe re-transmission of header?
                if (_buffer != null && msgId != MessageId)
                {
                    // New message started, discarding old
                    Reset();
                }

                if (_buffer == null)
                {
                    MessageId = msgId;
                    // Parse header manually to be safe or use Marshal
                    // Structure: [MsgId][Idx][Total][LenLo][LenHi]
                    _totalDataChunks = packet[2];
                    
                    // Manual extraction of PayloadLength
                    // Ensure Little Endian as per serializer
                    ushort payloadLen = (ushort)(packet[3] | (packet[4] << 8));

                    _buffer = new byte[payloadLen];
                    _receivedChunksMask = new bool[_totalDataChunks + 1]; // 1-based
                    _receivedChunksCount = 0;
                    return true;
                }
                
                return true; // Already initialized
            }
            
            // Case B: Data Packet (Index > 0)
            if (_buffer == null) return false; // Waiting for header first
            if (msgId != MessageId) return false; // Wrong message
            if (chunkIndex > _totalDataChunks) return false; // Index out of bounds

            // Check if duplicate
            if (_receivedChunksMask![chunkIndex]) return true; // Already have it

            // Copy data
            int maxDataPerChunk = MaxMtu - 2;
            int dataLen = packet.Length - 2;
            int offset = (chunkIndex - 1) * maxDataPerChunk;

            if (offset + dataLen > _buffer.Length) return false; // Overflow check

            Array.Copy(packet, 2, _buffer, offset, dataLen);
            _receivedChunksMask![chunkIndex] = true;
            _receivedChunksCount++;

            return true;
        }

        private void Reset()
        {
            _buffer = null;
            _receivedChunksMask = null;
            _receivedChunksCount = 0;
            _totalDataChunks = 0;
            MessageId = 0;
        }
    }
}
