using Xunit;
using Nodus.Shared.Services;

namespace Nodus.Tests.Unit;

public class ChunkerServiceTests
{
    private readonly ChunkerService _service = new();

    [Fact]
    public void Split_SmallPayload_CreatesSingleDataChunk()
    {
        // Arrange
        var payload = new byte[10];
        Array.Fill(payload, (byte)0xFF);
        byte msgId = 1;

        // Act
        var chunks = _service.Split(payload, msgId);

        // Assert
        // Expect 2 chunks: 1 Header + 1 Data
        Assert.Equal(2, chunks.Count);
        
        // Verify Header (Index 0)
        var headerChunk = chunks[0];
        // Cannot easily cast struct from byte[] in test without unsafe or structure methods, 
        // but we know it's 5 bytes.
        Assert.Equal(5, headerChunk.Length); 
        Assert.Equal(msgId, headerChunk[0]); // MessageId
        Assert.Equal(0, headerChunk[1]);     // Index 0

        // Verify Data (Index 1)
        var dataChunk = chunks[1];
        Assert.Equal(12, dataChunk.Length); // 2 header + 10 data
        Assert.Equal(msgId, dataChunk[0]);
        Assert.Equal(1, dataChunk[1]); // Index 1
        
        // precise data check
        var data = dataChunk.Skip(2).ToArray();
        Assert.Equal(payload, data);
    }

    [Fact]
    public void Split_LargePayload_CreatesMultipleChunks()
    {
        // MaxMtu = 180. Data per chunk = 178.
        // Let's create a payload of 178 * 2 + 50 = 406 bytes.
        // Expect: Header + 3 Data Chunks.
        int dataPerChunk = 178;
        int size = dataPerChunk * 2 + 50;
        var payload = new byte[size];
        new Random().NextBytes(payload);
        byte msgId = 42;

        var chunks = _service.Split(payload, msgId);

        Assert.Equal(4, chunks.Count); // 1 Header + 3 Data
        
        // Verify last chunk size
        var lastChunk = chunks.Last();
        Assert.Equal(50 + 2, lastChunk.Length);
    }

    [Fact]
    public void Assembler_ReconstructsPayloadCorrectly()
    {
        // Arrange
        var assembler = new ChunkerService.ChunkAssembler();
        var payload = new byte[500];
        new Random().NextBytes(payload);
        byte msgId = 10;
        
        var chunks = _service.Split(payload, msgId);

        // Act & Assert
        // Feed in random order? No, let's feed strictly for now, but skipping one to test logic if needed.
        // Let's feed normally.
        
        foreach (var chunk in chunks)
        {
            bool accepted = assembler.Add(chunk);
            Assert.True(accepted);
        }

        Assert.True(assembler.IsComplete);
        Assert.Equal(payload, assembler.GetPayload());
    }

    [Fact]
    public void Assembler_HandlesOutOfOrderChunks()
    {
        var assembler = new ChunkerService.ChunkAssembler();
        var payload = new byte[500];
        new Random().NextBytes(payload);
        byte msgId = 20;

        var chunks = _service.Split(payload, msgId);
        
        // Shuffle chunks (keeping Header 0 first is usually required for init, 
        // but our logic handles it? 
        // Our logic REQURES header first to init buffer size.
        // Let's feed header first, then shuffle rest.
        
        assembler.Add(chunks[0]); // Keep header first
        
        var dataChunks = chunks.Skip(1).ToList();
        // Reverse them
        dataChunks.Reverse();
        
        foreach (var chunk in dataChunks)
        {
            assembler.Add(chunk);
        }

        Assert.True(assembler.IsComplete);
        Assert.Equal(payload, assembler.GetPayload());
    }

    [Fact]
    public void Assembler_RejectsWrongMessageId()
    {
        var assembler = new ChunkerService.ChunkAssembler();
        var chunks = _service.Split(new byte[10], 1);
        
        assembler.Add(chunks[0]); // Init with ID 1
        
        // Try adding chunk from ID 2
        var badChunks = _service.Split(new byte[10], 2);
        bool accepted = assembler.Add(badChunks[1]);
        
        Assert.False(accepted);
    }
}
