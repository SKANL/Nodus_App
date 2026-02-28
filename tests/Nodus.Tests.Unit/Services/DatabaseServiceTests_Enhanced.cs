using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Nodus.Shared.Models;
using Nodus.Shared.Services;
using Xunit;
using DatabaseService = Nodus.Infrastructure.Services.LocalDatabaseService;

namespace Nodus.Tests.Unit.Services;

/// <summary>
/// Enhanced DatabaseService tests following industry standards.
/// Tests are designed to DISCOVER BUGS, not just pass.
/// </summary>
public class DatabaseServiceTests_Enhanced : IDisposable
{
    private readonly DatabaseService _sut;
    private readonly string _testDbPath;
    private readonly ILogger<DatabaseService> _logger;
    private readonly Mock<IFileService> _fileServiceMock;

    public DatabaseServiceTests_Enhanced()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"nodus_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDbPath);
        _logger = new Mock<ILogger<DatabaseService>>().Object;
        _fileServiceMock = new Mock<IFileService>();
        _fileServiceMock.Setup(x => x.GetAppDataDirectory()).Returns(_testDbPath);
        _fileServiceMock.Setup(x => x.CreateDirectory(It.IsAny<string>()));
        _sut = new DatabaseService(_fileServiceMock.Object);
    }

    #region Concurrency Tests - CRITICAL for discovering race conditions

    [Fact]
    public async Task SaveVoteAsync_WhenCalledConcurrently_ShouldHandleAllRequests()
    {
        // Arrange - Create 100 concurrent vote save operations
        var tasks = new List<Task<Result>>();
        
        for (int i = 0; i < 100; i++)
        {
            var vote = new Vote
            {
                EventId = "evt-concurrent",
                ProjectId = $"proj-{i}",
                JudgeId = "judge-1",
                PayloadJson = $"{{\"score\": {i}}}",
                Status = SyncStatus.Pending
            };
            
            tasks.Add(_sut.SaveVoteAsync(vote));
        }

        // Act - Execute all saves concurrently
        var results = await Task.WhenAll(tasks);

        // Assert - All should succeed without deadlocks or data corruption
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        
        // Verify all votes were actually saved
        var allVotes = await _sut.GetPendingVotesAsync();
        allVotes.IsSuccess.Should().BeTrue();
        allVotes.Value.Should().HaveCount(100);
    }

    [Fact]
    public async Task GetPendingVotesAsync_WhileVotesAreBeingAdded_ShouldReturnConsistentResults()
    {
        // Arrange - Start adding votes in background
        var addTask = Task.Run(async () =>
        {
            for (int i = 0; i < 50; i++)
            {
                var vote = new Vote
                {
                    EventId = "evt-1",
                    ProjectId = $"proj-{i}",
                    JudgeId = "judge-1",
                    PayloadJson = "{}",
                    Status = SyncStatus.Pending
                };
                await _sut.SaveVoteAsync(vote);
                await Task.Delay(10); // Simulate real-world timing
            }
        });

        // Act - Read while writing
        var readTasks = new List<Task<Result<List<Vote>>>>();
        for (int i = 0; i < 20; i++)
        {
            readTasks.Add(_sut.GetPendingVotesAsync());
            await Task.Delay(15);
        }

        await addTask;
        var results = await Task.WhenAll(readTasks);

        // Assert - No reads should fail, counts should be monotonically increasing
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        
        var counts = results.Select(r => r.Value!.Count).ToList();
        for (int i = 1; i < counts.Count; i++)
        {
            counts[i].Should().BeGreaterThanOrEqualTo(counts[i - 1], 
                "vote count should never decrease during concurrent reads");
        }
    }

    [Fact]
    public async Task SaveVoteAsync_WhenUpdatingConcurrently_ShouldNotLoseUpdates()
    {
        // Arrange - Create a vote
        var vote = new Vote
        {
            EventId = "evt-1",
            ProjectId = "proj-1",
            JudgeId = "judge-1",
            PayloadJson = "{\"score\": 0}",
            Status = SyncStatus.Pending
        };
        await _sut.SaveVoteAsync(vote);

        // Act - Try to update the same vote concurrently with different statuses
        var updateTasks = new List<Task<Result>>();
        for (int i = 0; i < 10; i++)
        {
            var updatedVote = new Vote
            {
                Id = vote.Id,
                EventId = vote.EventId,
                ProjectId = vote.ProjectId,
                JudgeId = vote.JudgeId,
                PayloadJson = $"{{\"score\": {i}}}",
                Status = i % 2 == 0 ? SyncStatus.Synced : SyncStatus.Pending
            };
            updateTasks.Add(_sut.SaveVoteAsync(updatedVote));
        }

        var results = await Task.WhenAll(updateTasks);

        // Assert - All updates should succeed
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        
        // Final state should be one of the updates (last write wins)
        var final = await _sut.GetVoteByIdAsync(vote.Id);
        final.IsSuccess.Should().BeTrue();
        final.Value.Should().NotBeNull();
    }

    #endregion

    #region Null and Invalid Input Tests - Edge cases

    [Fact]
    public async Task SaveVoteAsync_WithNullVote_ShouldReturnFailure()
    {
        // Arrange
        Vote? nullVote = null;

        // Act
        var result = await _sut.SaveVoteAsync(nullVote!);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("null");
    }

    [Fact]
    public async Task SaveVoteAsync_WithEmptyEventId_ShouldReturnFailure()
    {
        // Arrange
        var vote = new Vote
        {
            EventId = "", // Invalid
            ProjectId = "proj-1",
            JudgeId = "judge-1",
            PayloadJson = "{}",
            Status = SyncStatus.Pending
        };

        // Act
        var result = await _sut.SaveVoteAsync(vote);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("EventId");
    }

    [Fact]
    public async Task SaveVoteAsync_WithNullPayloadJson_ShouldReturnFailure()
    {
        // Arrange
        var vote = new Vote
        {
            EventId = "evt-1",
            ProjectId = "proj-1",
            JudgeId = "judge-1",
            PayloadJson = null!, // Invalid
            Status = SyncStatus.Pending
        };

        // Act
        var result = await _sut.SaveVoteAsync(vote);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("PayloadJson");
    }

    [Fact]
    public async Task SaveVoteAsync_WithInvalidJson_ShouldStillSave()
    {
        // Arrange - DatabaseService should not validate JSON structure
        var vote = new Vote
        {
            EventId = "evt-1",
            ProjectId = "proj-1",
            JudgeId = "judge-1",
            PayloadJson = "this is not valid json {{{",
            Status = SyncStatus.Pending
        };

        // Act
        var result = await _sut.SaveVoteAsync(vote);

        // Assert - Should save (validation is application layer responsibility)
        result.IsSuccess.Should().BeTrue();
        
        var retrieved = await _sut.GetVoteByIdAsync(vote.Id);
        retrieved.Value!.PayloadJson.Should().Be("this is not valid json {{{");
    }

    #endregion

    #region Boundary Value Tests

    [Fact]
    public async Task SaveVoteAsync_WithVeryLargePayload_ShouldHandleCorrectly()
    {
        // Arrange - Create a 1MB JSON payload
        var largePayload = new string('x', 1024 * 1024);
        var vote = new Vote
        {
            EventId = "evt-1",
            ProjectId = "proj-1",
            JudgeId = "judge-1",
            PayloadJson = largePayload,
            Status = SyncStatus.Pending
        };

        // Act
        var result = await _sut.SaveVoteAsync(vote);

        // Assert
        result.IsSuccess.Should().BeTrue();
        
        var retrieved = await _sut.GetVoteByIdAsync(vote.Id);
        retrieved.Value!.PayloadJson.Length.Should().Be(1024 * 1024);
    }

    [Fact]
    public async Task GetPendingVotesAsync_WithThousandsOfVotes_ShouldCompleteInReasonableTime()
    {
        // Arrange - Create 1000 votes
        for (int i = 0; i < 1000; i++)
        {
            var vote = new Vote
            {
                EventId = "evt-perf",
                ProjectId = $"proj-{i}",
                JudgeId = "judge-1",
                PayloadJson = "{}",
                Status = SyncStatus.Pending
            };
            await _sut.SaveVoteAsync(vote);
        }

        // Act - Measure query performance
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await _sut.GetPendingVotesAsync();
        sw.Stop();

        // Assert - Should complete in under 1 second
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1000);
        sw.ElapsedMilliseconds.Should().BeLessThan(1000, 
            "querying 1000 votes should be fast");
    }

    #endregion

    #region Data Integrity Tests

    [Fact]
    public async Task SaveVoteAsync_WhenVoteIdIsDuplicate_ShouldUpdate()
    {
        // Arrange - Save initial vote
        var vote = new Vote
        {
            EventId = "evt-1",
            ProjectId = "proj-1",
            JudgeId = "judge-1",
            PayloadJson = "{\"score\": 5}",
            Status = SyncStatus.Pending
        };
        await _sut.SaveVoteAsync(vote);

        // Act - Save again with same ID but different data
        vote.PayloadJson = "{\"score\": 10}";
        vote.Status = SyncStatus.Synced;
        var result = await _sut.SaveVoteAsync(vote);

        // Assert - Should update, not create duplicate
        result.IsSuccess.Should().BeTrue();
        
        var allVotes = await _sut.GetPendingVotesAsync();
        allVotes.Value.Should().HaveCount(0, "vote should be synced now");
        
        var retrieved = await _sut.GetVoteByIdAsync(vote.Id);
        retrieved.Value!.PayloadJson.Should().Be("{\"score\": 10}");
        retrieved.Value!.Status.Should().Be(SyncStatus.Synced);
    }

    [Fact]
    public async Task GetSyncStatsAsync_WithMixedStatuses_ShouldCalculateAccurately()
    {
        // Arrange - Create votes with different statuses
        var votes = new[]
        {
            new Vote { EventId = "e1", ProjectId = "p1", JudgeId = "j1", PayloadJson = "{}", Status = SyncStatus.Pending },
            new Vote { EventId = "e1", ProjectId = "p2", JudgeId = "j1", PayloadJson = "{}", Status = SyncStatus.Pending },
            new Vote { EventId = "e1", ProjectId = "p3", JudgeId = "j1", PayloadJson = "{}", Status = SyncStatus.Synced },
            new Vote { EventId = "e1", ProjectId = "p4", JudgeId = "j1", PayloadJson = "{}", Status = SyncStatus.Synced, LocalPhotoPath = "/path.jpg", IsMediaSynced = false },
            new Vote { EventId = "e1", ProjectId = "p5", JudgeId = "j1", PayloadJson = "{}", Status = SyncStatus.Synced, LocalPhotoPath = "/path2.jpg", IsMediaSynced = true },
        };

        foreach (var vote in votes)
        {
            await _sut.SaveVoteAsync(vote);
        }

        // Act
        var stats = await _sut.GetSyncStatsAsync();

        // Assert
        stats.IsSuccess.Should().BeTrue();
        stats.Value!.TotalVotes.Should().Be(5);
        stats.Value!.PendingVotes.Should().Be(2);
        stats.Value!.SyncedVotes.Should().Be(3);
        stats.Value!.PendingMedia.Should().Be(1);
        stats.Value!.SyncPercentage.Should().BeApproximately(60.0, 0.1);
    }

    #endregion

    #region Transaction and Rollback Tests

    [Fact]
    public async Task SaveMultipleVotes_WhenOneFailsValidation_ShouldNotAffectOthers()
    {
        // Arrange
        var validVote = new Vote
        {
            EventId = "evt-1",
            ProjectId = "proj-1",
            JudgeId = "judge-1",
            PayloadJson = "{}",
            Status = SyncStatus.Pending
        };

        var invalidVote = new Vote
        {
            EventId = "", // Invalid
            ProjectId = "proj-2",
            JudgeId = "judge-1",
            PayloadJson = "{}",
            Status = SyncStatus.Pending
        };

        // Act
        var result1 = await _sut.SaveVoteAsync(validVote);
        var result2 = await _sut.SaveVoteAsync(invalidVote);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeFalse();
        
        var allVotes = await _sut.GetPendingVotesAsync();
        allVotes.Value.Should().HaveCount(1, "only valid vote should be saved");
    }

    #endregion

    #region Empty Collection Tests

    [Fact]
    public async Task GetPendingVotesAsync_WhenNoVotes_ShouldReturnEmptyList()
    {
        // Act
        var result = await _sut.GetPendingVotesAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSyncStatsAsync_WhenNoVotes_ShouldReturnZeroStats()
    {
        // Act
        var result = await _sut.GetSyncStatsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalVotes.Should().Be(0);
        result.Value!.PendingVotes.Should().Be(0);
        result.Value!.SyncedVotes.Should().Be(0);
        result.Value!.PendingMedia.Should().Be(0);
        result.Value!.SyncPercentage.Should().Be(0);
    }

    #endregion

    public void Dispose()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Directory.Exists(_testDbPath))
                Directory.Delete(_testDbPath, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
