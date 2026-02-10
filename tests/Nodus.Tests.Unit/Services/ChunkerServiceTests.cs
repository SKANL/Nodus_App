using Xunit;
using FluentAssertions;
using Nodus.Shared.Services;
using System.Text;

namespace Nodus.Tests.Unit.Services;

public class ChunkerServiceTests
{
    private readonly ChunkerService _chunker;

    public ChunkerServiceTests()
    {
        _chunker = new ChunkerService();
    }

    [Fact]
    public void Split_ShouldThrowException_WhenPayloadIsEmpty()
    {
        // Arrange
        byte[] payload = Array.Empty<byte>();

        // Act
        Action act = () => _chunker.Split(payload, 1);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Payload cannot be empty");
    }

    [Fact]
    public void Split_ShouldCreateSingleHeader_WhenPayloadIsSmall()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("Small Payload");
        byte msgId = 10;

        // Act
        var chunks = _chunker.Split(payload, msgId);

        // Assert
        // 1 Header + 1 Data Chunk
        chunks.Should().HaveCount(2); 
        
        // Inspect Header
        var headerBytes = chunks[0];
        // Header struct is 5 bytes (MsgId, ChunkIndex, TotalChunks, PayloadLenLo, PayloadLenHi)
        headerBytes.Length.Should().Be(5);
        headerBytes[0].Should().Be(msgId);
        headerBytes[1].Should().Be(0); // Index 0 for header
        headerBytes[2].Should().Be(1); // Total 1 data chunk
        
        // Inspect Data
        var dataBytes = chunks[1];
        dataBytes[0].Should().Be(msgId);
        dataBytes[1].Should().Be(1); // Index 1
        
        // Verify Content
        var content = new byte[dataBytes.Length - 2];
        Array.Copy(dataBytes, 2, content, 0, content.Length);
        content.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void Split_ShouldDivideLargePayload_Correctly()
    {
        // Arrange
        // MaxMtu = 180. Overhead = 2. MaxData = 178.
        // Let's create a payload of 400 bytes.
        // Chunks needed: ceil(400 / 178) = 3 (178 + 178 + 44)
        var payload = new byte[400];
        new Random().NextBytes(payload);
        byte msgId = 20;

        // Act
        var chunks = _chunker.Split(payload, msgId);

        // Assert
        // 1 Header + 3 Data Chunks = 4 total
        chunks.Should().HaveCount(4);
        chunks[0][2].Should().Be(3); // Total data chunks in header
    }

    [Fact]
    public void Assembler_ShouldReassemble_SplitPayload()
    {
        // Arrange
        var payload = new byte[1024]; // 1KB
        new Random().NextBytes(payload);
        byte msgId = 30;

        var chunks = _chunker.Split(payload, msgId);
        var assembler = new ChunkerService.ChunkAssembler();

        // Act
        // Shuffle chunks to simulate out-of-order delivery (if logic supported it, but our logic expects header first usually)
        // actually strict logic might require header first to init buffer
        
        // Feed Header First
        bool headerAdded = assembler.Add(chunks[0]);
        headerAdded.Should().BeTrue();

        // Feed Data Chunks
        for (int i = 1; i < chunks.Count; i++)
        {
            var added = assembler.Add(chunks[i]);
            added.Should().BeTrue();
        }

        // Assert
        assembler.IsComplete.Should().BeTrue();
        assembler.GetPayload().Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void Assembler_ShouldReset_OnNewMessageId()
    {
        // Arrange
        var payload = new byte[100];
        var assembler = new ChunkerService.ChunkAssembler();
        
        // Msg 1
        var chunks1 = _chunker.Split(payload, 1);
        assembler.Add(chunks1[0]); // Header Msg 1
        assembler.MessageId.Should().Be(1);

        // Msg 2 (New Header)
        var chunks2 = _chunker.Split(payload, 2);
        bool accepted = assembler.Add(chunks2[0]); // Header Msg 2

        // Assert
        accepted.Should().BeTrue();
        assembler.MessageId.Should().Be(2); 
        assembler.IsComplete.Should().BeFalse();
    }
}
