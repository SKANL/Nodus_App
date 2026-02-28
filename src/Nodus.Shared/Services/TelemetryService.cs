using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;

namespace Nodus.Shared.Services;

/// <summary>
/// Telemetry service for tracking network metrics and topology
/// </summary>
public class TelemetryService
{
    private readonly ILogger<TelemetryService> _logger;
    private readonly IDateTimeProvider _dateTime;
    private readonly ConcurrentDictionary<string, NetworkMetrics> _nodeMetrics = new();
    private readonly ConcurrentQueue<NetworkTopology> _topologyHistory = new();
    private const int MaxHistorySize = 100;

    public event EventHandler<NetworkTopology>? TopologyUpdated;
    public event EventHandler<NetworkMetrics>? NodeMetricsUpdated;

    public TelemetryService(
        ILogger<TelemetryService> logger,
        IDateTimeProvider dateTime)
    {
        _logger = logger;
        _dateTime = dateTime;
    }

    /// <summary>
    /// Update or create metrics for a node
    /// </summary>
    public void UpdateNodeMetrics(string nodeId, Action<NetworkMetrics> updateAction)
    {
        var metrics = _nodeMetrics.GetOrAdd(nodeId, id => new NetworkMetrics
        {
            NodeId = id,
            LastSeen = _dateTime.UtcNow
        });

        lock (metrics)
        {
            updateAction(metrics);
            metrics.LastSeen = _dateTime.UtcNow;

            // Update status based on metrics
            metrics.Status = CalculateNodeStatus(metrics);
        }

        NodeMetricsUpdated?.Invoke(this, metrics);
        _logger.LogDebug("Updated metrics for node {NodeId}: RSSI={Rssi}, Quality={Quality:F1}%",
            nodeId, metrics.Rssi, metrics.ConnectionQuality);
    }

    /// <summary>
    /// Record a packet sent to a node
    /// </summary>
    public void RecordPacketSent(string nodeId, int bytes)
    {
        UpdateNodeMetrics(nodeId, m =>
        {
            m.PacketsSent++;
            m.BytesSent += bytes;
        });
    }

    /// <summary>
    /// Record a packet received from a node
    /// </summary>
    public void RecordPacketReceived(string nodeId, int bytes, double latencyMs)
    {
        UpdateNodeMetrics(nodeId, m =>
        {
            m.PacketsReceived++;
            m.BytesReceived += bytes;

            // Update latency metrics (running average)
            if (m.AverageLatency == 0)
            {
                m.AverageLatency = latencyMs;
                m.MinLatency = latencyMs;
                m.MaxLatency = latencyMs;
            }
            else
            {
                m.AverageLatency = (m.AverageLatency * 0.9) + (latencyMs * 0.1); // Exponential moving average
                m.MinLatency = Math.Min(m.MinLatency, latencyMs);
                m.MaxLatency = Math.Max(m.MaxLatency, latencyMs);
            }
        });
    }

    /// <summary>
    /// Record a packet loss
    /// </summary>
    public void RecordPacketLoss(string nodeId)
    {
        UpdateNodeMetrics(nodeId, m => m.PacketsLost++);
    }

    /// <summary>
    /// Update RSSI for a node
    /// </summary>
    public void UpdateRssi(string nodeId, int rssi)
    {
        UpdateNodeMetrics(nodeId, m => m.Rssi = rssi);
    }

    /// <summary>
    /// Mark a node as relay
    /// </summary>
    public void SetNodeAsRelay(string nodeId, bool isRelay)
    {
        UpdateNodeMetrics(nodeId, m => m.IsRelay = isRelay);
    }

    /// <summary>
    /// Set node label
    /// </summary>
    public void SetNodeLabel(string nodeId, string label)
    {
        UpdateNodeMetrics(nodeId, m => m.NodeLabel = label);
    }

    /// <summary>
    /// Get current network topology snapshot, updating statuses
    /// </summary>
    public NetworkTopology GetCurrentTopology()
    {
        // Update statuses for all nodes
        foreach (var metrics in _nodeMetrics.Values)
        {
            lock (metrics)
            {
                metrics.Status = CalculateNodeStatus(metrics);
            }
        }

        var topology = new NetworkTopology
        {
            Timestamp = _dateTime.UtcNow,
            Nodes = _nodeMetrics.Values.ToList()
        };

        // Add to history
        _topologyHistory.Enqueue(topology);
        while (_topologyHistory.Count > MaxHistorySize)
        {
            _topologyHistory.TryDequeue(out _);
        }

        TopologyUpdated?.Invoke(this, topology);
        return topology;
    }

    /// <summary>
    /// Get metrics for a specific node
    /// </summary>
    public NetworkMetrics? GetNodeMetrics(string nodeId)
    {
        return _nodeMetrics.TryGetValue(nodeId, out var metrics) ? metrics : null;
    }

    /// <summary>
    /// Get all node metrics
    /// </summary>
    public IEnumerable<NetworkMetrics> GetAllNodeMetrics()
    {
        return _nodeMetrics.Values.ToList();
    }

    /// <summary>
    /// Clear metrics for a node (when disconnected)
    /// </summary>
    public void RemoveNode(string nodeId)
    {
        _nodeMetrics.TryRemove(nodeId, out _);
        _logger.LogInformation("Removed node {NodeId} from telemetry", nodeId);
    }

    /// <summary>
    /// Clear all metrics
    /// </summary>
    public void ClearAll()
    {
        _nodeMetrics.Clear();
        _topologyHistory.Clear();
        _logger.LogInformation("Cleared all telemetry data");
    }

    /// <summary>
    /// Calculate node status based on metrics
    /// </summary>
    private NodeStatus CalculateNodeStatus(NetworkMetrics metrics)
    {
        var timeSinceLastSeen = _dateTime.UtcNow - metrics.LastSeen;

        // Offline if not seen in 30 seconds
        if (timeSinceLastSeen.TotalSeconds > 30)
            return NodeStatus.Offline;

        // Warning if stale (>10s) or weak signal (<-85 dBm) or high packet loss (>10%)
        if (timeSinceLastSeen.TotalSeconds > 10 ||
            metrics.Rssi < -85 ||
            metrics.PacketLossPercentage > 10)
            return NodeStatus.Warning;

        return NodeStatus.Online;
    }

    /// <summary>
    /// Get topology history
    /// </summary>
    public IEnumerable<NetworkTopology> GetTopologyHistory()
    {
        return _topologyHistory.ToList();
    }
}
