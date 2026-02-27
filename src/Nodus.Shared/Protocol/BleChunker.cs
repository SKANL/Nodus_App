using System.Runtime.InteropServices;

namespace Nodus.Shared.Protocol;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ChunkHeader
{
    public byte MessageId;       // Unique ID for this transfer (0-255)
    public byte ChunkIndex;      // 0 = Header, 1..N = Data
    public byte TotalChunks;     // Total packet count
    public ushort PayloadLength; // Total bytes (sanity check)

    public byte[] ToBytes()
    {
        var size = Marshal.SizeOf(this);
        var arr = new byte[size];
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(this, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            return arr;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public static ChunkHeader FromBytes(byte[] bytes)
    {
        var header = new ChunkHeader();
        var size = Marshal.SizeOf(header);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(bytes, 0, ptr, size);
            header = (ChunkHeader)Marshal.PtrToStructure(ptr, typeof(ChunkHeader))!;
            return header;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}

public static class ChunkHelper
{
    // Conservative MTU assumption for BLE
    public const int DEFAULT_CHUNK_SIZE = 180;

    public static List<byte[]> Split(byte[] payload, byte messageId, int maxChunkSize = DEFAULT_CHUNK_SIZE)
    {
        var chunks = new List<byte[]>();

        // 1. Calculate chunks required
        // We use (maxChunkSize - 0) for data because Chunk 0 is purely Header? 
        // Docs say: "Sender writes Chunk 0 (Header). Sender writes Chunk 1..N (Data)"
        // So Header is its own standalone packet.

        var totalChunks = (int)Math.Ceiling((double)payload.Length / maxChunkSize);
        if (totalChunks > 255) throw new ArgumentException("Payload too large for 1-byte chunk count");

        // 2. Create Header Packet (Chunk 0)
        var header = new ChunkHeader
        {
            MessageId = messageId,
            ChunkIndex = 0,
            TotalChunks = (byte)(totalChunks + 1), // +1 for the header itself
            PayloadLength = (ushort)payload.Length
        };
        chunks.Add(header.ToBytes());

        // 3. Create Data Packets
        for (int i = 0; i < totalChunks; i++)
        {
            var offset = i * maxChunkSize;
            var length = Math.Min(maxChunkSize, payload.Length - offset);

            // We could prepend a mini-header (msgId + index) to every chunk for robustness out-of-order,
            // but the docs describe a "Burst" logic where order is assumed or managed by L2cap/GATT ordering.
            // "Sender writes Chunk 1..N in a burst".
            // Implementation detail: If we just send raw bytes, how does Receiver know which chunk is which if a packet drops?
            // Pure BLE WriteWithoutResponse *can* drop packets.
            // ROBUSTNESS UPGRADE: Let's prepend [MessageId, ChunkIndex] to every data packet too.
            // But strict adherence to the doc "Chunk 0 = Header" implies the Header describes the intent.
            // Let's stick to the doc: Header First. Then Data. 
            // NOTE: If we want strict robustness, we usually define: [Header][Data].
            // Let's implement: [MsgId][Index][Data...] for specific chunks?
            // The doc diagram suggests: "Sender writes Chunk 1..N". 
            // Let's stick to the simplest interpretation first:
            // Packet 0: [HeaderStruct]
            // Packet 1: [Data...]
            // Packet 2: [Data...]

            var chunk = new byte[length];
            Array.Copy(payload, offset, chunk, 0, length);
            chunks.Add(chunk);
        }

        return chunks;
    }
}

public class ChunkAssembler
{
    private ChunkHeader? _currentHeader;
    private readonly MemoryStream _buffer = new();
    private readonly object _lock = new();

    public event EventHandler<byte[]>? PayloadCompleted;

    public void ProcessPacket(byte[] data)
    {
        lock (_lock)
        {
            // Heuristic: Is this a Header?
            // Size of Header is: 1+1+1+2 = 5 bytes.
            if (data.Length == 5)
            {
                try
                {
                    var possibleHeader = ChunkHeader.FromBytes(data);
                    if (possibleHeader.ChunkIndex == 0 && possibleHeader.TotalChunks > 0)
                    {
                        // Start new sequence
                        _currentHeader = possibleHeader;
                        _buffer.SetLength(0); // Reset
                        return;
                    }
                }
                catch { /* Not a header, treated as data? */ }
            }

            // It's Data (presumably)
            if (_currentHeader.HasValue)
            {
                _buffer.Write(data, 0, data.Length);

                // Check completion
                if (_buffer.Length >= _currentHeader.Value.PayloadLength)
                {
                    PayloadCompleted?.Invoke(this, _buffer.ToArray());
                    _currentHeader = null; // Reset
                    _buffer.SetLength(0);
                }
            }
        }
    }
}
