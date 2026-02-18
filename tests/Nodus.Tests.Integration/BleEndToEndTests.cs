using Moq;
using Xunit;
using Nodus.Shared.Services;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;
using Nodus.Shared.Common;
using Microsoft.Extensions.Logging;
using System.Reactive.Subjects;
using System.Text;
using Shiny.BluetoothLE; // For ConnectionState

namespace Nodus.Tests.Integration;

public class BleEndToEndTests
{
    private readonly Mock<IBleClientService> _bleClientMock;
    private readonly Mock<IDatabaseService> _clientDb;
    private readonly Mock<IDatabaseService> _serverDb;
    private readonly Mock<IChunkerService> _chunker;
    private readonly Mock<IImageCompressionService> _compressor;
    private readonly Mock<IFileService> _fileService;
    
    // Server Dependencies
    private readonly VoteAggregatorService _aggregator;
    
    // Client Dependnecies
    private readonly MediaSyncService _clientService;

    // Pipe
    // Unused callback removed

    public BleEndToEndTests()
    {
        // --- Shared Mocks ---
        var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        
        _chunker = new Mock<IChunkerService>();
        _bleClientMock = new Mock<IBleClientService>();
        _clientDb = new Mock<IDatabaseService>();
        // Real logic for chunker? No, implementation details.
        // But for E2E we want REAL chunker if possible, or simple mock.
        // Let's use simple mock that returns the whole payload as one chunk for simplicity, 
        // to avoid testing Chunker logic (which has its own tests).
        _chunker.Setup(x => x.Split(It.IsAny<byte[]>(), It.IsAny<byte>()))
             .Returns((byte[] data, byte size) => new List<byte[]> { data }); // Return as single chunk
        
        _compressor = new Mock<IImageCompressionService>();
        _compressor.Setup(x => x.Compress(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        
        _fileService = new Mock<IFileService>();
        _fileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _fileService.Setup(x => x.ReadAllBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 0xBE, 0xEF });
        _fileService.Setup(x => x.GetAppDataDirectory()).Returns("C:\\TestPath");
        _fileService.Setup(x => x.CreateDirectory(It.IsAny<string>()));
        _fileService.Setup(x => x.WriteAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);


        // --- Server Setup ---
        _serverDb = new Mock<IDatabaseService>();
        
        var aggregatorLogger = loggerFactory.CreateLogger<VoteAggregatorService>();
        
        _aggregator = new VoteAggregatorService(_serverDb.Object, aggregatorLogger);
        
        // Fix: Use VoteIngestionService in BleEndToEndTests
        var ingestionLogger = loggerFactory.CreateLogger<VoteIngestionService>();
        var ingestionService = new VoteIngestionService(_serverDb.Object, _aggregator, _fileService.Object, ingestionLogger);

        // We can test 'BleServerService' wrapper too if we want, OR just test 'VoteIngestionService' directly.
        // Testing BleServerService wrapper on Windows requires 'Mock<IBleHostingManager>', but we know it's guarded by #if ANDROID.
        // So on Windows, BleServerService is just the Stub.
        // Testing the stub is useless.
        // So we should test 'VoteIngestionService' directly in our "End To End" test, 
        // effectively simulating the BLE Transport layer stripping and passing payload to Ingestion.
        
        // Let's redefine 'EndToEnd' as 'Client -> BLE Mock -> Ingestion Service -> Server DB'.
        // This is still E2E logic, just skipping the Android-specific Gatt wrapper.

        // So `_serverService` (BleServerService) is NOT used in the test assertion loop directly, 
        // we use `ingestionService`.
        
        // But `MediaSyncService` (Client) calls `WriteRawAsync`.
        // Our Mock `_bleClientMock` traps this call.
        // We need to pipe it to `ingestionService.ProcessPayloadAsync`.

        // Create Subject for Notifications to control it
        var notifySubject = new Subject<byte[]>();
        _bleClientMock.Setup(x => x.Notifications).Returns(notifySubject);

        _bleClientMock.Setup(x => x.WriteRawAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(async (byte[] data, CancellationToken ct) => 
            {
                // Simulate pipe (Async)
                var result = await ingestionService.ProcessPayloadAsync(data);
                // If result is not null (Ack), we could feed it back to client Notification?
                // Client expects Ack via Notification.
                if (result != null)
                {
                    notifySubject.OnNext(result);
                }
                return Result.Success();
            });
            
        _bleClientMock.Setup(x => x.IsConnected).Returns(true);
        _bleClientMock.Setup(x => x.LastRssi).Returns(-50);
        _bleClientMock.Setup(x => x.ConnectionState).Returns(new BehaviorSubject<ConnectionState>(ConnectionState.Connected)); // Now ConnectionState should be resolved
        
        // Setup Client Service normally
        var clientLogger = loggerFactory.CreateLogger<MediaSyncService>();
        _clientService = new MediaSyncService(
            _bleClientMock.Object,
            _clientDb.Object,
            _chunker.Object,
            _compressor.Object,
            _fileService.Object,
            clientLogger
        );
    }

    [Fact]
    public async Task EndToEnd_Sync_VoteMedia_ShouldPersistOnServer()
    {
        // 1. (No Server Start needed since we bypass BleServerService Transport)
        
        // 2. Setup Client Pending Vote
        var voteId = Guid.NewGuid().ToString();
        var vote = new Vote 
        { 
            Id = voteId, 
            PayloadJson = "{}",
            LocalPhotoPath = "test.jpg",
            EventId = "TestEvent",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        
        _clientDb.Setup(x => x.GetVotesWithPendingMediaAsync())
            .ReturnsAsync(Result<List<Vote>>.Success(new List<Vote> { vote }));
            
        _clientDb.Setup(x => x.SaveVoteAsync(It.IsAny<Vote>()))
            .ReturnsAsync(Result.Success());

        // Server DB Verification Setup
        _serverDb.Setup(x => x.GetVoteByIdAsync(voteId))
            .ReturnsAsync(Result<Vote>.Success(new Vote { Id = voteId })); // Exist for media attach

        _serverDb.Setup(x => x.SaveVoteAsync(It.IsAny<Vote>()))
            .ReturnsAsync(Result.Success());
            
        // 3. Act - Run Sync on Client
        await _clientService.CheckAndSyncAsync();

        // 4. Assert
        // Verify IngestionService called ServerDB.SaveVoteAsync (to update LocalPhotoPath)
        _serverDb.Verify(x => x.SaveVoteAsync(It.Is<Vote>(v => v.Id == voteId && v.IsMediaSynced)), Times.Once);
        
        // Verify File Creation
        _fileService.Verify(x => x.WriteAllBytesAsync(It.Is<string>(s => s.Contains(voteId)), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

