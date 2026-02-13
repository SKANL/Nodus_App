using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nodus.Shared;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Nodus.Shared.Models;
using Nodus.Shared.Protocol;
using Nodus.Shared.Services;
using Shiny.BluetoothLE;
using System.Reactive.Linq;
using Xunit;

using Nodus.Shared.Security;
using Nodus.Tests.Unit.Services;

/// <summary>
/// Enhanced BleClientService tests following industry standards.
/// Tests are designed to DISCOVER BUGS in BLE communication layer.
/// </summary>
public class BleClientServiceTests_Enhanced : IDisposable
{
    private readonly Mock<IBleManager> _bleManagerMock;
    private readonly Mock<ISecureStorageService> _secureStorageMock;
    private readonly Mock<ILogger<BleClientService>> _loggerMock;
    private readonly ChunkerService _chunkerService;
    private readonly BleClientService _sut;

    public BleClientServiceTests_Enhanced()
    {
        _bleManagerMock = new Mock<IBleManager>();
        _secureStorageMock = new Mock<ISecureStorageService>();
        _loggerMock = new Mock<ILogger<BleClientService>>();
        _chunkerService = new ChunkerService();

        _sut = new BleClientService(
            _bleManagerMock.Object,
            _chunkerService,
            _secureStorageMock.Object,
            _loggerMock.Object);

        // Setup default secure storage with VALID keys
        var keys = CryptoHelper.GenerateSigningKeys();
        
        _secureStorageMock.Setup(s => s.GetAsync(NodusConstants.KEY_SHARED_AES))
            .ReturnsAsync("dGVzdC1rZXktMTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM="); // 32-byte key base64
        _secureStorageMock.Setup(s => s.GetAsync(NodusConstants.KEY_JUDGE_NAME))
            .ReturnsAsync("TestJudge");
        _secureStorageMock.Setup(s => s.GetAsync(NodusConstants.KEY_PRIVATE_KEY))
            .ReturnsAsync(keys.PrivateKeyBase64);
        _secureStorageMock.Setup(s => s.GetAsync(NodusConstants.KEY_PUBLIC_KEY))
            .ReturnsAsync(keys.PublicKeyBase64);
    }

    #region Connection Management Tests

    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_ShouldReturnSuccessWithoutReconnecting()
    {
        // Arrange - First connection
        var peripheral1 = CreateMockPeripheral("Server1", ConnectionState.Connected);
        await _sut.ConnectAsync(peripheral1.Object, TimeSpan.FromSeconds(1));

        // Act - Try to connect to different peripheral while already connected
        var peripheral2 = CreateMockPeripheral("Server2", ConnectionState.Connected);
        var result = await _sut.ConnectAsync(peripheral2.Object, TimeSpan.FromSeconds(1));

        // Assert - Should skip connection
        result.IsSuccess.Should().BeTrue();
        _sut.IsConnected.Should().BeTrue();
        
        // Verify second peripheral was NOT connected to
        peripheral2.Verify(p => p.ConnectAsync(It.IsAny<ConnectionConfig>(), It.IsAny<CancellationToken>()), 
            Times.Never, "should not connect when already connected");
    }

    [Fact]
    public async Task ConnectAsync_WhenTimeoutOccurs_ShouldReturnFailure()
    {
        // Arrange - Peripheral that never connects
        var peripheralMock = new Mock<IBlePeripheralWrapper>();
        peripheralMock.SetupGet(p => p.Name).Returns("SlowServer");
        peripheralMock.SetupGet(p => p.Status).Returns(ConnectionState.Connecting); // Stuck in connecting
        peripheralMock.Setup(p => p.WhenStatusChanged())
            .Returns(Observable.Never<ConnectionState>()); // Never completes
        
        // Simulate ConnectAsync that hangs
        peripheralMock.Setup(p => p.ConnectAsync(It.IsAny<ConnectionConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async (ConnectionConfig config, CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct); // Hang until cancelled
            });

        // Act - Very short timeout
        var result = await _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromMilliseconds(100));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cancelled", "timeout should trigger cancellation");
        _sut.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectAsync_WhenHandshakeFails_ShouldReturnFailure()
    {
        // Arrange - Peripheral connects but write fails
        var peripheralMock = CreateMockPeripheral("Server", ConnectionState.Connected);
        
        // Make WriteCharacteristic fail
        // Make WriteCharacteristicAsync fail (Task version)
        peripheralMock.Setup(p => p.WriteCharacteristicAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Write failed"));

        // Act
        var result = await _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(2));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Handshake failed");
        _sut.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task DisconnectAsync_WhenConnected_ShouldDisconnectSuccessfully()
    {
        // Arrange - Connect first
        var peripheralMock = CreateMockPeripheral("Server", ConnectionState.Connected);
        await _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(1));
        _sut.IsConnected.Should().BeTrue();

        // Act
        var result = await _sut.DisconnectAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        _sut.IsConnected.Should().BeFalse();
        peripheralMock.Verify(p => p.CancelConnection(), Times.Once);
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_ShouldReturnSuccess()
    {
        // Arrange - Not connected
        _sut.IsConnected.Should().BeFalse();

        // Act
        var result = await _sut.DisconnectAsync();

        // Assert - Should be idempotent
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Data Transfer Tests

    [Fact]
    public async Task SendVoteAsync_WhenNotConnected_ShouldReturnFailure()
    {
        // Arrange
        var vote = new Vote
        {
            EventId = "evt-1",
            ProjectId = "proj-1",
            JudgeId = "judge-1",
            PayloadJson = "{\"score\": 5}",
            Status = SyncStatus.Pending
        };

        // Act
        var result = await _sut.SendVoteAsync(vote);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Not connected");
    }

    [Fact]
    public async Task SendVoteAsync_WhenConnectionLostDuringTransmission_ShouldReturnFailure()
    {
        // Arrange - Connect first
        var peripheralMock = CreateMockPeripheral("Server", ConnectionState.Connected);
        await _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(1));

        // Simulate connection loss during write
        int writeCount = 0;
        peripheralMock.Setup(p => p.WriteCharacteristicAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(async (string svc, string chr, byte[] data, bool resp, CancellationToken ct) =>
            {
                writeCount++;
                if (writeCount > 2)
                {
                    // Simulate disconnection
                    peripheralMock.SetupGet(p => p.Status).Returns(ConnectionState.Disconnected);
                    throw new Exception("Connection lost");
                }
                await Task.CompletedTask;
            });

        var vote = new Vote
        {
            EventId = "evt-1",
            ProjectId = "proj-1",
            JudgeId = "judge-1",
            PayloadJson = new string('x', 1000), // Large payload to require multiple chunks
            Status = SyncStatus.Pending
        };

        // Act
        var result = await _sut.SendVoteAsync(vote);

        // Assert
        result.IsSuccess.Should().BeFalse();
        // Failed transmission can result in "Not connected" if retry happens after disconnection
        result.Error.Should().Match(e => e.Contains("failed") || e.Contains("Not connected"));
    }

    [Fact]
    public async Task WriteRawAsync_WithNullConnection_ShouldReturnFailure()
    {
        // Arrange - Not connected
        var data = new byte[] { 0x01, 0x02, 0x03 };

        // Act
        var result = await _sut.WriteRawAsync(data);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Not connected");
    }

    #endregion

    #region RSSI Monitoring Tests

    [Fact]
    public async Task ReadRssiAsync_WhenConnected_ShouldReturnValidRssi()
    {
        // Arrange - Connect first
        var peripheralMock = CreateMockPeripheral("Server", ConnectionState.Connected);
        peripheralMock.Setup(p => p.ReadRssiAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(-65);
        
        await _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(1));

        // Act
        var rssi = await _sut.ReadRssiAsync();

        // Assert
        rssi.Should().Be(-65);
        _sut.LastRssi.Should().Be(-65);
    }

    [Fact]
    public async Task ReadRssiAsync_WhenNotConnected_ShouldReturnErrorValue()
    {
        // Arrange - Not connected

        // Act
        var rssi = await _sut.ReadRssiAsync();

        // Assert
        rssi.Should().Be(-999, "should return error value when not connected");
    }

    [Fact]
    public async Task ReadRssiAsync_WhenReadFails_ShouldReturnLastKnownValue()
    {
        // Arrange - Connect and set initial RSSI
        var peripheralMock = CreateMockPeripheral("Server", ConnectionState.Connected);
        peripheralMock.Setup(p => p.ReadRssiAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(-70);
        
        await _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(1));
        await _sut.ReadRssiAsync(); // Set LastRssi to -70

        // Now make RSSI read fail
        peripheralMock.Setup(p => p.ReadRssiAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("RSSI read failed"));

        // Act
        var rssi = await _sut.ReadRssiAsync();

        // Assert
        rssi.Should().Be(-70, "should return last known RSSI on error");
    }

    #endregion

    #region Error Scenario Tests

    [Fact]
    public async Task ConnectAsync_WithCancellation_ShouldReturnFailure()
    {
        // Arrange
        var peripheralMock = CreateMockPeripheral("Server", ConnectionState.Connecting);
        var cts = new CancellationTokenSource();
        
        // Cancel immediately
        cts.Cancel();

        // Act
        var result = await _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(10), cts.Token);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cancelled");
    }

    [Fact]
    public async Task SendVoteAsync_WithMissingEncryptionKey_ShouldReturnFailure()
    {
        // Arrange - Connect first
        var peripheralMock = CreateMockPeripheral("Server", ConnectionState.Connected);
        await _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(1));

        // Remove encryption key
        _secureStorageMock.Setup(s => s.GetAsync(NodusConstants.KEY_SHARED_AES))
            .ReturnsAsync((string?)null);

        var vote = new Vote
        {
            EventId = "evt-1",
            ProjectId = "proj-1",
            JudgeId = "judge-1",
            PayloadJson = "{}",
            Status = SyncStatus.Pending
        };

        // Act
        var result = await _sut.SendVoteAsync(vote);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("encryption key", "should fail without encryption key");
    }

    [Fact]
    public async Task EnableNotificationsAsync_WhenNotConnected_ShouldReturnFailure()
    {
        // Arrange - Not connected

        // Act
        var result = await _sut.EnableNotificationsAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Not connected");
    }

    [Fact]
    public async Task RelayPacketAsync_WhenNotConnected_ShouldReturnFailure()
    {
        // Arrange
        var packet = new NodusPacket
        {
            Type = MessageType.Vote,
            SenderId = "judge-1",
            EncryptedPayload = new byte[] { 1, 2, 3 }
        };

        // Act
        var result = await _sut.RelayPacketAsync(packet);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Not connected");
    }

    #endregion

    #region Scanning Tests

    [Fact]
    public async Task StartScanningForServerAsync_WhenAlreadyScanning_ShouldReturnSuccess()
    {
        // Arrange
        _bleManagerMock.SetupGet(m => m.IsScanning).Returns(true);

        // Act
        var result = await _sut.StartScanningForServerAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        
        // Should not start new scan
        _bleManagerMock.Verify(m => m.Scan(It.IsAny<ScanConfig>()), Times.Never);
    }

    [Fact]
    public void StopScanning_ShouldClearNearbyLinks()
    {
        // Arrange - Setup scanning (would normally populate _nearbyLinks)
        bool linkCountChangedFired = false;
        _sut.LinkCountChanged += (s, count) => linkCountChangedFired = true;

        // Act
        _sut.StopScanning();

        // Assert
        linkCountChangedFired.Should().BeTrue("should fire LinkCountChanged event");
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task ConnectAsync_WhenCalledConcurrently_ShouldOnlyConnectOnce()
    {
        // Arrange
        var peripheralMock = CreateMockPeripheral("Server", ConnectionState.Connected);
        
        // Add delay to simulate slow connection
        peripheralMock.Setup(p => p.ConnectAsync(It.IsAny<ConnectionConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async (ConnectionConfig config, CancellationToken ct) =>
            {
                await Task.Delay(100, ct);
            });

        // Act - Try to connect concurrently
        var task1 = _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(5));
        var task2 = _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(5));
        var task3 = _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(5));

        var results = await Task.WhenAll(task1, task2, task3);

        // Assert - All should succeed but only one actual connection
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        
        // Verify ConnectAsync was called only once (due to lock)
        peripheralMock.Verify(p => p.ConnectAsync(It.IsAny<ConnectionConfig>(), It.IsAny<CancellationToken>()), 
            Times.Once, "connection lock should prevent concurrent connections");
    }

    [Fact]
    public async Task DisconnectAsync_WhileConnecting_ShouldCancelConnection()
    {
        // Arrange - Slow connection
        var peripheralMock = CreateMockPeripheral("Server", ConnectionState.Connecting);
        peripheralMock.Setup(p => p.ConnectAsync(It.IsAny<ConnectionConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async (ConnectionConfig config, CancellationToken ct) =>
            {
                await Task.Delay(5000, ct); // Long delay
            });

        // Act - Start connection and immediately disconnect
        var connectTask = _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(10));
        await Task.Delay(50); // Let connection start
        var disconnectResult = await _sut.DisconnectAsync();

        // Wait for connection to complete (should be cancelled)
        var connectResult = await connectTask;

        // Assert
        disconnectResult.IsSuccess.Should().BeTrue();
        connectResult.IsSuccess.Should().BeFalse("connection should be cancelled");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task SendVoteAsync_WithVeryLargePayload_ShouldChunkCorrectly()
    {
        // Arrange - Connect first
        var peripheralMock = CreateMockPeripheral("Server", ConnectionState.Connected);
        
        var writtenChunks = new List<byte[]>();
        peripheralMock.Setup(p => p.WriteCharacteristicAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, byte[], bool, CancellationToken>((s, c, data, r, ct) => writtenChunks.Add(data));
        // WriteCharacteristicAsync returns void Task, not Task<BleCharacteristicResult>

        await _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(1));

        // Create vote with large payload (>1KB)
        var vote = new Vote
        {
            EventId = "evt-1",
            ProjectId = "proj-1",
            JudgeId = "judge-1",
            PayloadJson = new string('x', 5000), // 5KB payload
            Status = SyncStatus.Pending
        };

        // Act
        var result = await _sut.SendVoteAsync(vote);

        // Assert
        result.IsSuccess.Should().BeTrue();
        writtenChunks.Should().NotBeEmpty("large payload should be chunked");
        writtenChunks.Count.Should().BeGreaterThan(1, "5KB payload should require multiple chunks");
    }

    [Fact]
    public void Dispose_ShouldCleanupResources()
    {
        // Arrange
        var peripheralMock = CreateMockPeripheral("Server", ConnectionState.Connected);

        // Act
        _sut.Dispose();

        // Assert - Should not throw
        // Verify cleanup happened
        _sut.IsConnected.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private Mock<IBlePeripheralWrapper> CreateMockPeripheral(string name, ConnectionState state)
    {
        var mock = new Mock<IBlePeripheralWrapper>();
        mock.SetupGet(p => p.Name).Returns(name);
        mock.SetupGet(p => p.Status).Returns(state);
        mock.SetupGet(p => p.Uuid).Returns(Guid.NewGuid().ToString());
        mock.Setup(p => p.WhenStatusChanged()).Returns(Observable.Return(state));
        
        // Mock successful connection (now mockable!)
        mock.Setup(p => p.ConnectAsync(It.IsAny<ConnectionConfig>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Mock successful write for handshake (now mockable!)
        var serviceInfo = new BleServiceInfo(NodusConstants.SERVICE_UUID);
        var charInfo = new BleCharacteristicInfo(serviceInfo, NodusConstants.CHARACTERISTIC_UUID, false, CharacteristicProperties.Write);
        var successResult = new BleCharacteristicResult(charInfo, BleCharacteristicEvent.Write, null);

        mock.Setup(p => p.WriteCharacteristic(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()))
            .Returns(Observable.Return(successResult));

        // Mock WriteCharacteristicAsync for data transfer (now mockable!)
        mock.Setup(p => p.WriteCharacteristicAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    #endregion

    public void Dispose()
    {
        _sut?.Dispose();
    }
}
