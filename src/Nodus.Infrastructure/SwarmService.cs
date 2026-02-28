using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nodus.Shared;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Services;

namespace Nodus.Infrastructure.Services;

public enum SwarmState
{
    Seeker,     // Default: Silent, Scanning
    Candidate,  // Thinking about becoming a Link (Trickle Wait)
    Link,       // Advertising as Relay
    Cooldown    // Resting
}

public interface ISwarmService
{
    SwarmState CurrentState { get; set; } // Settable for tests if needed, or make read-only via method
    int NeighborLinkCount { get; }
    bool IsMuleMode { get; }
    void UpdateNeighborStats(int linkCount);
    // Task StartServiceAsync? Or automatic on ctor? Nodus is automatic usually.
}

public class SwarmService : ISwarmService, INotifyPropertyChanged
{
    private readonly IBleClientService _bleClient;
    private readonly IRelayHostingService _relayService;
    private readonly ILogger<SwarmService> _logger;
    private readonly ITimerFactory _timerFactory;
    private readonly IDateTimeProvider _dateTime;

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private SwarmState _currentState = SwarmState.Seeker;
    public SwarmState CurrentState
    {
        get => _currentState;
        set { if (_currentState == value) return; _currentState = value; OnPropertyChanged(); }
    }

    private int _neighborLinkCount;
    public int NeighborLinkCount
    {
        get => _neighborLinkCount;
        private set { if (_neighborLinkCount == value) return; _neighborLinkCount = value; OnPropertyChanged(); }
    }

    private bool _isMuleMode;
    public bool IsMuleMode
    {
        get => _isMuleMode;
        private set { if (_isMuleMode == value) return; _isMuleMode = value; OnPropertyChanged(); }
    }

    private IAppTimer? _heartbeat;
    private DateTime _cooldownExpiresAt = DateTime.MinValue;
    private DateTime _linkStartedAt = DateTime.MinValue;
    private DateTime _lastServerConnectionAt; // Set in ctor

    /// <summary>
    /// Guards against concurrent Seeker→Candidate promotions when heartbeat ticks
    /// overlap during the Trickle wait period.
    /// </summary>
    private volatile bool _candidateInProgress = false;

    private const int RSSI_THRESHOLD = -75; // Strong signal required (doc 12 §3B1)
    private const int MAX_LINK_DURATION_SECONDS = 60;
    private const int COOLDOWN_MINUTES = 5;
    private const int MULE_MODE_THRESHOLD_MINUTES = 10;

    public SwarmService(
        IBleClientService bleClient,
        IRelayHostingService relayService,
        ITimerFactory timerFactory,
        IDateTimeProvider dateTime,
        ILogger<SwarmService> logger)
    {
        _bleClient = bleClient;
        _relayService = relayService;
        _timerFactory = timerFactory;
        _dateTime = dateTime;
        _logger = logger;

        _lastServerConnectionAt = _dateTime.UtcNow;

        _bleClient.LinkCountChanged += (s, count) =>
        {
            NeighborLinkCount = count;
            _logger.LogDebug("Neighbor Link Count Updated: {Count}", count);
        };

        _bleClient.ConnectionStatusChanged += (s, isConnected) =>
        {
            if (isConnected) _lastServerConnectionAt = _dateTime.UtcNow;
        };

        StartHeartbeat();
    }

    private void StartHeartbeat()
    {
        _heartbeat = _timerFactory?.CreateTimer();
        if (_heartbeat != null)
        {
            _heartbeat.Interval = TimeSpan.FromSeconds(5);
            _heartbeat.Tick += async (s, e) => await CheckStateAsync();
            _heartbeat.Start();
        }
    }

    /// <summary>
    /// Internal for Unit Testing. Executes the state machine logic.
    /// </summary>
    public async Task CheckStateAsync()
    {
        // 0. Mule Mode Check
        var minutesSince = (_dateTime.UtcNow - _lastServerConnectionAt).TotalMinutes;
        IsMuleMode = (minutesSince > MULE_MODE_THRESHOLD_MINUTES);

        // 1. Cooldown Check
        if (CurrentState == SwarmState.Cooldown)
        {
            if (_dateTime.UtcNow > _cooldownExpiresAt)
            {
                _logger.LogDebug("Cooldown expired. Returning to Seeker.");
                CurrentState = SwarmState.Seeker;
            }
            return;
        }

        // 2. Link Duration Check
        if (CurrentState == SwarmState.Link)
        {
            var duration = (_dateTime.UtcNow - _linkStartedAt).TotalSeconds;
            if (duration > MAX_LINK_DURATION_SECONDS)
            {
                _logger.LogDebug("Link duration maxed. Entering Cooldown.");
                await StopRelayAsync();
                CurrentState = SwarmState.Cooldown;
                _cooldownExpiresAt = _dateTime.UtcNow.AddMinutes(COOLDOWN_MINUTES);
            }
            return;
        }

        // 3. Candidate Promotion Logic (Trickle)
        if (CurrentState == SwarmState.Seeker)
        {
            // Guard: only one promotion attempt at a time (heartbeat could fire again during T_wait)
            if (_candidateInProgress) return;

            // Signal quality check (doc 12 §3B1): requires RSSI > -75 dBm.
            // LastRssi is updated by BleClientService on every scan result.
            bool hasStrongSignal = _bleClient.IsConnected && _bleClient.LastRssi > RSSI_THRESHOLD;

            if (hasStrongSignal)
            {
                _candidateInProgress = true;
                try
                {
                    // Enter Candidate Mode
                    CurrentState = SwarmState.Candidate;

                    // Trickle random wait: 5-30s (doc 12 §3B1 "T_wait = Random(5s, 30s)")
                    var randomWait = Random.Shared.Next(5000, 30000);
                    _logger.LogDebug("Candidate! Waiting {RandomWait}ms (RSSI={Rssi})...", randomWait, _bleClient.LastRssi);

                    await _dateTime.Delay(TimeSpan.FromMilliseconds(randomWait));

                    // Redundancy Check — Trickle k=2 (doc 02 §Trickle, doc 11 §1B)
                    // If ≥2 neighbors are already acting as Links, suppress promotion.
                    if (NeighborLinkCount >= 2)
                    {
                        _logger.LogDebug("Too many neighbors ({Count}). Aborting promotion.", NeighborLinkCount);
                        CurrentState = SwarmState.Seeker;
                    }
                    else
                    {
                        // Promote to LINK!
                        _logger.LogInformation("Promoted to LINK! RSSI={Rssi}", _bleClient.LastRssi);
                        CurrentState = SwarmState.Link;
                        _linkStartedAt = _dateTime.UtcNow;
                        await StartRelayAsync();
                    }
                }
                finally
                {
                    _candidateInProgress = false;
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
