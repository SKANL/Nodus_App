using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Services;
using Nodus.Shared.Models;
using Xunit;
using System.Collections.Concurrent;

namespace Nodus.Tests.Unit.Services;

public class TelemetryServiceTests_Enhanced
{
    private readonly Mock<ILogger<TelemetryService>> _loggerMock;
    private readonly Mock<IDateTimeProvider> _dateTimeMock;
    private readonly TelemetryService _sut;
    private readonly DateTime _utcNow;

    public TelemetryServiceTests_Enhanced()
    {
        _loggerMock = new Mock<ILogger<TelemetryService>>();
        _dateTimeMock = new Mock<IDateTimeProvider>();
        
        _utcNow = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _dateTimeMock.Setup(x => x.UtcNow).Returns(_utcNow);

        _sut = new TelemetryService(_loggerMock.Object, _dateTimeMock.Object);
    }

    [Fact]
    public void RecordPacketSent_ShouldIncrementMetrics()
    {
        // Arrange
        var nodeId = "node1";
        var bytes = 100;

        // Act
        _sut.RecordPacketSent(nodeId, bytes);

        // Assert
        var metrics = _sut.GetNodeMetrics(nodeId);
        metrics.Should().NotBeNull();
        metrics!.NodeId.Should().Be(nodeId);
        metrics.PacketsSent.Should().Be(1);
        metrics.BytesSent.Should().Be(bytes);
        metrics.LastSeen.Should().Be(_utcNow);
        metrics.Status.Should().Be(NodeStatus.Online);
    }

    [Fact]
    public void RecordPacketReceived_ShouldUpdateMetricsAndLatency()
    {
        // Arrange
        var nodeId = "node1";
        
        // Act
        _sut.RecordPacketReceived(nodeId, 100, 50); // First packet: avg=50
        _dateTimeMock.Setup(x => x.UtcNow).Returns(_utcNow.AddSeconds(1));
        _sut.RecordPacketReceived(nodeId, 200, 100); // Second packet: avg = (50*0.9) + (100*0.1) = 45 + 10 = 55

        // Assert
        var metrics = _sut.GetNodeMetrics(nodeId);
        metrics.Should().NotBeNull();
        metrics!.PacketsReceived.Should().Be(2);
        metrics.BytesReceived.Should().Be(300);
        metrics.AverageLatency.Should().Be(55);
        metrics.MinLatency.Should().Be(50);
        metrics.MaxLatency.Should().Be(100);
    }

    [Fact]
    public void UpdateNodeMetrics_ShouldBeThreadSafe()
    {
        // Arrange
        var nodeId = "concurrent-node";
        int taskCount = 100;
        int bytesPerTask = 10;
        
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < taskCount; i++)
        {
            tasks.Add(Task.Run(() => _sut.RecordPacketSent(nodeId, bytesPerTask)));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        var metrics = _sut.GetNodeMetrics(nodeId);
        metrics.Should().NotBeNull();
        metrics!.PacketsSent.Should().Be(taskCount);
        metrics.BytesSent.Should().Be(taskCount * bytesPerTask);
    }

    [Fact]
    public void CalculateNodeStatus_WhenTimeoutExceeded_ShouldBeOffline()
    {
        // Arrange
        var nodeId = "offline-node";
        _sut.UpdateNodeMetrics(nodeId, m => {}); // LastSeen = _utcNow
        
        // Advance time by 31 seconds
        _dateTimeMock.Setup(x => x.UtcNow).Returns(_utcNow.AddSeconds(31));

        // Act
        // Refresh statuses via GetCurrentTopology
        _sut.GetCurrentTopology();

        // Assert
        var metrics = _sut.GetNodeMetrics(nodeId);
        metrics!.Status.Should().Be(NodeStatus.Offline);
    }

    [Fact]
    public void CalculateNodeStatus_WhenPacketLossHigh_ShouldBeWarning()
    {
        // Arrange
        var nodeId = "lossy-node";
        
        // Act
        _sut.UpdateNodeMetrics(nodeId, m => 
        {
            m.PacketsSent = 100;
            m.PacketsLost = 15; // 15% loss
        });

        // Assert
        var metrics = _sut.GetNodeMetrics(nodeId);
        // PacketLossPercentage is calculated property? Let's verify NetworkMetrics model if needed.
        // Assuming PacketLossPercentage logic exists or is calculated in getter.
        // Wait, I need to check NetworkMetrics model or TelemetryService logic.
        // TelemetryService logic doesn't calculate PacketLossPercentage, it reads it.
        // Does NetworkMetrics have logic?
        
        // Let's assume yes for now, if failure, I check Model.
        metrics!.Status.Should().Be(NodeStatus.Warning);
    }

    [Fact]
    public void GetCurrentTopology_ShouldReturnSnapshotAndAddToHistory()
    {
        // Arrange
        _sut.UpdateNodeMetrics("node1", m => {});
        _sut.UpdateNodeMetrics("node2", m => {});

        // Act
        var topology = _sut.GetCurrentTopology();

        // Assert
        topology.Nodes.Should().HaveCount(2);
        topology.Timestamp.Should().Be(_utcNow);
        
        var history = _sut.GetTopologyHistory();
        history.Should().Contain(topology);
    }

    [Fact]
    public void RemoveNode_ShouldDeleteMetrics()
    {
        // Arrange
        _sut.UpdateNodeMetrics("node1", m => {});

        // Act
        _sut.RemoveNode("node1");

        // Assert
        _sut.GetNodeMetrics("node1").Should().BeNull();
    }
}
