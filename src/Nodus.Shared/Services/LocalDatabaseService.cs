using LiteDB;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Nodus.Shared.Models;

namespace Nodus.Shared.Services;

public class LocalDatabaseService : IDatabaseService, IDisposable
{
    private readonly LiteDatabase _db;

    public LocalDatabaseService(IFileService fileService)
    {
        var dir = fileService.GetAppDataDirectory();
        fileService.CreateDirectory(dir);
        
        var dbPath = Path.Combine(dir, "nodus_local.db");
        _db = new LiteDatabase(dbPath);
        
        // Ensure indices
        _db.GetCollection<Event>("events").EnsureIndex(x => x.IsActive);
        _db.GetCollection<Vote>("votes").EnsureIndex(x => x.ProjectId);
        _db.GetCollection<Vote>("votes").EnsureIndex(x => x.Status);
    }

    // ── Events ───────────────────────────────────────────────────────────────

    public Task<Result<List<Event>>> GetEventsAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var list = _db.GetCollection<Event>("events").FindAll().ToList();
                return Result<List<Event>>.Success(list);
            }
            catch (Exception ex)
            {
                return Result<List<Event>>.Failure(ex.Message, ex);
            }
        }, ct);
    }

    public Task<Result<Event>> GetEventAsync(string id, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var evt = _db.GetCollection<Event>("events").FindById(id);
                if (evt == null) return Result<Event>.Failure($"Event {id} not found");
                return Result<Event>.Success(evt);
            }
            catch (Exception ex)
            {
                return Result<Event>.Failure(ex.Message, ex);
            }
        }, ct);
    }

    public Task<Result> SaveEventAsync(Event evt, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                _db.GetCollection<Event>("events").Upsert(evt);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex.Message, ex);
            }
        }, ct);
    }

    // ── Projects ─────────────────────────────────────────────────────────────

    public Task<Result<List<Project>>> GetProjectsAsync(string eventId, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var list = _db.GetCollection<Project>("projects")
                    .Find(x => x.EventId == eventId)
                    .ToList();
                return Result<List<Project>>.Success(list);
            }
            catch (Exception ex)
            {
                return Result<List<Project>>.Failure(ex.Message, ex);
            }
        }, ct);
    }

    public Task<Result<List<Project>>> GetAllProjectsAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var list = _db.GetCollection<Project>("projects").FindAll().ToList();
                return Result<List<Project>>.Success(list);
            }
            catch (Exception ex)
            {
                return Result<List<Project>>.Failure(ex.Message, ex);
            }
        }, ct);
    }

    public Task<Result<Project>> GetProjectAsync(string id, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var proj = _db.GetCollection<Project>("projects").FindById(id);
                if (proj == null) return Result<Project>.Failure($"Project {id} not found");
                return Result<Project>.Success(proj);
            }
            catch (Exception ex)
            {
                return Result<Project>.Failure(ex.Message, ex);
            }
        }, ct);
    }

    public Task<Result> SaveProjectAsync(Project project, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                _db.GetCollection<Project>("projects").Upsert(project);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex.Message, ex);
            }
        }, ct);
    }

    // ── Votes ────────────────────────────────────────────────────────────────

    public Task<Result<List<Vote>>> GetVotesAsync(string projectId, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var list = _db.GetCollection<Vote>("votes")
                    .Find(x => x.ProjectId == projectId)
                    .ToList();
                return Result<List<Vote>>.Success(list);
            }
            catch (Exception ex)
            {
                return Result<List<Vote>>.Failure(ex.Message, ex);
            }
        }, ct);
    }

    public Task<Result<Vote>> GetVoteByIdAsync(string id, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var vote = _db.GetCollection<Vote>("votes").FindById(id);
                if (vote == null) return Result<Vote>.Failure($"Vote {id} not found");
                return Result<Vote>.Success(vote);
            }
            catch (Exception ex)
            {
                return Result<Vote>.Failure(ex.Message, ex);
            }
        }, ct);
    }

    public Task<Result> SaveVoteAsync(Vote vote, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(vote.Id)) vote.Id = Guid.NewGuid().ToString();
                _db.GetCollection<Vote>("votes").Upsert(vote);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex.Message, ex);
            }
        }, ct);
    }

    public Task<Result<List<Vote>>> GetPendingVotesAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                // Assuming SyncStatus is an enum, LiteDB stores it as int by default or string if configured.
                // We'll rely on default behavior (usually int for enums unless BsonMapper configured otherwise).
                // However, our models use string status in MongoDB, but enum in C# POCO.
                // LiteDB by default maps enum to Int.
                // If Vote.Status is SyncStatus (enum), equality check works.
                var list = _db.GetCollection<Vote>("votes")
                    .Find(x => x.Status == SyncStatus.Pending)
                    .ToList();
                return Result<List<Vote>>.Success(list);
            }
            catch (Exception ex)
            {
                return Result<List<Vote>>.Failure(ex.Message, ex);
            }
        }, ct);
    }

    public Task<Result<List<Vote>>> GetVotesWithPendingMediaAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var list = _db.GetCollection<Vote>("votes")
                    .Find(x => !x.IsMediaSynced && x.LocalPhotoPath != null)
                    .ToList();
                return Result<List<Vote>>.Success(list);
            }
            catch (Exception ex)
            {
                return Result<List<Vote>>.Failure(ex.Message, ex);
            }
        }, ct);
    }

    // ── Judges ───────────────────────────────────────────────────────────────

    public Task<Result<List<Judge>>> GetJudgesAsync(string eventId, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                // LiteDB doesn't query inside lists easily with simple LINQ (EventIds is List<string>).
                // We use Query.Contains logic.
                var col = _db.GetCollection<Judge>("judges");
                // Find all where EventIds contains eventId
                var list = col.Find(j => j.EventId == eventId).ToList();
                return Result<List<Judge>>.Success(list);
            }
            catch (Exception ex)
            {
                return Result<List<Judge>>.Failure(ex.Message, ex);
            }
        }, ct);
    }

    public Task<Result<Judge>> GetJudgeAsync(string id, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var judge = _db.GetCollection<Judge>("judges").FindById(id);
                if (judge == null) return Result<Judge>.Failure($"Judge {id} not found");
                return Result<Judge>.Success(judge);
            }
            catch (Exception ex)
            {
                return Result<Judge>.Failure(ex.Message, ex);
            }
        }, ct);
    }

    public Task<Result> SaveJudgeAsync(Judge judge, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                _db.GetCollection<Judge>("judges").Upsert(judge);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex.Message, ex);
            }
        }, ct);
    }

    // ── Other ────────────────────────────────────────────────────────────────

    public Task<Result> ExecuteInTransactionAsync(Action<SQLite.SQLiteConnection> action, CancellationToken ct = default)
    {
        return Task.FromResult(Result.Failure("Transactions with SQLite connection not supported in LiteDB implementation."));
    }

    public void Dispose()
    {
        _db?.Dispose();
    }
}
