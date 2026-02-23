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
        try 
        {
            _logger.LogInformation("Starting Cloud -> Local Sync...");
            
            // 1. Sync Projects
            var cloudProjects = await _mongoDb.GetAllProjectsAsync();
            if (cloudProjects.IsSuccess)
            {
                foreach (var project in cloudProjects.Value)
                {
                    await _localDb.SaveProjectAsync(project);
                }
                _logger.LogInformation("Synced {Count} projects from Cloud", cloudProjects.Value.Count);
            }

            // 2. Sync Events
            var cloudEvents = await _mongoDb.GetEventsAsync();
            if (cloudEvents.IsSuccess)
            {
                foreach (var evt in cloudEvents.Value)
                {
                    await _localDb.SaveEventAsync(evt);
                }
            }

            _logger.LogInformation("Cloud -> Local Sync completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud -> Local Sync failed");
        }
    }

    public void Stop()
    {
        _syncTimer?.Stop();
        _syncTimer?.Dispose();
        _syncTimer = null;
    }
}
