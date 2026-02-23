using SQLite;
using Nodus.Shared.Models;
using Nodus.Shared.Common;
using Nodus.Shared.Abstractions;
using Microsoft.Extensions.Logging;

namespace Nodus.Shared.Services;

/// <summary>
/// Professional database service with transaction support and proper error handling.
/// NOTE: sqlite-net-pcl does not support CancellationToken in async methods.
/// </summary>
public class DatabaseService : IDatabaseService
{
    private readonly SQLiteAsyncConnection _db;
    private readonly ILogger<DatabaseService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public DatabaseService(string dbPath, ILogger<DatabaseService> logger)
    {
        _db = new SQLiteAsyncConnection(dbPath, 
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        _logger = logger;
    }

    private async Task<Result> EnsureInitializedAsync(CancellationToken ct)
    {
        if (_isInitialized) return Result.Success();

        await _initLock.WaitAsync(ct);
        try
        {
            if (_isInitialized) return Result.Success();

            _logger.LogInformation("Initializing database schema...");
            
            await _db.CreateTableAsync<Event>();
            await _db.CreateTableAsync<Project>();
            await _db.CreateTableAsync<Vote>();
            
            // Performance Indices
            await CreateIndicesAsync();
            
            _isInitialized = true;
            _logger.LogInformation("Database schema initialized successfully");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            return Result.Failure("Database initialization failed", ex);
        }
        finally
        {
            _initLock.Release();
        }
    }
    
    private async Task CreateIndicesAsync()
    {
        try
        {
            // Index for pending votes query
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS idx_votes_status ON Vote(Status)");
            
            // Index for pending media query
            await _db.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS idx_votes_media_pending ON Vote(LocalPhotoPath, IsMediaSynced) WHERE LocalPhotoPath IS NOT NULL AND IsMediaSynced = 0");
            
            _logger.LogInformation("Database indices created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create indices (non-fatal)");
        }
    }

    // Events
    
    /// <inheritdoc/>
    public async Task<Result<List<Event>>> GetEventsAsync(CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) 
            return Result<List<Event>>.Failure(initResult.Error, initResult.Exception);

        try
        {
            var events = await _db.Table<Event>().ToListAsync();
            return Result<List<Event>>.Success(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve events");
            return Result<List<Event>>.Failure("Failed to retrieve events", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<Event>> GetEventAsync(string id, CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) 
            return Result<Event>.Failure(initResult.Error, initResult.Exception);

        try
        {
            var evt = await _db.Table<Event>().Where(e => e.Id == id).FirstOrDefaultAsync();
            return evt != null 
                ? Result<Event>.Success(evt)
                : Result<Event>.Failure($"Event with ID '{id}' not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve event {EventId}", id);
            return Result<Event>.Failure($"Failed to retrieve event {id}", ex);
        }
    }

    /// <summary>
    /// Saves an event to the local database.
    /// </summary>
    public async Task<Result> SaveEventAsync(Event evt, CancellationToken ct = default)
    {
        if (evt == null) return Result.Failure("Event cannot be null");
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) return initResult;

        try
        {
            await _db.RunInTransactionAsync(tran =>
            {
                tran.InsertOrReplace(evt);
            });
            
            _logger.LogInformation("Event {EventId} saved successfully", evt.Id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save event {EventId}", evt.Id);
            return Result.Failure($"Failed to save event {evt.Id}", ex);
        }
    }

    // Projects
    
    /// <inheritdoc/>
    public async Task<Result<List<Project>>> GetProjectsAsync(string eventId, CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) 
            return Result<List<Project>>.Failure(initResult.Error, initResult.Exception);

        try
        {
            var projects = await _db.Table<Project>()
                .Where(p => p.EventId == eventId)
                .ToListAsync();
            return Result<List<Project>>.Success(projects);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve projects for event {EventId}", eventId);
            return Result<List<Project>>.Failure($"Failed to retrieve projects for event {eventId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<Project>>> GetAllProjectsAsync(CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) 
            return Result<List<Project>>.Failure(initResult.Error, initResult.Exception);

        try
        {
            var projects = await _db.Table<Project>().ToListAsync();
            return Result<List<Project>>.Success(projects);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all projects");
            return Result<List<Project>>.Failure("Failed to retrieve all projects", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<Project>> GetProjectAsync(string id, CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) 
            return Result<Project>.Failure(initResult.Error, initResult.Exception);

        try
        {
            var project = await _db.Table<Project>().Where(p => p.Id == id).FirstOrDefaultAsync();
            return project != null 
                ? Result<Project>.Success(project)
                : Result<Project>.Failure($"Project with ID '{id}' not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve project {ProjectId}", id);
            return Result<Project>.Failure($"Failed to retrieve project {id}", ex);
        }
    }

    /// <summary>
    /// Saves a project to the local database.
    /// </summary>
    /// <param name="project">The project to save.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    public async Task<Result> SaveProjectAsync(Project project, CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) return initResult;

        try
        {
            await _db.RunInTransactionAsync(tran =>
            {
                tran.InsertOrReplace(project);
            });
            
            _logger.LogInformation("Project {ProjectId} saved successfully", project.Id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save project {ProjectId}", project.Id);
            return Result.Failure($"Failed to save project {project.Id}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<Vote>>> GetVotesAsync(string projectId, CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) 
            return Result<List<Vote>>.Failure(initResult.Error, initResult.Exception);

        try
        {
            var votes = await _db.Table<Vote>()
                .Where(v => v.ProjectId == projectId)
                .ToListAsync();
            return Result<List<Vote>>.Success(votes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve votes for project {ProjectId}", projectId);
            return Result<List<Vote>>.Failure($"Failed to retrieve votes for project {projectId}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<Vote>>> GetAllVotesAsync(CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) 
            return Result<List<Vote>>.Failure(initResult.Error, initResult.Exception);

        try
        {
            var votes = await _db.Table<Vote>().ToListAsync();
            return Result<List<Vote>>.Success(votes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all votes");
            return Result<List<Vote>>.Failure("Failed to retrieve all votes", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<Vote>> GetVoteByIdAsync(string id, CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) 
            return Result<Vote>.Failure(initResult.Error, initResult.Exception);

        try
        {
            var vote = await _db.Table<Vote>().Where(v => v.Id == id).FirstOrDefaultAsync();
            return vote != null 
                ? Result<Vote>.Success(vote)
                : Result<Vote>.Failure($"Vote with ID '{id}' not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve vote {VoteId}", id);
            return Result<Vote>.Failure($"Failed to retrieve vote {id}", ex);
        }
    }

    /// <summary>
    /// Saves a vote securely within a transaction.
    /// Ensures atomicity for data integrity as per Offline-First spec.
    /// </summary>
    /// <param name="vote">The vote entity to save or update.</param>
    /// <param name="ct">Cancellation token (note: not fully supported by sqlite-net-pcl transaction).</param>
    /// <returns>Success result or failure with exception.</returns>
    public async Task<Result> SaveVoteAsync(Vote vote, CancellationToken ct = default)
    {
        // Input validation - BUG-001, BUG-002, BUG-003 fixes
        if (vote == null) 
            return Result.Failure("Vote cannot be null");
        
        if (string.IsNullOrWhiteSpace(vote.EventId))
            return Result.Failure("EventId cannot be null or empty");
        
        if (string.IsNullOrWhiteSpace(vote.ProjectId))
            return Result.Failure("ProjectId cannot be null or empty");
        
        if (string.IsNullOrWhiteSpace(vote.JudgeId))
            return Result.Failure("JudgeId cannot be null or empty");
        
        if (string.IsNullOrWhiteSpace(vote.PayloadJson))
            return Result.Failure("PayloadJson cannot be null or empty");
        
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) return initResult;

        try
        {
            // CRITICAL: Atomic write per spec (03.Data.Offline_First.md)
            await _db.RunInTransactionAsync(tran =>
            {
                tran.InsertOrReplace(vote);
            });
            
            _logger.LogInformation("Vote {VoteId} saved successfully (Status: {Status})", 
                vote.Id, vote.Status);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save vote {VoteId}", vote.Id);
            return Result.Failure($"Failed to save vote {vote.Id}", ex);
        }
    }

    public async Task<Result<List<Vote>>> GetPendingVotesAsync(CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) 
            return Result<List<Vote>>.Failure(initResult.Error, initResult.Exception);

        try
        {
            var pending = await _db.Table<Vote>()
                .Where(v => v.Status == SyncStatus.Pending)
                .ToListAsync();
            
            _logger.LogDebug("Retrieved {Count} pending votes", pending.Count);
            return Result<List<Vote>>.Success(pending);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve pending votes");
            return Result<List<Vote>>.Failure("Failed to retrieve pending votes", ex);
        }
    }

    public async Task<Result<List<Vote>>> GetVotesWithPendingMediaAsync(CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) 
            return Result<List<Vote>>.Failure(initResult.Error, initResult.Exception);

        try
        {
            // Pending media: Has local path, but !IsMediaSynced
            var pending = await _db.Table<Vote>()
                .Where(v => v.LocalPhotoPath != null && v.IsMediaSynced == false)
                .ToListAsync();
            
            _logger.LogDebug("Retrieved {Count} votes with pending media", pending.Count);
            return Result<List<Vote>>.Success(pending);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve votes with pending media");
            return Result<List<Vote>>.Failure("Failed to retrieve votes with pending media", ex);
        }
    }
    
    /// <summary>
    /// Get sync statistics for monitoring dashboard
    /// </summary>
    public async Task<Result<SyncStatistics>> GetSyncStatsAsync(CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure)
            return Result<SyncStatistics>.Failure(initResult.Error, initResult.Exception);

        try
        {
            var total = await _db.Table<Vote>().CountAsync();
            var pending = await _db.Table<Vote>()
                .Where(v => v.Status == SyncStatus.Pending)
                .CountAsync();
            var pendingMedia = await _db.Table<Vote>()
                .Where(v => v.LocalPhotoPath != null && v.IsMediaSynced == false)
                .CountAsync();
            
            var stats = SyncStatistics.Calculate(total, pending, pendingMedia);
            _logger.LogDebug("Sync stats: {Total} total, {Pending} pending, {Media} pending media", 
                total, pending, pendingMedia);
            
            return Result<SyncStatistics>.Success(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve sync statistics");
            return Result<SyncStatistics>.Failure("Failed to retrieve sync statistics", ex);
        }
    }

    /// <summary>
    /// Executes a synchronous action within a transaction.
    /// IMPORTANT: The action must be synchronous to work with sqlite-net-pcl's transaction model.
    /// </summary>
    public async Task<Result> ExecuteInTransactionAsync(Action<SQLiteConnection> action, CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) return initResult;

        try
        {
            await _db.RunInTransactionAsync(tran =>
            {
                action(tran);
            });
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction failed");
            return Result.Failure("Transaction execution failed", ex);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STUBS de Judge — Solo para satisfacer IDatabaseService en compilación.
    // En runtime, el DI inyecta MongoDbService; estos métodos NUNCA se ejecutan.
    // ─────────────────────────────────────────────────────────────────────────

    public Task<Result<List<Judge>>> GetJudgesAsync(string eventId, CancellationToken ct = default)
    {
        _logger.LogError("GetJudgesAsync: use MongoDbService, no DatabaseService (SQLite).");
        return Task.FromResult(Result<List<Judge>>.Failure("Requires MongoDbService."));
    }

    public Task<Result<Judge>> GetJudgeAsync(string id, CancellationToken ct = default)
    {
        _logger.LogError("GetJudgeAsync: use MongoDbService, no DatabaseService (SQLite).");
        return Task.FromResult(Result<Judge>.Failure("Requires MongoDbService."));
    }

    public Task<Result> SaveJudgeAsync(Judge judge, CancellationToken ct = default)
    {
        _logger.LogError("SaveJudgeAsync: use MongoDbService, no DatabaseService (SQLite).");
        return Task.FromResult(Result.Failure("Requires MongoDbService."));
    }
}
