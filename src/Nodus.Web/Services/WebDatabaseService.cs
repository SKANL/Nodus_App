using Blazored.LocalStorage;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Nodus.Shared.Models;
namespace Nodus.Web.Services;

/// <summary>
/// Blazor WASM implementation of IDatabaseService.
/// Strategy: LocalStorage is the primary read cache; Nodus.Api is the sync source.
/// All writes go to LocalStorage immediately (offline-first), then push to the API
/// in a best-effort fire-and-forget task.
/// </summary>
public class WebDatabaseService : IDatabaseService
{
    private readonly ILocalStorageService _localStorage;
    private readonly NodusApiService _apiService;

    // Key used by SettingsService — read directly to avoid a circular DI dependency
    // (WebDatabaseService → SettingsService → EventService → WebDatabaseService)
    private const string CurrentEventIdKey = "CurrentEventId";

    private const string ProjectsKey = "nodus_projects";
    private const string VotesKey = "nodus_votes";
    private const string EventsKey = "nodus_events";
    private const string JudgesKey = "nodus_judges";

    public WebDatabaseService(
        ILocalStorageService localStorage,
        NodusApiService apiService)
    {
        _localStorage = localStorage;
        _apiService = apiService;
    }

    // --- Projects ---

    /// <summary>
    /// Reads projects from localStorage only. Offline-first: never makes a network call.
    /// Use GetProjectsAsync(eventId) to also trigger a cloud sync.
    /// </summary>
    public async Task<Result<List<Project>>> GetAllProjectsAsync(CancellationToken ct = default)
    {
        try
        {
            var local = await _localStorage.GetItemAsync<List<Project>>(ProjectsKey, ct) ?? new List<Project>();
            return Result<List<Project>>.Success(local);
        }
        catch (Exception ex)
        {
            return Result<List<Project>>.Failure(ex.Message, ex);
        }
    }

    public async Task<Result<Project>> GetProjectAsync(string id, CancellationToken ct = default)
    {
        var projects = await GetAllProjectsAsync(ct);
        if (!projects.IsSuccess) return Result<Project>.Failure(projects.Error);

        var project = (projects.Value ?? []).FirstOrDefault(p => p.Id == id);
        return project != null ? Result<Project>.Success(project) : Result<Project>.Failure($"Project {id} not found");
    }

    /// <summary>
    /// Returns projects for an event. Performs a single cloud sync before returning,
    /// then falls back to localStorage on network failure.
    /// </summary>
    public async Task<Result<List<Project>>> GetProjectsAsync(string eventId, CancellationToken ct = default)
    {
        // Single sync point — no duplicate roundtrips
        await SyncProjectsFromApiAsync(eventId, ct);

        var allProjects = await GetAllProjectsAsync(ct);
        if (!allProjects.IsSuccess) return Result<List<Project>>.Failure(allProjects.Error);

        return Result<List<Project>>.Success(
            (allProjects.Value ?? []).Where(p => p.EventId == eventId).ToList());
    }

    /// <summary>
    /// Best-effort cloud sync: fetches projects for an event from the API and merges
    /// into localStorage. Silently falls through on any network error.
    /// </summary>
    private async Task SyncProjectsFromApiAsync(string eventId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return;
        try
        {
            var cloudResult = await _apiService.GetProjectsAsync(eventId, ct);
            if (!cloudResult.IsSuccess || cloudResult.Value == null || cloudResult.Value.Count == 0)
                return;

            var local = await _localStorage.GetItemAsync<List<Project>>(ProjectsKey, ct) ?? new List<Project>();
            foreach (var cp in cloudResult.Value)
            {
                var idx = local.FindIndex(x => x.Id == cp.Id);
                if (idx >= 0) local[idx] = cp;
                else local.Add(cp);
            }
            await _localStorage.SetItemAsync(ProjectsKey, local, ct);
        }
        catch
        {
            // Network failure is expected when offline — localStorage remains the source of truth
        }
    }

    public async Task<Result> SaveProjectAsync(Project project, CancellationToken ct = default)
    {
        try
        {
            var projects = await _localStorage.GetItemAsync<List<Project>>(ProjectsKey, ct) ?? new List<Project>();
            var existing = projects.FirstOrDefault(p => p.Id == project.Id);
            if (existing != null) projects.Remove(existing);
            projects.Add(project);
            await _localStorage.SetItemAsync(ProjectsKey, projects, ct);

            // Sync with Nodus API (Background/Best-effort)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _apiService.SaveProjectAsync(project);
                }
                catch (HttpRequestException httpEx)
                {
                    Console.WriteLine($"Atlas Sync Failed (Network): {httpEx.Message}");
                }
                catch (Exception ex)
                {
                    // Log but don't fail the local save
                    Console.WriteLine($"Atlas Sync Failed (System): {ex.Message}");
                }
            }, ct);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message, ex);
        }
    }

    // --- Votes ---
    public async Task<Result<List<Vote>>> GetAllVotesAsync(CancellationToken ct = default)
    {
        try
        {
            var votes = await _localStorage.GetItemAsync<List<Vote>>(VotesKey, ct) ?? new List<Vote>();
            return Result<List<Vote>>.Success(votes);
        }
        catch (Exception ex)
        {
            return Result<List<Vote>>.Failure(ex.Message, ex);
        }
    }

    public async Task<Result<List<Vote>>> GetVotesAsync(string projectId, CancellationToken ct = default)
    {
        var votes = await GetAllVotesAsync(ct);
        if (!votes.IsSuccess) return Result<List<Vote>>.Failure(votes.Error);
        return Result<List<Vote>>.Success((votes.Value ?? []).Where(v => v.ProjectId == projectId).ToList());
    }

    public async Task<Result<Vote>> GetVoteByIdAsync(string id, CancellationToken ct = default)
    {
        var votes = await GetAllVotesAsync(ct);
        if (!votes.IsSuccess) return Result<Vote>.Failure(votes.Error);
        var vote = (votes.Value ?? []).FirstOrDefault(v => v.Id == id);
        return vote != null ? Result<Vote>.Success(vote) : Result<Vote>.Failure($"Vote {id} not found");
    }

    public async Task<Result> SaveVoteAsync(Vote vote, CancellationToken ct = default)
    {
        try
        {
            var votes = await _localStorage.GetItemAsync<List<Vote>>(VotesKey, ct) ?? new List<Vote>();
            var existing = votes.FirstOrDefault(v => v.Id == vote.Id);
            if (existing != null) votes.Remove(existing);
            votes.Add(vote);
            await _localStorage.SetItemAsync(VotesKey, votes, ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message, ex);
        }
    }

    public async Task<Result<List<Vote>>> GetPendingVotesAsync(CancellationToken ct = default)
    {
        var votes = await GetAllVotesAsync(ct);
        if (!votes.IsSuccess) return Result<List<Vote>>.Failure(votes.Error);
        return Result<List<Vote>>.Success((votes.Value ?? []).Where(v => v.Status == SyncStatus.Pending).ToList());
    }

    public async Task<Result<List<Vote>>> GetVotesWithPendingMediaAsync(CancellationToken ct = default)
    {
        var votes = await GetAllVotesAsync(ct);
        if (!votes.IsSuccess) return Result<List<Vote>>.Failure(votes.Error);
        return Result<List<Vote>>.Success((votes.Value ?? []).Where(v => !v.IsMediaSynced && !string.IsNullOrEmpty(v.LocalPhotoPath)).ToList());
    }

    public async Task<Result<SyncStats>> GetSyncStatsAsync(CancellationToken ct = default)
    {
        var votesResult = await GetAllVotesAsync(ct);
        if (!votesResult.IsSuccess) return Result<SyncStats>.Failure(votesResult.Error);

        var votes = votesResult.Value ?? [];
        return Result<SyncStats>.Success(new SyncStats
        {
            TotalVotes = votes.Count,
            PendingVotes = votes.Count(v => v.Status == SyncStatus.Pending),
            SyncedVotes = votes.Count(v => v.Status == SyncStatus.Synced),
            PendingMedia = votes.Count(v => !v.IsMediaSynced && !string.IsNullOrEmpty(v.LocalPhotoPath))
        });
    }

    // --- Events ---
    public async Task<Result<List<Event>>> GetEventsAsync(CancellationToken ct = default)
    {
        try
        {
            var events = await _localStorage.GetItemAsync<List<Event>>(EventsKey, ct) ?? new List<Event>();
            return Result<List<Event>>.Success(events);
        }
        catch (Exception ex)
        {
            return Result<List<Event>>.Failure(ex.Message, ex);
        }
    }

    public async Task<Result<Event>> GetEventAsync(string id, CancellationToken ct = default)
    {
        var events = await GetEventsAsync(ct);
        if (!events.IsSuccess) return Result<Event>.Failure(events.Error);
        var evt = (events.Value ?? []).FirstOrDefault(e => e.Id == id);
        return evt != null ? Result<Event>.Success(evt) : Result<Event>.Failure($"Event {id} not found");
    }

    public async Task<Result> SaveEventAsync(Event evt, CancellationToken ct = default)
    {
        try
        {
            var events = await _localStorage.GetItemAsync<List<Event>>(EventsKey, ct) ?? new List<Event>();
            var existing = events.FirstOrDefault(e => e.Id == evt.Id);
            if (existing != null) events.Remove(existing);
            events.Add(evt);
            await _localStorage.SetItemAsync(EventsKey, events, ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message, ex);
        }
    }

    // --- Judges ---
    public async Task<Result<List<Judge>>> GetJudgesAsync(string eventId, CancellationToken ct = default)
    {
        try
        {
            var judges = await _localStorage.GetItemAsync<List<Judge>>(JudgesKey, ct) ?? new List<Judge>();
            return Result<List<Judge>>.Success(judges.Where(j => j.EventId == eventId).ToList());
        }
        catch (Exception ex)
        {
            return Result<List<Judge>>.Failure(ex.Message, ex);
        }
    }

    public async Task<Result<Judge>> GetJudgeAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var judges = await _localStorage.GetItemAsync<List<Judge>>(JudgesKey, ct) ?? new List<Judge>();
            var judge = judges.FirstOrDefault(j => j.Id == id);
            return judge != null ? Result<Judge>.Success(judge) : Result<Judge>.Failure($"Judge {id} not found");
        }
        catch (Exception ex)
        {
            return Result<Judge>.Failure(ex.Message, ex);
        }
    }

    public async Task<Result> SaveJudgeAsync(Judge judge, CancellationToken ct = default)
    {
        try
        {
            var judges = await _localStorage.GetItemAsync<List<Judge>>(JudgesKey, ct) ?? new List<Judge>();
            var existing = judges.FirstOrDefault(j => j.Id == judge.Id);
            if (existing != null) judges.Remove(existing);
            judges.Add(judge);
            await _localStorage.SetItemAsync(JudgesKey, judges, ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message, ex);
        }
    }

    public Task<Result> ExecuteInTransactionAsync(Func<Task> action, CancellationToken ct = default)
    {
        // LocalStorage doesn't support transactions in a meaningful way here.
        // We'll just execute the action.
        return action().ContinueWith(_ => Result.Success());
    }
}
