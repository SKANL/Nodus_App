using Microsoft.Extensions.Logging;
using System.Reactive.Linq;
using Moq;
using Nodus.Shared;
using Nodus.Shared.Services;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Nodus.Shared.Models;
using Nodus.Shared.Protocol;
using Shiny.BluetoothLE;
using Xunit;

namespace Nodus.Tests.Unit.Services;

public class BleClientServiceTests
{
    private readonly Mock<IBleManager> _bleManagerMock;
    private readonly Mock<ISecureStorageService> _secureStorageMock;
    private readonly Mock<ILogger<BleClientService>> _loggerMock;
    private readonly ChunkerService _chunkerService;
    private readonly BleClientService _sut;

    public BleClientServiceTests()
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

        // Setup default storage mocks
        _secureStorageMock.Setup(s => s.GetAsync(NodusConstants.KEY_SHARED_AES))
            .ReturnsAsync("dGVzdC1rZXktMTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM="); // test-key-123... (base64)
        _secureStorageMock.Setup(s => s.GetAsync(NodusConstants.KEY_JUDGE_NAME))
            .ReturnsAsync("Judge1");
    }

    [Fact]
    public async Task ConnectAsync_Success_ReturnsSuccessResult()
    {
        // Arrange
        var peripheralMock = new Mock<IPeripheral>();
        // Mock Status to return Connected so the extension method passes immediately
        peripheralMock.SetupGet(p => p.Status).Returns(ConnectionState.Connected);
        peripheralMock.Setup(p => p.WhenStatusChanged()).Returns(Observable.Return(ConnectionState.Connected));
        
        // Mock BleServiceInfo then BleCharacteristicInfo
        var serviceInfo = new BleServiceInfo(NodusConstants.SERVICE_UUID);
        var charInfo = new BleCharacteristicInfo(serviceInfo, NodusConstants.CHARACTERISTIC_UUID, false, CharacteristicProperties.Write);
        
        var successResult = new BleCharacteristicResult(
            charInfo, 
            BleCharacteristicEvent.Write, 
            null);

        peripheralMock.Setup(p => p.WriteCharacteristic(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()))
            .Returns(Observable.Return(successResult)); 

        // Act
        var result = await _sut.ConnectAsync(peripheralMock.Object);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(_sut.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_ConnectionFails_ReturnsFailure()
    {
        // Arrange
        var peripheralMock = new Mock<IPeripheral>();
        peripheralMock.SetupGet(p => p.Status).Returns(ConnectionState.Disconnected); // stays disconnected
        peripheralMock.Setup(p => p.WhenStatusChanged()).Returns(Observable.Return(ConnectionState.Disconnected));

        // Act
        // Pass short timeout to avoid waiting 30s
        var result = await _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromMilliseconds(100));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(_sut.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_WithRetry_SucceedsAfterFailures()
    {
        // Arrange
        var peripheralMock = new Mock<IPeripheral>();
        
        // Simulation: First attempts fail (Status=Disconnected), then succeeds (Status=Connected)
        int checkCount = 0;
        
        // Mock Status property to eventually return Connected
        peripheralMock.SetupGet(p => p.Status).Returns(() => 
        {
            return checkCount > 2 ? ConnectionState.Connected : ConnectionState.Disconnected;
        });

        // Mock Observable to return Disconnected first, then Connected
        // The extension method subscribes to this.
        peripheralMock.Setup(p => p.WhenStatusChanged()).Returns(() => 
        {
            checkCount++;
            if (checkCount > 2) 
            {
                 return Observable.Return(ConnectionState.Connected);
            }
            return Observable.Return(ConnectionState.Disconnected);
        });

        // Mock WriteChar for handshake
        var serviceInfo = new BleServiceInfo(NodusConstants.SERVICE_UUID);
        var charInfo = new BleCharacteristicInfo(serviceInfo, NodusConstants.CHARACTERISTIC_UUID, false, CharacteristicProperties.Write);
        
        var successResult = new BleCharacteristicResult(charInfo, BleCharacteristicEvent.Write, null);

        peripheralMock.Setup(p => p.WriteCharacteristic(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()))
            .Returns(Observable.Return(successResult));

        // Act
        // We need enough timeout for Retries (500ms initial + backoff) AND enough polling/accesses.
        // If we set timeout to 5s, and checking status is fast.
        var result = await _sut.ConnectAsync(peripheralMock.Object, TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(_sut.IsConnected);
    }

    [Fact]
    public async Task SendVoteAsync_NotConnected_ReturnsFailure()
    {
        // Arrange
        var vote = new Vote { Id = "v1" };

        // Act
        var result = await _sut.SendVoteAsync(vote);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Not connected", result.Error);
    }
}
