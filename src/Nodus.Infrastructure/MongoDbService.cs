using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using Nodus.Shared.Common;
using Nodus.Shared.Models;
using Nodus.Infrastructure.Models;
using Nodus.Shared.Abstractions;

namespace Nodus.Infrastructure.Services;

/// <summary>
/// Implementación de IDatabaseService usando MongoDB.
/// Reemplaza a DatabaseService (SQLite) sin cambiar el contrato de la interfaz.
/// 
/// CONFIGURACIÓN:
///   - connectionString: "mongodb://localhost:27017" (local) o Atlas URI (producción)
///   - databaseName:     "nodus_db"
/// 
/// COLECCIONES:
///   - events    → EventDocument
///   - projects  → ProjectDocument
///   - votes     → VoteDocument
///   - judges    → Judge  (NUEVA — no existe en SQLite)
/// </summary>
public class MongoDbService : IDatabaseService
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<EventDocument> _events;
    private readonly IMongoCollection<ProjectDocument> _projects;
    private readonly IMongoCollection<VoteDocument> _votes;
    private readonly IMongoCollection<Judge> _judges;
    private readonly ILogger<MongoDbService> _logger;

    public MongoDbService(string connectionString, string databaseName, ILogger<MongoDbService> logger)
    {
        _logger = logger;

        // Use explicit timeouts so that connection failures surface quickly instead
        // of hanging for the 30-second MongoDB driver default.
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(8);
        settings.ConnectTimeout = TimeSpan.FromSeconds(8);
        settings.SocketTimeout = TimeSpan.FromSeconds(15);

        // Apply TLS workarounds only for remote URIs (Atlas / SRV).
        // Local mongodb:// connections don't use TLS and should skip this block.
        bool isRemote = connectionString.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase)
                     || (connectionString.Contains('@') && !connectionString.Contains("localhost") && !connectionString.Contains("127.0.0.1"));
        if (isRemote)
        {
            // Windows Schannel (0x80090304 / SEC_E_NO_CREDENTIALS): the OS cannot
            // reach the root CA for OCSP/CRL validation (firewall, outdated cert
            // store, corporate proxy, etc.).
            // • AllowInsecureTls disables hostname verification at the driver level.
            // • ServerCertificateValidationCallback bypasses cert-chain validation
            //   once the TLS handshake completes.
            // • Using only TLS 1.2 avoids Schannel TLS 1.3 credential issues on
            //   some Windows builds.
            // • SendTlsResumeTicket=false prevents session-resumption race conditions.
            // ⚠️  Remove these overrides (or gate on IsDevelopment) before shipping.
            AppContext.SetSwitch("System.Net.Security.SendTlsResumeTicket", false);
            settings.AllowInsecureTls = true;
            settings.SslSettings = new MongoDB.Driver.SslSettings
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12,
                ServerCertificateValidationCallback = (_, _, _, _) => true
            };
        }

        var client = new MongoClient(settings);
        _database = client.GetDatabase(databaseName);

        _events = _database.GetCollection<EventDocument>("events");
        _projects = _database.GetCollection<ProjectDocument>("projects");
        _votes = _database.GetCollection<VoteDocument>("votes");
        _judges = _database.GetCollection<Judge>("judges");

        // Crear índices al inicializar
        _ = CreateIndexesAsync();

        _logger.LogInformation("MongoDbService initialized. Database: {DbName}", databaseName);
    }

    // ─────────────────────────────────────────
    // ÍNDICES
    // ─────────────────────────────────────────

    private async Task CreateIndexesAsync()
    {
        try
        {
            // projects: índice por eventId
            await _projects.Indexes.CreateOneAsync(
                new CreateIndexModel<ProjectDocument>(
                    Builders<ProjectDocument>.IndexKeys.Ascending(p => p.EventId)));

            // votes: índices por eventId, projectId, judgeId, status
            await _votes.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<VoteDocument>(
                    Builders<VoteDocument>.IndexKeys.Ascending(v => v.EventId)),
                new CreateIndexModel<VoteDocument>(
                    Builders<VoteDocument>.IndexKeys.Ascending(v => v.ProjectId)),
                new CreateIndexModel<VoteDocument>(
                    Builders<VoteDocument>.IndexKeys.Ascending(v => v.JudgeId)),
                new CreateIndexModel<VoteDocument>(
                    Builders<VoteDocument>.IndexKeys.Ascending(v => v.Status)),
                // Índice compuesto para media sync (reemplaza índice parcial — compatible v3.x)
                new CreateIndexModel<VoteDocument>(
                    Builders<VoteDocument>.IndexKeys
                        .Ascending(v => v.LocalPhotoPath)
                        .Ascending(v => v.IsMediaSynced)),
                // Índice para evitar duplicidad real del mismo juez votando por el mismo proyecto
                new CreateIndexModel<VoteDocument>(
                    Builders<VoteDocument>.IndexKeys
                        .Ascending(v => v.ProjectId)
                        .Ascending(v => v.JudgeId),
                    new CreateIndexOptions { Unique = true, Name = "uq_vote_project_judge" })
            });

            // judges: índice por eventId
            await _judges.Indexes.CreateOneAsync(
                new CreateIndexModel<Judge>(
                    Builders<Judge>.IndexKeys.Ascending(j => j.EventId)));

            _logger.LogInformation("MongoDB indexes created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create indexes (non-fatal)");
        }
    }

    // ─────────────────────────────────────────
    // EVENTS
    // ─────────────────────────────────────────

    public async Task<Result<List<Event>>> GetEventsAsync(CancellationToken ct = default)
    {
        try
        {
            var docs = await _events.Find(_ => true).ToListAsync(ct);
            return Result<List<Event>>.Success(docs.Select(ToEvent).ToList());
        }
        catch (Exception ex)
        {
            if (IsDatabaseUnavailable(ex))
            {
                _logger.LogWarning(ex, "Database unavailable while retrieving events. Returning empty list.");
                return Result<List<Event>>.Success([]);
            }

            _logger.LogError(ex, "Failed to retrieve events");
            return Result<List<Event>>.Failure("Failed to retrieve events", ex);
        }
    }

    public async Task<Result<Event>> GetEventAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var doc = await _events.Find(e => e.Id == id).FirstOrDefaultAsync(ct);
            return doc != null
                ? Result<Event>.Success(ToEvent(doc))
                : Result<Event>.Failure($"Event '{id}' not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve event {EventId}", id);
            return Result<Event>.Failure($"Failed to retrieve event {id}", ex);
        }
    }

    public async Task<Result> SaveEventAsync(Event evt, CancellationToken ct = default)
    {
        if (evt == null) return Result.Failure("Event cannot be null");
        try
        {
            var doc = ToEventDoc(evt);
            await _events.ReplaceOneAsync(
                filter: e => e.Id == evt.Id,
                replacement: doc,
                options: new ReplaceOptions { IsUpsert = true },
                cancellationToken: ct);
            _logger.LogInformation("Event {EventId} saved", evt.Id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save event {EventId}", evt.Id);
            return Result.Failure($"Failed to save event {evt.Id}", ex);
        }
    }

    // ─────────────────────────────────────────
    // PROJECTS
    // ─────────────────────────────────────────

    public async Task<Result<List<Project>>> GetProjectsAsync(string eventId, CancellationToken ct = default)
    {
        try
        {
            var docs = await _projects.Find(p => p.EventId == eventId).ToListAsync(ct);
            return Result<List<Project>>.Success(docs.Select(ToProject).ToList());
        }
        catch (Exception ex)
        {
            if (IsDatabaseUnavailable(ex))
            {
                _logger.LogWarning(ex, "Database unavailable while retrieving projects for event {EventId}. Returning empty list.", eventId);
                return Result<List<Project>>.Success([]);
            }

            _logger.LogError(ex, "Failed to retrieve projects for event {EventId}", eventId);
            return Result<List<Project>>.Failure($"Failed to retrieve projects for event {eventId}", ex);
        }
    }

    public async Task<Result<List<Project>>> GetAllProjectsAsync(CancellationToken ct = default)
    {
        try
        {
            var docs = await _projects.Find(_ => true).ToListAsync(ct);
            return Result<List<Project>>.Success(docs.Select(ToProject).ToList());
        }
        catch (Exception ex)
        {
            if (IsDatabaseUnavailable(ex))
            {
                _logger.LogWarning(ex, "Database unavailable while retrieving all projects. Returning empty list.");
                return Result<List<Project>>.Success([]);
            }

            _logger.LogError(ex, "Failed to retrieve all projects");
            return Result<List<Project>>.Failure("Failed to retrieve all projects", ex);
        }
    }

    private static bool IsDatabaseUnavailable(Exception ex)
    {
        var current = ex;
        while (current != null)
        {
            if (current is TimeoutException or MongoConnectionException or SocketException)
            {
                return true;
            }

            current = current.InnerException!;
        }

        return false;
    }

    public async Task<Result<Project>> GetProjectAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var doc = await _projects.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
            return doc != null
                ? Result<Project>.Success(ToProject(doc))
                : Result<Project>.Failure($"Project '{id}' not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve project {ProjectId}", id);
            return Result<Project>.Failure($"Failed to retrieve project {id}", ex);
        }
    }

    public async Task<Result> SaveProjectAsync(Project project, CancellationToken ct = default)
    {
        try
        {
            var doc = ToProjectDoc(project);
            await _projects.ReplaceOneAsync(
                filter: p => p.Id == project.Id,
                replacement: doc,
                options: new ReplaceOptions { IsUpsert = true },
                cancellationToken: ct);
            _logger.LogInformation("Project {ProjectId} saved", project.Id);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save project {ProjectId}", project.Id);
            return Result.Failure($"Failed to save project {project.Id}", ex);
        }
    }

    // ─────────────────────────────────────────
    // VOTES
    // ─────────────────────────────────────────

    public async Task<Result<List<Vote>>> GetVotesAsync(string projectId, CancellationToken ct = default)
    {
        try
        {
            var docs = await _votes.Find(v => v.ProjectId == projectId).ToListAsync(ct);
            return Result<List<Vote>>.Success(docs.Select(ToVote).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve votes for project {ProjectId}", projectId);
            return Result<List<Vote>>.Failure($"Failed to retrieve votes for project {projectId}", ex);
        }
    }

    public async Task<Result<List<Vote>>> GetAllVotesAsync(CancellationToken ct = default)
    {
        try
        {
            var docs = await _votes.Find(_ => true).ToListAsync(ct);
            return Result<List<Vote>>.Success(docs.Select(ToVote).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve all votes");
            return Result<List<Vote>>.Failure("Failed to retrieve all votes", ex);
        }
    }

    public async Task<Result<Vote>> GetVoteByIdAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var doc = await _votes.Find(v => v.Id == id).FirstOrDefaultAsync(ct);
            return doc != null
                ? Result<Vote>.Success(ToVote(doc))
                : Result<Vote>.Failure($"Vote '{id}' not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve vote {VoteId}", id);
            return Result<Vote>.Failure($"Failed to retrieve vote {id}", ex);
        }
    }

    public async Task<Result> SaveVoteAsync(Vote vote, CancellationToken ct = default)
    {
        if (vote == null) return Result.Failure("Vote cannot be null");
        if (string.IsNullOrWhiteSpace(vote.EventId)) return Result.Failure("EventId cannot be empty");
        if (string.IsNullOrWhiteSpace(vote.ProjectId)) return Result.Failure("ProjectId cannot be empty");
        if (string.IsNullOrWhiteSpace(vote.JudgeId)) return Result.Failure("JudgeId cannot be empty");

        try
        {
            var doc = ToVoteDoc(vote);
            await _votes.ReplaceOneAsync(
                filter: v => v.Id == vote.Id,
                replacement: doc,
                options: new ReplaceOptions { IsUpsert = true },
                cancellationToken: ct);
            _logger.LogInformation("Vote {VoteId} saved (Status: {Status})", vote.Id, vote.Status);
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
        try
        {
            // Returns both Pending and SyncError votes so that failed votes are
            // automatically retried on the next sync cycle instead of being orphaned.
            var pendingStr = SyncStatus.Pending.ToString();
            var errorStr = SyncStatus.SyncError.ToString();
            var filter = Builders<VoteDocument>.Filter.In(v => v.Status, new[] { pendingStr, errorStr });
            var docs = await _votes.Find(filter).ToListAsync(ct);
            return Result<List<Vote>>.Success(docs.Select(ToVote).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve pending votes");
            return Result<List<Vote>>.Failure("Failed to retrieve pending votes", ex);
        }
    }

    public async Task<Result<List<Vote>>> GetVotesWithPendingMediaAsync(CancellationToken ct = default)
    {
        try
        {
            var filter = Builders<VoteDocument>.Filter.And(
                Builders<VoteDocument>.Filter.Exists(v => v.LocalPhotoPath),
                Builders<VoteDocument>.Filter.Eq(v => v.IsMediaSynced, false));

            var docs = await _votes.Find(filter).ToListAsync(ct);
            return Result<List<Vote>>.Success(docs.Select(ToVote).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve votes with pending media");
            return Result<List<Vote>>.Failure("Failed to retrieve votes with pending media", ex);
        }
    }

    public async Task<Result<SyncStats>> GetSyncStatsAsync(CancellationToken ct = default)
    {
        try
        {
            var votes = await _votes.Find(_ => true).ToListAsync(ct);
            var pendingVotes = votes.Count(v => v.Status == SyncStatus.Pending.ToString());
            var pendingMedia = votes.Count(v => !v.IsMediaSynced && !string.IsNullOrEmpty(v.LocalPhotoPath));
            var syncedVotes = votes.Count(v => v.Status == SyncStatus.Synced.ToString());

            return Result<SyncStats>.Success(new SyncStats
            {
                PendingVotes = pendingVotes,
                PendingMedia = pendingMedia,
                SyncedVotes = syncedVotes,
                TotalVotes = votes.Count
            });
        }
        catch (Exception ex)
        {
            return Result<SyncStats>.Failure(ex.Message, ex);
        }
    }

    /// <summary>
    /// MongoDB no usa transacciones de la misma manera que SQLite.
    /// Se requiere Replica Set para sesiones transaccionales.
    /// Para operaciones simples se usa upsert atómico directamente.
    /// </summary>
    public async Task<Result> ExecuteInTransactionAsync(Func<Task> action, CancellationToken ct = default)
    {
        // NOTA: Este método existe en la interfaz para compatibilidad genérica.
        // En MongoDB real (con Replica Set), usa IClientSessionHandle + WithTransaction.
        // Aquí ejecutamos la acción directamente de forma best-effort.
        try
        {
            await action();
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute transaction action");
            return Result.Failure("Transaction action failed", ex);
        }
    }

    // ─────────────────────────────────────────
    // JUDGES (NUEVO)
    // ─────────────────────────────────────────

    public async Task<Result<List<Judge>>> GetJudgesAsync(string eventId, CancellationToken ct = default)
    {
        try
        {
            var judges = await _judges.Find(j => j.EventId == eventId).ToListAsync(ct);
            return Result<List<Judge>>.Success(judges);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve judges for event {EventId}", eventId);
            return Result<List<Judge>>.Failure($"Failed to retrieve judges for event {eventId}", ex);
        }
    }

    public async Task<Result<Judge>> GetJudgeAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var judge = await _judges.Find(j => j.Id == id).FirstOrDefaultAsync(ct);
            return judge != null
                ? Result<Judge>.Success(judge)
                : Result<Judge>.Failure($"Judge '{id}' not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve judge {JudgeId}", id);
            return Result<Judge>.Failure($"Failed to retrieve judge {id}", ex);
        }
    }

    public async Task<Result> SaveJudgeAsync(Judge judge, CancellationToken ct = default)
    {
        if (judge == null) return Result.Failure("Judge cannot be null");
        if (string.IsNullOrWhiteSpace(judge.Name)) return Result.Failure("Judge name cannot be empty");
        try
        {
            await _judges.ReplaceOneAsync(
                filter: j => j.Id == judge.Id,
                replacement: judge,
                options: new ReplaceOptions { IsUpsert = true },
                cancellationToken: ct);
            _logger.LogInformation("Judge {JudgeId} ({Name}) saved for Event {EventId}",
                judge.Id, judge.Name, judge.EventId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save judge {JudgeId}", judge.Id);
            return Result.Failure($"Failed to save judge {judge.Id}", ex);
        }
    }

    // ─────────────────────────────────────────
    // MAPPERS SQLite ↔ MongoDB
    // ─────────────────────────────────────────

    private static Event ToEvent(EventDocument d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        RubricJson = d.Rubric ?? "{}",
        GlobalSalt = d.GlobalSalt,
        SharedAesKeyEncrypted = d.SharedAesKeyEncrypted,
        IsActive = d.IsActive
    };

    private static EventDocument ToEventDoc(Event e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Rubric = string.IsNullOrEmpty(e.RubricJson) ? "{}" : e.RubricJson,
        GlobalSalt = e.GlobalSalt,
        SharedAesKeyEncrypted = e.SharedAesKeyEncrypted,
        IsActive = e.IsActive
    };

    private static Project ToProject(ProjectDocument d) => new()
    {
        Id = d.Id,
        EventId = d.EventId,
        Name = d.Name,
        Category = d.Category,
        Description = d.Description,
        Authors = d.Authors,
        GithubUrl = d.GithubUrl,
        CreatedAtUtc = d.CreatedAtUtc,
        UpdatedAtUtc = d.UpdatedAtUtc
    };

    private static ProjectDocument ToProjectDoc(Project p) => new()
    {
        Id = p.Id,
        EventId = p.EventId,
        Name = p.Name,
        Category = p.Category,
        Description = p.Description,
        Authors = p.Authors,
        GithubUrl = p.GithubUrl,
        CreatedAtUtc = p.CreatedAtUtc,
        UpdatedAtUtc = p.UpdatedAtUtc
    };

    private static Vote ToVote(VoteDocument d) => new()
    {
        Id = d.Id,
        EventId = d.EventId,
        ProjectId = d.ProjectId,
        JudgeId = d.JudgeId,
        PayloadJson = d.Payload,
        Status = Enum.TryParse<SyncStatus>(d.Status, out var s) ? s : SyncStatus.Pending,
        Timestamp = d.Timestamp,
        LocalPhotoPath = d.LocalPhotoPath,
        IsMediaSynced = d.IsMediaSynced,
        SyncedAtUtc = d.SyncedAtUtc
    };

    private static VoteDocument ToVoteDoc(Vote v) => new()
    {
        Id = v.Id,
        EventId = v.EventId,
        ProjectId = v.ProjectId,
        JudgeId = v.JudgeId,
        Payload = string.IsNullOrEmpty(v.PayloadJson) ? "{}" : v.PayloadJson,
        Status = v.Status.ToString(),
        Timestamp = v.Timestamp,
        LocalPhotoPath = v.LocalPhotoPath,
        IsMediaSynced = v.IsMediaSynced,
        SyncedAtUtc = v.SyncedAtUtc
    };
}
