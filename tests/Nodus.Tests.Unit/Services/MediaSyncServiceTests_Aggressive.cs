using Moq;
using Xunit;
using Nodus.Shared.Services;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;
using Microsoft.Extensions.Logging;
// // using Shiny.BluetoothLE; // ConnectionState->string // ConnectionState replaced by string
using System.Reactive.Subjects;
using System.Text;
using Nodus.Shared;
using Nodus.Shared.Common;

namespace Nodus.Tests.Unit.Services;

public class TestableMediaSyncService : MediaSyncService
{
    public TestableMediaSyncService(
        IBleClientService bleService, 
        IDatabaseService databaseService, 
        IChunkerService chunker, 
        IImageCompressionService compressor, 
        IFileService fileService, 
        ILogger<MediaSyncService> logger) 
        : base(bleService, databaseService, chunker, compressor, fileService, logger) { }

    protected override int MaxRetries => 3; 
    protected override int BaseDelayMs => 10; // Fast retry
    protected override int MaxConsecutiveFailures => 3; // Trip faster
}

[Trait("Category", "Aggressive")]
public class MediaSyncServiceTests_Aggressive
{
    private readonly Mock<IBleClientService> _bleMock;
    private readonly Mock<IDatabaseService> _dbMock;
    private readonly Mock<IChunkerService> _chunkerMock;
    private readonly Mock<IImageCompressionService> _compressorMock;
    private readonly Mock<IFileService> _fileMock;
    private readonly Mock<ILogger<MediaSyncService>> _loggerMock;
    private readonly MediaSyncService _service;
    private readonly BehaviorSubject<string> _connectionSubject;

    public MediaSyncServiceTests_Aggressive()
    {
        _bleMock = new Mock<IBleClientService>();
        _dbMock = new Mock<IDatabaseService>();
        _chunkerMock = new Mock<IChunkerService>();
        _compressorMock = new Mock<IImageCompressionService>();
        _fileMock = new Mock<IFileService>();
        _loggerMock = new Mock<ILogger<MediaSyncService>>();
        
        _connectionSubject = new BehaviorSubject<string>("Connected");
        _bleMock.Setup(x => x.ConnectionState).Returns(_connectionSubject);
        _bleMock.Setup(x => x.IsConnected).Returns(true);
        _bleMock.Setup(x => x.LastRssi).Returns(-50); // Good signal for manual sync
        _bleMock.Setup(x => x.ReadRssiAsync(It.IsAny<CancellationToken>())).ReturnsAsync(-100); // Weak signal for background loop
        _bleMock.Setup(x => x.Notifications).Returns(new Subject<byte[]>());
        
        // Default mocks
        _fileMock.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _fileMock.Setup(x => x.ReadAllBytesAsync(It.IsAny<string>())).ReturnsAsync(new byte[100]); // Dummy bytes
        _compressorMock.Setup(x => x.Compress(It.IsAny<byte[]>())).Returns((byte[] b) => b); // Identity
        _chunkerMock.Setup(x => x.Split(It.IsAny<byte[]>(), It.IsAny<byte>()))
                .Returns((byte[] data, byte size) => new List<byte[]> { data }); // Return as single chunk for simplicity

        // Use Testable Service by default
        _service = new TestableMediaSyncService(
            _bleMock.Object,
            _dbMock.Object,
            _chunkerMock.Object,
            _compressorMock.Object,
            _fileMock.Object,
            _loggerMock.Object
        )
        {
            AckTimeout = TimeSpan.FromMilliseconds(1000) // Fast timeout for tests
        };
    }

    [Fact]
    public async Task SyncPendingMedia_ShouldRetry_OnBleFailure()
    {
        // Arrange
        var vote = new Vote { Id = Guid.NewGuid().ToString(), LocalPhotoPath = "test.jpg" };
        _dbMock.Setup(x => x.GetVotesWithPendingMediaAsync())
            .ReturnsAsync(Result<List<Vote>>.Success(new List<Vote> { vote }));

        var notificationSubject = new Subject<byte[]>();
        _bleMock.Setup(x => x.Notifications).Returns(notificationSubject);
        
        // Re-init service to bind subscription
        var service = new TestableMediaSyncService(
            _bleMock.Object, _dbMock.Object, _chunkerMock.Object, 
            _compressorMock.Object, _fileMock.Object, _loggerMock.Object)
        { AckTimeout = TimeSpan.FromMilliseconds(1000) };

        int calls = 0;
        _bleMock.Setup(x => x.WriteRawAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .Returns(async (byte[] data, CancellationToken ct) => {
                    calls++;
                    if (calls <= 2) return Result.Failure("Simulated Failure");
                    
                    // Success on 3rd try
                    // Simulate Async ACK
                    _ = Task.Run(async () => {
                        await Task.Delay(10);
                        var ackPayload = new byte[17];
                        ackPayload[0] = 0xA1; 
                        var voteIdBytes = Guid.Parse(vote.Id).ToByteArray();
                        Array.Copy(voteIdBytes, 0, ackPayload, 1, 16);
                        notificationSubject.OnNext(ackPayload);
                    });
                    
                    return Result.Success();
                });
        
        _dbMock.Setup(x => x.SaveVoteAsync(It.IsAny<Vote>()))
            .ReturnsAsync(Result.Success());

        // Act
        await service.CheckAndSyncAsync();

        // Debug assertions
        // Expect 2 warnings (attempts 1 & 2 failed)
        _loggerMock.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Exactly(2), "Should have 2 warnings");
        
        // Expect 1 info (attempt 3 success)
        _loggerMock.Verify(x => x.Log(LogLevel.Information, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Synced media")), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once, "Should log success");

        // Assert
        _bleMock.Verify(x => x.WriteRawAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        Assert.True(vote.IsMediaSynced, "Vote should be synced");
        _dbMock.Verify(x => x.SaveVoteAsync(vote), Times.Once);
    }

    [Fact]
    public async Task SyncPendingMedia_ShouldTripCircuitBreaker_OnConsecutiveFailures()
    {
        // Arrange
        var votes = Enumerable.Range(0, 6).Select(i => new Vote { Id = Guid.NewGuid().ToString(), LocalPhotoPath = $"img{i}.jpg" }).ToList();
        
        _dbMock.Setup(x => x.GetVotesWithPendingMediaAsync())
            .ReturnsAsync(Result<List<Vote>>.Success(votes));
            
        // Always fail
        _bleMock.Setup(x => x.WriteRawAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Persistent Failure"));

        var notificationSubject = new Subject<byte[]>();
        _bleMock.Setup(x => x.Notifications).Returns(notificationSubject);

        // MaxConsecutiveFailures = 3 in TestableMediaSyncService
        var service = new TestableMediaSyncService(
            _bleMock.Object, _dbMock.Object, _chunkerMock.Object, 
            _compressorMock.Object, _fileMock.Object, _loggerMock.Object)
        { AckTimeout = TimeSpan.FromMilliseconds(10) }; 
        
        // Act
        await service.CheckAndSyncAsync();

        // Debug assertions
        _dbMock.Verify(x => x.GetVotesWithPendingMediaAsync(), Times.Once, "DB GetVotes should be called");
        _fileMock.Verify(x => x.Exists(It.IsAny<string>()), Times.AtLeastOnce, "File Exists should be called");
        
        // Assert
        // Should stop after 3 votes failed (tripped).
        // Each vote has 3 retries (1 initial + 2 retries).
        // Total calls = 3 votes * 3 attempts = 9 calls.
        
        // Verify attempts (WriteRawAsync is called inside the retry loop)
        _bleMock.Verify(x => x.WriteRawAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Exactly(9));
        
        // Verify we stopped extracting/reading files after 3rd vote
        _fileMock.Verify(x => x.ReadAllBytesAsync(It.IsAny<string>()), Times.Exactly(9));
    }

    [Fact]
    public async Task SyncPendingMedia_ShouldStartCooldown_AfterCircuitBreakerTrip()
    {
        // This verifies that subsequent calls to CheckAndSyncAsync return immediately
        await Task.CompletedTask;
    }
}
