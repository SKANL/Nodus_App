using Microsoft.Extensions.Logging;
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
        peripheralMock.Setup(p => p.Status).Returns(ConnectionState.Connected);
        peripheralMock.Setup(p => p.ConnectAsync(It.IsAny<ConnectionConfig>(), It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);
        
        // Mock BleServiceInfo then BleCharacteristicInfo
        var serviceInfo = new BleServiceInfo(NodusConstants.SERVICE_UUID);
        var charInfo = new BleCharacteristicInfo(serviceInfo, NodusConstants.CHARACTERISTIC_UUID, false, CharacteristicProperties.Write);
        
        var successResult = new BleCharacteristicResult(
            charInfo, 
            BleCharacteristicEvent.Write, 
            null);

        peripheralMock.Setup(p => p.WriteCharacteristicAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(),It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(successResult)); 

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
        peripheralMock.Setup(p => p.Status).Returns(ConnectionState.Disconnected); // stays disconnected
        peripheralMock.Setup(p => p.ConnectAsync(It.IsAny<ConnectionConfig>(), It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ConnectAsync(peripheralMock.Object);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(_sut.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_WithRetry_SucceedsAfterFailures()
    {
        // Arrange
        var peripheralMock = new Mock<IPeripheral>();
        peripheralMock.Setup(p => p.Status).Returns(ConnectionState.Disconnected); // Initially disconnected

        // Setup ConnectAsync to fail 2 times then succeed
        int callCount = 0;
        peripheralMock.Setup(p => p.ConnectAsync(It.IsAny<ConnectionConfig>(), It.IsAny<CancellationToken>()))
            .Returns(() => 
            {
                callCount++;
                if (callCount < 3) return Task.FromException(new Exception("Connection failed"));
                
                // On 3rd attempt, simulate connection success
                peripheralMock.Setup(p => p.Status).Returns(ConnectionState.Connected);
                return Task.CompletedTask;
            });

        // Mock BleServiceInfo then BleCharacteristicInfo
        var serviceInfo = new BleServiceInfo(NodusConstants.SERVICE_UUID);
        var charInfo = new BleCharacteristicInfo(serviceInfo, NodusConstants.CHARACTERISTIC_UUID, false, CharacteristicProperties.Write);

        var successResult = new BleCharacteristicResult(
            charInfo, 
            BleCharacteristicEvent.Write, 
            null);

        peripheralMock.Setup(p => p.WriteCharacteristicAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(),It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(successResult));

        // Act
        var result = await _sut.ConnectAsync(peripheralMock.Object);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(_sut.IsConnected);
        Assert.Equal(3, callCount); // verify retries happened
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
