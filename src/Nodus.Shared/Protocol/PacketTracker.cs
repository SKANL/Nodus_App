using System.Collections.Concurrent;
using Nodus.Shared.Abstractions;

namespace Nodus.Shared.Protocol;

/// <summary>
/// Tracks seen packet IDs to prevent routing loops in the Swarm.
/// Implements a Time-To-Live (TTL) cache.
/// </summary>
public class PacketTracker
{
    private readonly IDateTimeProvider _dateTime;
    private const int MaxCacheSize = 10000;
    // Thread-safe dictionary: PacketID -> ExpiryTime
    private readonly ConcurrentDictionary<string, DateTime> _seenPackets = new();

    // How long to remember a packet
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromMinutes(10);

    // Cleanup interval
    private DateTime _lastCleanup = DateTime.MinValue;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(1);
    private readonly object _cleanupLock = new();

    public PacketTracker(IDateTimeProvider dateTime)
    {
        _dateTime = dateTime;
    }

    /// <summary>
    /// Checks if a packet has been seen recently. 
    /// If NOT seen, it adds it to the cache and returns true (Process it).
    /// If SEEN, returns false (Drop it).
    /// </summary>
    public bool TryProcess(string packetId)
    {
        if (string.IsNullOrEmpty(packetId)) return false;

        CheckCleanup();

        var now = _dateTime.UtcNow;
        if (_seenPackets.TryGetValue(packetId, out var expiry))
        {
            if (expiry > now)
            {
                return false; // Already seen and valid, drop (Loop Prevention)
            }
            // Expired, update expiry
            _seenPackets[packetId] = now.Add(_retentionPeriod);
            return true; // Re-process if expired (though ideally unique IDs shouldn't recur)
        }

        if (_seenPackets.Count >= MaxCacheSize)
        {
            // Emergency clear if we are flooded to prevent OOM
            _seenPackets.Clear();
        }

        // Add to cache
        _seenPackets.TryAdd(packetId, now.Add(_retentionPeriod));
        return true; // New packet, process/relay it
    }

    private void CheckCleanup()
    {
        var now = _dateTime.UtcNow;
        if (now - _lastCleanup < _cleanupInterval) return;

        lock (_cleanupLock)
        {
            if (now - _lastCleanup < _cleanupInterval) return; // Double-check locking pattern

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
}
