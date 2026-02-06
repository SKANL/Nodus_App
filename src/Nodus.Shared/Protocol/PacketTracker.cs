using System.Collections.Concurrent;

namespace Nodus.Shared.Protocol;

/// <summary>
/// Tracks seen packet IDs to prevent routing loops in the Swarm.
/// Implements a Time-To-Live (TTL) cache.
/// </summary>
public class PacketTracker
{
    // Thread-safe dictionary: PacketID -> ExpiryTime
    private readonly ConcurrentDictionary<string, DateTime> _seenPackets = new();
    
    // How long to remember a packet (in minutes)
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromMinutes(10);
    
    // Cleanup interval
    private DateTime _lastCleanup = DateTime.MinValue;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Checks if a packet has been seen recently. 
    /// If NOT seen, it adds it to the cache and returns true (Process it).
    /// If SEEN, returns false (Drop it).
    /// </summary>
    public bool TryProcess(string packetId)
    {
        CleanupIfNeeded();

        if (string.IsNullOrEmpty(packetId)) return false;

        if (_seenPackets.ContainsKey(packetId))
        {
            return false; // Already seen, drop (Loop Prevention)
        }

        // Add to cache
        _seenPackets.TryAdd(packetId, DateTime.UtcNow.Add(_retentionPeriod));
        return true; // New packet, process/relay it
    }

    private void CleanupIfNeeded()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCleanup < _cleanupInterval) return;

        _lastCleanup = now;
        
        // Remove expired entries
        foreach (var kvp in _seenPackets)
        {
            if (kvp.Value < now)
            {
                _seenPackets.TryRemove(kvp.Key, out _);
            }
        }
    }
}
