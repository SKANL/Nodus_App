using Microsoft.Extensions.Logging;
using Nodus.Infrastructure.Services;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;

namespace Nodus.Server.Services;

public class CloudSyncService
{
    private readonly MongoDbService _mongoDb;
    private readonly IDatabaseService _localDb;
    private readonly ILogger<CloudSyncService> _logger;
    private System.Timers.Timer? _syncTimer;
    // Prevents concurrent sync runs when the timer fires while a previous sync is still running.
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public CloudSyncService(MongoDbService mongoDb, IDatabaseService localDb, ILogger<CloudSyncService> logger)
    {
        _mongoDb = mongoDb;
        _localDb = localDb;
        _logger = logger;

        _logger.LogInformation("CloudSyncService initialized");
    }

    public void Start()
    {
        if (_syncTimer != null) return;

        // Initial sync
        _ = SyncAllAsync();

        // Sync every 5 minutes
        _syncTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
        _syncTimer.Elapsed += async (s, e) => await SyncAllAsync();
        _syncTimer.Start();

        _logger.LogInformation("CloudSyncService started (Auto-sync: 5m)");
    }

    public async Task SyncAllAsync()
    {
        // Guard: skip if a sync is already in progress (timer fired again before completion).
        if (!await _syncLock.WaitAsync(0))
        {
            _logger.LogWarning("Sync skipped — previous sync still running.");
            return;
        }

        try
        {
            _logger.LogInformation("Starting bidirectional Cloud <-> Local Sync...");

            // ── PULL: Cloud → Local ──────────────────────────────────────────────

            // 1. Pull Projects from MongoDB → LiteDB
            var cloudProjects = await _mongoDb.GetAllProjectsAsync();
            if (cloudProjects.IsSuccess)
            {
                foreach (var project in cloudProjects.Value ?? [])
                    await _localDb.SaveProjectAsync(project);
                _logger.LogInformation("Pulled {Count} projects from Cloud", cloudProjects.Value?.Count ?? 0);
            }

            // 2. Pull Events from MongoDB → LiteDB
            var cloudEvents = await _mongoDb.GetEventsAsync();
            if (cloudEvents.IsSuccess)
            {
                foreach (var evt in cloudEvents.Value ?? [])
                    await _localDb.SaveEventAsync(evt);
            }

            // ── PUSH: Local → Cloud ──────────────────────────────────────────────
            // Admin creates Events and Projects on the Server (saved to LiteDB).
            // CloudSyncService must publish them to MongoDB so Client and Web can see them.
            // MongoDB uses IsUpsert=true, so this is idempotent.

            // 3. Push local Events → MongoDB
            var localEvents = await _localDb.GetEventsAsync();
            if (localEvents.IsSuccess)
            {
                foreach (var evt in localEvents.Value ?? [])
                    await _mongoDb.SaveEventAsync(evt);
                _logger.LogInformation("Pushed {Count} local events to Cloud", localEvents.Value?.Count ?? 0);
            }

            // 4. Push local Projects → MongoDB
            var localProjects = await _localDb.GetAllProjectsAsync();
            if (localProjects.IsSuccess)
            {
                foreach (var project in localProjects.Value ?? [])
                    await _mongoDb.SaveProjectAsync(project);
                _logger.LogInformation("Pushed {Count} local projects to Cloud", localProjects.Value?.Count ?? 0);
            }

            // 5. Upload Pending/Failed Votes → MongoDB
            // GetPendingVotesAsync returns Status=Pending and Status=SyncError (retry).
            _logger.LogInformation("Uploading unsynced votes to Cloud...");
            var pendingVotes = await _localDb.GetPendingVotesAsync();
            if (pendingVotes.IsSuccess && (pendingVotes.Value?.Count ?? 0) > 0)
            {
                int synced = 0, failed = 0;
                foreach (var vote in pendingVotes.Value ?? [])
                {
                    var uploadResult = await _mongoDb.SaveVoteAsync(vote);
                    if (uploadResult.IsSuccess)
                    {
                        vote.Status = SyncStatus.Synced;
                        await _localDb.SaveVoteAsync(vote);
                        synced++;
                    }
                    else
                    {
                        vote.Status = SyncStatus.SyncError;
                        await _localDb.SaveVoteAsync(vote);
                        failed++;
                        _logger.LogWarning("Failed to sync vote {VoteId}: {Error}", vote.Id, uploadResult.Error);
                    }
                }
                _logger.LogInformation("Vote upload: {Synced} synced, {Failed} failed", synced, failed);
            }

            _logger.LogInformation("Cloud <-> Local Sync completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud <-> Local Sync failed");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public void Stop()
    {
        _syncTimer?.Stop();
        _syncTimer?.Dispose();
        _syncTimer = null;
    }
}
