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

    // Events
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

    public async Task<Result> SaveEventAsync(Event evt, CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) return initResult;

        try
        {
            await _db.RunInTransactionAsync(tran =>
            {
                _db.InsertOrReplaceAsync(evt).Wait();
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

    public async Task<Result> SaveProjectAsync(Project project, CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) return initResult;

        try
        {
            await _db.RunInTransactionAsync(tran =>
            {
                _db.InsertOrReplaceAsync(project).Wait();
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

    // Votes (Critical Transaction Integrity)
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

    public async Task<Result> SaveVoteAsync(Vote vote, CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) return initResult;

        try
        {
            // CRITICAL: Atomic write per spec (03.Data.Offline_First.md)
            await _db.RunInTransactionAsync(tran =>
            {
                _db.InsertOrReplaceAsync(vote).Wait();
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

    public async Task<Result> ExecuteInTransactionAsync(Func<Task> action, CancellationToken ct = default)
    {
        var initResult = await EnsureInitializedAsync(ct);
        if (initResult.IsFailure) return initResult;

        try
        {
            await _db.RunInTransactionAsync(tran =>
            {
                action().Wait();
            });
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction failed");
            return Result.Failure("Transaction execution failed", ex);
        }
    }
}
