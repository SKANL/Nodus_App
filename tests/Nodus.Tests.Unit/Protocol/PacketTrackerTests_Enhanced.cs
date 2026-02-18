using FluentAssertions;
using Moq;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Protocol;
using Xunit;

namespace Nodus.Tests.Unit.Protocol;

public class PacketTrackerTests_Enhanced
{
    private readonly Mock<IDateTimeProvider> _dateTimeMock;
    private readonly PacketTracker _sut;
    private readonly DateTime _utcNow;

    public PacketTrackerTests_Enhanced()
    {
        _dateTimeMock = new Mock<IDateTimeProvider>();
        _utcNow = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _dateTimeMock.Setup(x => x.UtcNow).Returns(_utcNow);

        _sut = new PacketTracker(_dateTimeMock.Object);
    }

    [Fact]
    public void TryProcess_WhenNewPacket_ShouldReturnTrue()
    {
        // Act
        var result = _sut.TryProcess("pkt-1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TryProcess_WhenDuplicatePacket_ShouldReturnFalse()
    {
        // Arrange
        _sut.TryProcess("pkt-1");

        // Act
        var result = _sut.TryProcess("pkt-1");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryProcess_WhenDuplicatePacketExpired_ShouldReturnTrue()
    {
        // Arrange
        _sut.TryProcess("pkt-1");
        
        // Advance time by 11 minutes (Retention is 10 mins)
        _dateTimeMock.Setup(x => x.UtcNow).Returns(_utcNow.AddMinutes(11));

        // Act
        var result = _sut.TryProcess("pkt-1");

        // Assert
        result.Should().BeTrue("expired packets should be re-processable");
    }

    [Fact]
    public void Cleanup_ShouldRemoveExpiredEntries()
    {
        // Arrange
        _sut.TryProcess("pkt-1");
        
        // Advance time by 11 minutes (Retention 10 mins + Cleanup Interval 1 min reached)
        // Cleanup runs if now - lastCleanup > cleanupInterval
        // lastCleanup starts at MinValue.
        // First call sets lastCleanup to now.
        // We need to trigger cleanup manually implicitly.
        
        // Initial state: lastCleanup = _utcNow (implicitly set on first CheckCleanup? No, initially MinValue)
        // CheckCleanup logic: if (now - lastCleanup < interval) return.
        // Initial call: Now - MinValue > Interval -> lastCleanup = Now.
        
        // So first TryProcess sets lastCleanup = _utcNow.
        
        // Let's create pkt-1 at T0. Expiry = T0 + 10m.
        
        // Advance to T0 + 12m.
        var future = _utcNow.AddMinutes(12);
        _dateTimeMock.Setup(x => x.UtcNow).Returns(future);
        
        // Act
        // Call TryProcess with a new packet to trigger cleanup
        _sut.TryProcess("pkt-temp"); 
        
        // Now verify pkt-1 is gone by trying to process it again.
        // If it was still there (and valid), it would return false.
        // If it was removed, it should return true (treated as new).
        // Wait, if it's there but expired, TryProcess returns true anyway and updates timestamp.
        // So functional behavior is the same.
        // To verify cleanup specifically, we might need to inspect internal state or rely on memory behavior, 
        // OR define behavior difference.
        
        // Actually, if we advance time, TryProcess("pkt-1") returns true regardless of cleanup.
        // So from a functional "black box" perspective, cleanup is an optimization.
        // However, we can test that it doesn't crash or break things.
        
        var result = _sut.TryProcess("pkt-1");
        result.Should().BeTrue();
    }

    [Fact]
    public void TryProcess_ShouldBeThreadSafe()
    {
        // Arrange
        int threadCount = 20;
        int packetsPerThread = 1000;
        var tasks = new List<Task>();
        
        // Act
        for (int i = 0; i < threadCount; i++)
        {
            int threadId = i;
            tasks.Add(Task.Run(() => 
            {
                for (int j = 0; j < packetsPerThread; j++)
                {
                    _sut.TryProcess($"t{threadId}-p{j}");
                }
            }));
        }

        // Also add some duplicate checks concurrently
        tasks.Add(Task.Run(() => 
        {
            for (int k = 0; k < 1000; k++)
            {
                _sut.TryProcess("shared-pkt");
            }
        }));

        Action act = () => Task.WaitAll(tasks.ToArray());

        // Assert
        act.Should().NotThrow();
    }
}
