using Microsoft.Extensions.Logging;
using Moq;
using Nodus.Infrastructure.Services; // SwarmService lives in Nodus.Infrastructure
using Nodus.Shared.Abstractions;
using Xunit;

namespace Nodus.Tests.Unit.Services;

public class SwarmServiceTests
{
    private readonly Mock<IBleClientService> _bleClientMock;
    private readonly Mock<IRelayHostingService> _relayServiceMock;
    private readonly Mock<ITimerFactory> _timerFactoryMock;
    private readonly Mock<IAppTimer> _timerMock;
    private readonly Mock<IDateTimeProvider> _dateTimeMock;
    private readonly Mock<ILogger<SwarmService>> _loggerMock;
    private readonly SwarmService _sut;

    public SwarmServiceTests()
    {
        _bleClientMock = new Mock<IBleClientService>();
        _relayServiceMock = new Mock<IRelayHostingService>();
        _timerFactoryMock = new Mock<ITimerFactory>();
        _timerMock = new Mock<IAppTimer>();
        _dateTimeMock = new Mock<IDateTimeProvider>();
        _loggerMock = new Mock<ILogger<SwarmService>>();

        _timerFactoryMock.Setup(x => x.CreateTimer()).Returns(_timerMock.Object);
        _dateTimeMock.Setup(x => x.UtcNow).Returns(DateTime.UtcNow); // Default
        _dateTimeMock.Setup(x => x.Delay(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask); // Fast forward delays

        _sut = new SwarmService(
            _bleClientMock.Object, 
            _relayServiceMock.Object, 
            _timerFactoryMock.Object,
            _dateTimeMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task CheckStateAsync_SeekerToCandidate_WhenConnected()
    {
        // Arrange
        _sut.CurrentState = SwarmState.Seeker;
        _bleClientMock.Setup(x => x.IsConnected).Returns(true);
        _bleClientMock.Setup(x => x.LastRssi).Returns(-65); // Strong signal > -75 threshold
        _sut.UpdateNeighborStats(0);

        // Act
        await _sut.CheckStateAsync();

        // Assert
        // Logic: Seeker -> Connected -> Candidate -> Wait -> Link (if neighbors low)
        // CheckStateAsync awaits the delay, so it should reach Link state
        Assert.Equal(SwarmState.Link, _sut.CurrentState);
        _relayServiceMock.Verify(x => x.StartAdvertisingAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckStateAsync_SeekerAbortsPromotion_WhenTooManyNeighbors()
    {
        // Arrange
        _sut.CurrentState = SwarmState.Seeker;
        _bleClientMock.Setup(x => x.IsConnected).Returns(true);
        _sut.UpdateNeighborStats(5); // Too many neighbors

        // Act
        await _sut.CheckStateAsync();

        // Assert
        // Logic: Seeker -> Connected -> Candidate -> Wait -> Seeker (abort)
        Assert.Equal(SwarmState.Seeker, _sut.CurrentState);
        _relayServiceMock.Verify(x => x.StartAdvertisingAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckStateAsync_LinkToCooldown_AfterDuration()
    {
        // Arrange
        _sut.CurrentState = SwarmState.Link;
        // Logic uses _linkStartedAt = DateTime.MinValue by default.
        // So duration will be large -> Cooldown.
        
        // Act
        await _sut.CheckStateAsync();

        // Assert
        Assert.Equal(SwarmState.Cooldown, _sut.CurrentState);
        _relayServiceMock.Verify(x => x.StopAdvertising(), Times.Once);
    }

    [Fact]
    public async Task TestCooldownTimer_Enforcement()
    {
        // Arrange
        // 1. Start in Link state to trigger cooldown
        _sut.CurrentState = SwarmState.Link;
        var now = DateTime.UtcNow;
        _dateTimeMock.Setup(x => x.UtcNow).Returns(now);

        // 2. Advance time past MAX_LINK_DURATION_SECONDS (60s) to enter Cooldown
        var future = now.AddSeconds(61);
        _dateTimeMock.Setup(x => x.UtcNow).Returns(future);
        
        // Act 1: Transition Link -> Cooldown
        await _sut.CheckStateAsync();
        Assert.Equal(SwarmState.Cooldown, _sut.CurrentState);

        // 3. Advance time into cooldown but NOT expired (e.g. +2 mins)
        // Cooldown is 5 minutes.
        var midCooldown = future.AddMinutes(2);
        _dateTimeMock.Setup(x => x.UtcNow).Returns(midCooldown);

        // Act 2: Check state in mid-cooldown
        await _sut.CheckStateAsync();
        Assert.Equal(SwarmState.Cooldown, _sut.CurrentState);

        // 4. Advance time past cooldown expiration (+5 mins from start of cooldown)
        var cooldownExpired = future.AddMinutes(5).AddSeconds(1);
        _dateTimeMock.Setup(x => x.UtcNow).Returns(cooldownExpired);

        // Act 3: Check state after expiry
        await _sut.CheckStateAsync();

        // Assert
        Assert.Equal(SwarmState.Seeker, _sut.CurrentState); // Should return to seeker
    }

    [Fact]
    public async Task TestRssiThreshold_Validation()
    {
        // Verifies that a node does NOT promote to CANDIDATE when RSSI is weak (<= -75 dBm),
        // even if the BLE connection is active. This tests the real RSSI path (doc 12 §3B1).

        // Arrange
        _sut.CurrentState = SwarmState.Seeker;
        _bleClientMock.Setup(x => x.IsConnected).Returns(true);  // Connected but weak signal
        _bleClientMock.Setup(x => x.LastRssi).Returns(-80);       // Below -75 threshold

        // Act
        await _sut.CheckStateAsync();

        // Assert
        Assert.Equal(SwarmState.Seeker, _sut.CurrentState);
        _relayServiceMock.Verify(x => x.StartAdvertisingAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TestRssiThreshold_StrongSignal_Promotes()
    {
        // Verifies promotion IS suppressed when RSSI is exactly at the boundary (-75 dBm = NOT > threshold).

        // Arrange
        _sut.CurrentState = SwarmState.Seeker;
        _bleClientMock.Setup(x => x.IsConnected).Returns(true);
        _bleClientMock.Setup(x => x.LastRssi).Returns(-75); // Exactly at threshold -> should NOT promote (needs > -75)
        _sut.UpdateNeighborStats(0);

        // Act
        await _sut.CheckStateAsync();

        // Assert: -75 is NOT > -75, so stays Seeker
        Assert.Equal(SwarmState.Seeker, _sut.CurrentState);
        _relayServiceMock.Verify(x => x.StartAdvertisingAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
