using Nodus.Shared.Common;
using Nodus.Shared.Models;

namespace Nodus.Shared.Abstractions;

/// <summary>
/// Abstraction for database operations with explicit error handling.
/// </summary>
public interface IDatabaseService
{
    // Events
    Task<Result<List<Event>>> GetEventsAsync(CancellationToken ct = default);
    Task<Result<Event>> GetEventAsync(string id, CancellationToken ct = default);
    Task<Result> SaveEventAsync(Event evt, CancellationToken ct = default);

    // Projects
    Task<Result<List<Project>>> GetProjectsAsync(string eventId, CancellationToken ct = default);
    Task<Result<List<Project>>> GetAllProjectsAsync(CancellationToken ct = default);
    Task<Result<Project>> GetProjectAsync(string id, CancellationToken ct = default);
    Task<Result> SaveProjectAsync(Project project, CancellationToken ct = default);

    // Votes
    Task<Result<List<Vote>>> GetVotesAsync(string projectId, CancellationToken ct = default);
    Task<Result<List<Vote>>> GetAllVotesAsync(CancellationToken ct = default);
    Task<Result<Vote>> GetVoteByIdAsync(string id, CancellationToken ct = default);
    Task<Result> SaveVoteAsync(Vote vote, CancellationToken ct = default);
    Task<Result<List<Vote>>> GetPendingVotesAsync(CancellationToken ct = default);
    Task<Result<List<Vote>>> GetVotesWithPendingMediaAsync(CancellationToken ct = default);
    Task<Result<SyncStats>> GetSyncStatsAsync(CancellationToken ct = default);

    // Judges
    Task<Result<List<Judge>>> GetJudgesAsync(string eventId, CancellationToken ct = default);
    Task<Result<Judge>> GetJudgeAsync(string id, CancellationToken ct = default);
    Task<Result> SaveJudgeAsync(Judge judge, CancellationToken ct = default);

    /// <summary>
    /// Executes multiple operations in a single transaction.
    /// The action receives a transaction context (if supported by implementation).
    /// </summary>
    Task<Result> ExecuteInTransactionAsync(Func<Task> action, CancellationToken ct = default);
}
