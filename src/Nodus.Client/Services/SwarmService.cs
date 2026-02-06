using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nodus.Shared;

using Nodus.Client.Abstractions;

namespace Nodus.Client.Services;

public enum SwarmState
{
    Seeker,     // Default: Silent, Scanning
    Candidate,  // Thinking about becoming a Link (Trickle Wait)
    Link,       // Advertising as Relay
    Cooldown    // Resting
}

public partial class SwarmService : ObservableObject, ISwarmService
{
    private readonly IBleClientService _bleClient;
    // We will inject RelayService later when implemented
    private readonly IRelayHostingService _relayService; 
    private readonly ILogger<SwarmService> _logger;

    [ObservableProperty] private SwarmState _currentState = SwarmState.Seeker;
    [ObservableProperty] private int _neighborLinkCount = 0;
    [ObservableProperty] private bool _isMuleMode = false;
    
    private IDispatcherTimer? _heartbeat;
    private DateTime _cooldownExpiresAt = DateTime.MinValue;
    private DateTime _linkStartedAt = DateTime.MinValue;
    private DateTime _lastServerConnectionAt = DateTime.UtcNow; // Assume fresh start
    
    private const int RSSI_THRESHOLD = -75; // Strong signal required
    private const int MAX_LINK_DURATION_SECONDS = 60;
    private const int COOLDOWN_MINUTES = 5;
    private const int MULE_MODE_THRESHOLD_MINUTES = 10;

    public SwarmService(IBleClientService bleClient, IRelayHostingService relayService, ILogger<SwarmService> logger)
    {
        _bleClient = bleClient;
        _relayService = relayService;
        _logger = logger;

        _bleClient.LinkCountChanged += (s, count) => 
        {
            NeighborLinkCount = count;
            _logger.LogDebug("Neighbor Link Count Updated: {Count}", count);
        };
        
        _bleClient.ConnectionStatusChanged += (s, isConnected) => 
        {
            if (isConnected) _lastServerConnectionAt = DateTime.UtcNow;
        };

        StartHeartbeat();
    }

    private void StartHeartbeat()
    {
        _heartbeat = Application.Current?.Dispatcher.CreateTimer();
        if (_heartbeat != null)
        {
            _heartbeat.Interval = TimeSpan.FromSeconds(5);
            _heartbeat.Tick += OnHeartbeat;
            _heartbeat.Start();
        }
    }

    private async void OnHeartbeat(object? sender, EventArgs e)
    {
        // 0. Mule Mode Check
        var minutesSince = (DateTime.UtcNow - _lastServerConnectionAt).TotalMinutes;
        IsMuleMode = (minutesSince > MULE_MODE_THRESHOLD_MINUTES);

        // 1. Cooldown Check
        if (CurrentState == SwarmState.Cooldown)
        {
            if (DateTime.UtcNow > _cooldownExpiresAt)
            {
                _logger.LogDebug("Cooldown expired. Returning to Seeker.");
                CurrentState = SwarmState.Seeker;
            }
            return;
        }

        // 2. Link Duration Check
        if (CurrentState == SwarmState.Link)
        {
            var duration = (DateTime.UtcNow - _linkStartedAt).TotalSeconds;
            if (duration > MAX_LINK_DURATION_SECONDS)
            {
                _logger.LogDebug("Link duration maxed. Entering Cooldown.");
                await StopRelayAsync();
                CurrentState = SwarmState.Cooldown;
                _cooldownExpiresAt = DateTime.UtcNow.AddMinutes(COOLDOWN_MINUTES);
            }
            return;
        }

        // 3. Candidate Promotion Logic (Trickle)
        if (CurrentState == SwarmState.Seeker)
        {
            // Do we have a strong connection to Server?
            // Note: We need to expose RSSI from BleClientService more robustly. 
            // For now, assuming if Connected, RSSI is decent enough (simplified).
            // Ideal: _bleClient.LastRssi > RSSI_THRESHOLD
            
            // Simplified Check:
            if (_bleClient.IsConnected) 
            {
                // Enter Candidate Mode
                // Random Wait (Trickle) to avoid collision
                CurrentState = SwarmState.Candidate;
                var randomWait = Random.Shared.Next(2000, 10000);
                _logger.LogDebug("Candidate! Waiting {RandomWait}ms...", randomWait);
                
                await Task.Delay(randomWait);

                // Redundancy Check (The "k" constant)
                // If we see too many other Relays, abort.
                if (NeighborLinkCount >= 2)
                {
                    _logger.LogDebug("Too many neighbors. Aborting promotion.");
                    CurrentState = SwarmState.Seeker;
                }
                else
                {
                    // Promote!
                    _logger.LogInformation("Promoted to LINK!");
                    CurrentState = SwarmState.Link;
                    _linkStartedAt = DateTime.UtcNow;
                    await StartRelayAsync();
                }
            }
        }
    }

    // Placeholders for RelayService interaction
    private Task StartRelayAsync()
    {
        return _relayService.StartAdvertisingAsync();
    }

    private Task StopRelayAsync()
    {
        _relayService.StopAdvertising();
        return Task.CompletedTask;
    }

    // Called by BleClientService scan results
    public void UpdateNeighborStats(int linkCount)
    {
        NeighborLinkCount = linkCount;
    }
}
