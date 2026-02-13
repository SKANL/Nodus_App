namespace Nodus.Shared.Models;

/// <summary>
/// Network metrics for telemetry and monitoring
/// </summary>
public class NetworkMetrics
{
    public string NodeId { get; set; } = string.Empty;
    public string NodeLabel { get; set; } = string.Empty;
    public bool IsRelay { get; set; }
    public DateTime LastSeen { get; set; }
    public int Rssi { get; set; }
    public NodeStatus Status { get; set; }
    
    // Connection metrics
    public int PacketsSent { get; set; }
    public int PacketsReceived { get; set; }
    public int PacketsLost { get; set; }
    public double PacketLossPercentage => PacketsSent > 0 
        ? (PacketsLost / (double)PacketsSent) * 100 
        : 0;
    
    // Latency metrics (milliseconds)
    public double AverageLatency { get; set; }
    public double MinLatency { get; set; }
    public double MaxLatency { get; set; }
    
    // Bandwidth metrics
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    
    // Connection quality
    public double ConnectionQuality => CalculateConnectionQuality();
    
    private double CalculateConnectionQuality()
    {
        // Quality score from 0-100 based on multiple factors
        double rssiScore = Math.Max(0, Math.Min(100, (Rssi + 100) * 2)); // -100 to 0 dBm mapped to 0-100
        double latencyScore = Math.Max(0, Math.Min(100, 100 - (AverageLatency / 10))); // Lower is better
        double packetLossScore = Math.Max(0, 100 - (PacketLossPercentage * 10));
        
        return (rssiScore * 0.4) + (latencyScore * 0.3) + (packetLossScore * 0.3);
    }
}

public enum NodeStatus
{
    Online,    // Green - Active and healthy
    Warning,   // Yellow - Weak signal or high latency
    Offline    // Red - Not responding
}

/// <summary>
/// Network topology snapshot
/// </summary>
public class NetworkTopology
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<NetworkMetrics> Nodes { get; set; } = new();
    public int TotalNodes => Nodes.Count;
    public int ActiveNodes => Nodes.Count(n => n.Status == NodeStatus.Online);
    public int RelayNodes => Nodes.Count(n => n.IsRelay);
    public double AverageConnectionQuality => Nodes.Any() 
        ? Nodes.Average(n => n.ConnectionQuality) 
        : 0;
}
