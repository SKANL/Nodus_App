using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nodus.Shared.Services;
using Nodus.Shared;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Nodus.Shared.Models;
using Shiny.BluetoothLE;
using System.Reactive.Subjects;
using Xunit;

namespace Nodus.Tests.Unit.Services;

public class MediaSyncServiceTests_Enhanced
{
    private readonly Mock<IBleClientService> _bleServiceMock;
    private readonly Mock<IDatabaseService> _databaseServiceMock;
    private readonly Mock<IChunkerService> _chunkerMock;
    private readonly Mock<IImageCompressionService> _compressorMock;
    private readonly Mock<IFileService> _fileServiceMock;
    private readonly Mock<ILogger<MediaSyncService>> _loggerMock;
    private readonly MediaSyncService _sut;

    private readonly Subject<ConnectionState> _connectionStateSubject;
    private readonly Subject<byte[]> _notificationSubject;

    public MediaSyncServiceTests_Enhanced()
    {
        _bleServiceMock = new Mock<IBleClientService>();
        _databaseServiceMock = new Mock<IDatabaseService>();
        _chunkerMock = new Mock<IChunkerService>();
        _compressorMock = new Mock<IImageCompressionService>();
        _fileServiceMock = new Mock<IFileService>();
        _loggerMock = new Mock<ILogger<MediaSyncService>>();

        _connectionStateSubject = new Subject<ConnectionState>();
        _notificationSubject = new Subject<byte[]>();

        _bleServiceMock.Setup(x => x.ConnectionState).Returns(_connectionStateSubject);
        _bleServiceMock.Setup(x => x.Notifications).Returns(_notificationSubject);

        _sut = new MediaSyncService(
            _bleServiceMock.Object,
            _databaseServiceMock.Object,
            _chunkerMock.Object,
            _compressorMock.Object,
            _fileServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task CheckAndSyncAsync_WhenDisconnected_ShouldNotSync()
    {
        // Arrange
        _bleServiceMock.Setup(x => x.IsConnected).Returns(false);

        // Act
        await _sut.CheckAndSyncAsync(-50);

        // Assert
        _databaseServiceMock.Verify(x => x.GetVotesWithPendingMediaAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndSyncAsync_WhenRssiBelowThreshold_ShouldNotSync()
    {
        // Arrange
        _bleServiceMock.Setup(x => x.IsConnected).Returns(true);
        // Threshold is -75, so -80 is too weak
        int weakRssi = -80;

        // Act
        await _sut.CheckAndSyncAsync(weakRssi);

        // Assert
        _databaseServiceMock.Verify(x => x.GetVotesWithPendingMediaAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndSyncAsync_WhenRssiGood_ShouldSync()
    {
        // Arrange
        _bleServiceMock.Setup(x => x.IsConnected).Returns(true);
        _databaseServiceMock.Setup(x => x.GetVotesWithPendingMediaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<Vote>>.Success(new List<Vote>())); 
        
        int goodRssi = -60;

        // Act
        await _sut.CheckAndSyncAsync(goodRssi);

        // Assert
        _databaseServiceMock.Verify(x => x.GetVotesWithPendingMediaAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndSyncAsync_WhenFileMissing_ShouldLogWarningAndContinue()
    {
        // Arrange
        _bleServiceMock.Setup(x => x.IsConnected).Returns(true);
        
        var vote = new Vote { Id = Guid.NewGuid().ToString(), LocalPhotoPath = "missing.jpg" };
        _databaseServiceMock.Setup(x => x.GetVotesWithPendingMediaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<Vote>>.Success(new List<Vote> { vote }));

        _fileServiceMock.Setup(x => x.Exists(vote.LocalPhotoPath)).Returns(false);

        // Act
        await _sut.CheckAndSyncAsync(-50);

        // Assert
        _fileServiceMock.Verify(x => x.ReadAllBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Missing file")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckAndSyncAsync_WhenFileExists_ShouldCompressAndSend()
    {
        // Arrange
        _bleServiceMock.Setup(x => x.IsConnected).Returns(true);
        var voteId = Guid.NewGuid().ToString();
        var vote = new Vote { Id = voteId, LocalPhotoPath = "photo.jpg" };
        
        _databaseServiceMock.Setup(x => x.GetVotesWithPendingMediaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<Vote>>.Success(new List<Vote> { vote }));

        _fileServiceMock.Setup(x => x.Exists(vote.LocalPhotoPath)).Returns(true);
        _fileServiceMock.Setup(x => x.ReadAllBytesAsync(vote.LocalPhotoPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });

        _compressorMock.Setup(x => x.Compress(It.IsAny<byte[]>())).Returns(new byte[] { 4, 5 });
        
        var chunks = new List<byte[]> { new byte[] { 0x01 }, new byte[] { 0x02 } };
        _chunkerMock.Setup(x => x.Split(It.IsAny<byte[]>(), It.IsAny<byte>())).Returns(chunks);

        _bleServiceMock.Setup(x => x.WriteRawAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // Simulate ACK immediately after sending
        // This is tricky because WriteRawAsync is awaited. 
        // We can use Callback to simulate ACK reception *during* the process, 
        // OR better yet, since the code waits for ACK *after* sending chunks, 
        // we can trigger the notification using a delayed task or just relying on the fact 
        // that we need to trigger it *before* the timeout happens.
        
        // However, the test runs on a single thread usually (or similar context).
        // CheckAndSyncAsync awaits Task.WhenAny(tcs.Task, timeoutTask).
        
        // We can fire the notification immediately, but the TCS isn't created until *after* sending chunks.
        // So we need to fire the notification *after* sending enters the wait state.
        // BUT, we can't easily inject code *between* sending and waiting.
        
        // Wait! The loop sends chunks. After loop, it creates TCS.
        // If we fire notification *during* one of the WriteRawAsync calls, the TCS won't exist yet!
        // The TCS is created at line 203, AFTER the loop.
        // Logic flaw in test plan?
        // Actually, the Service stores TCS in `_pendingAcks`.
        // If we receive the notification *too early*, `_pendingAcks` won't have the ID yet, so it does nothing.
        
        // Solution: Do not wait for ACK in this test if possible, or mock WriteRawAsync to trigger a background task
        // that waits a bit (shimmy) and then fires notification.
        
        _bleServiceMock.Setup(x => x.WriteRawAsync(It.Is<byte[]>(b => b == chunks.Last()), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success())
            .Callback(() => 
            {
               // Fire ACK after a small delay to ensure TCS is registered
               Task.Run(async () => 
               {
                   await Task.Delay(50);
                   var ackPacket = new byte[17];
                   ackPacket[0] = NodusConstants.PACKET_TYPE_ACK; // 0xA1
                   var idBytes = Guid.Parse(voteId).ToByteArray();
                   Array.Copy(idBytes, 0, ackPacket, 1, 16);
                   _notificationSubject.OnNext(ackPacket);
               });
            });

        // Act
        await _sut.CheckAndSyncAsync(-50);

        // Assert
        _chunkerMock.Verify(x => x.Split(It.IsAny<byte[]>(), It.IsAny<byte>()), Times.Once);
        _bleServiceMock.Verify(x => x.WriteRawAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        
        // Verify Vote was saved as Synced
        _databaseServiceMock.Verify(x => x.SaveVoteAsync(It.Is<Vote>(v => v.IsMediaSynced == true), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndSyncAsync_WhenAckTimeout_ShouldThrowAndNotSave()
    {
        // Arrange
        _sut.AckTimeout = TimeSpan.FromMilliseconds(50);
        
        _bleServiceMock.Setup(x => x.IsConnected).Returns(true);
        var voteId = Guid.NewGuid().ToString();
        var vote = new Vote { Id = voteId, LocalPhotoPath = "photo.jpg" };
        
        _databaseServiceMock.Setup(x => x.GetVotesWithPendingMediaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<Vote>>.Success(new List<Vote> { vote }));

        _fileServiceMock.Setup(x => x.Exists(vote.LocalPhotoPath)).Returns(true);
        _fileServiceMock.Setup(x => x.ReadAllBytesAsync(vote.LocalPhotoPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1 });
        _compressorMock.Setup(x => x.Compress(It.IsAny<byte[]>())).Returns(new byte[] { 1 });
        _chunkerMock.Setup(x => x.Split(It.IsAny<byte[]>(), It.IsAny<byte>())).Returns(new List<byte[]> { new byte[] { 1 } });
        _bleServiceMock.Setup(x => x.WriteRawAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // We DO NOT fire ACK.

        // Act
        // The method catches exception and logs it, but it should NOT save the vote as synced
        await _sut.CheckAndSyncAsync(-50);

        // Assert
        _databaseServiceMock.Verify(x => x.SaveVoteAsync(It.IsAny<Vote>(), It.IsAny<CancellationToken>()), Times.Never);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to sync vote")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task CheckAndSyncAsync_WhenMultipleVotes_ShouldSyncAll()
    {
        // Arrange
        _bleServiceMock.Setup(x => x.IsConnected).Returns(true);
        
        var voteId1 = Guid.NewGuid().ToString();
        var voteId2 = Guid.NewGuid().ToString();
        var vote1 = new Vote { Id = voteId1, LocalPhotoPath = "p1.jpg" };
        var vote2 = new Vote { Id = voteId2, LocalPhotoPath = "p2.jpg" };
        
        _databaseServiceMock.Setup(x => x.GetVotesWithPendingMediaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<Vote>>.Success(new List<Vote> { vote1, vote2 }));

        _fileServiceMock.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _fileServiceMock.Setup(x => x.ReadAllBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1 });
        _compressorMock.Setup(x => x.Compress(It.IsAny<byte[]>())).Returns(new byte[] { 1 });
        _chunkerMock.Setup(x => x.Split(It.IsAny<byte[]>(), It.IsAny<byte>())).Returns(new List<byte[]> { new byte[] { 1 } });

        _bleServiceMock.Setup(x => x.WriteRawAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success())
            .Callback<byte[], CancellationToken>((data, ct) => 
            {
                 // Which vote is currently being processed?
                 // Since we can't easily know in callback which vote it is without parsing data,
                 // We can just rely on the fact that sequential processing happens.
                 // We will fire ACK for *both* or handle dynamically.
                 // But wait, the Service processes strictly sequentially.
            });
            
        // We need to fire ACK for v1, then wait, then fire ACK for v2.
        // This is complex to mock with simple Callback.
        // We can just set AckTimeout to very small, but then they will fail.
        // We need to successfuly ACK.
        
        // Mock WriteRawAsync to fire correct ACK based on data
        _bleServiceMock.Setup(x => x.WriteRawAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success())
            .Callback<byte[], CancellationToken>((data, ct) => 
            {
                // data[0] is messageId. 
                // We'd need to map messageId to voteId.
                // HashCode logic is: byte msgId = (byte)(vote.Id.GetHashCode() & 0xFF);
                
                // Let's just fire ACK for *both* IDs whenever WriteRawAsync is called? 
                // The service checks `_pendingAcks`. If we fire ACK for v2 while v1 is pending, it ignores it.
                // If we fire ACK for v1 while v1 is pending, it completes v1.
                
                // So, firing BOTH ACKs is safe!
                
                Task.Run(async () => 
                {
                    await Task.Delay(100);
                    FireAck(voteId1);
                    FireAck(voteId2);
                });
            });

        // Act
        await _sut.CheckAndSyncAsync(-50);

        // Assert
        _databaseServiceMock.Verify(x => x.SaveVoteAsync(It.Is<Vote>(v => v.Id == voteId1 && v.IsMediaSynced), It.IsAny<CancellationToken>()), Times.Once);
        _databaseServiceMock.Verify(x => x.SaveVoteAsync(It.Is<Vote>(v => v.Id == voteId2 && v.IsMediaSynced), It.IsAny<CancellationToken>()), Times.Once);
    }

    private void FireAck(string voteId)
    {
        var ackPacket = new byte[17];
        ackPacket[0] = NodusConstants.PACKET_TYPE_ACK;
        var idBytes = Guid.Parse(voteId).ToByteArray(); 
        Array.Copy(idBytes, 0, ackPacket, 1, 16);
        _notificationSubject.OnNext(ackPacket);
    }
}
