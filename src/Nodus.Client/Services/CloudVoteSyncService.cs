using Microsoft.Extensions.Logging;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;
using System.Net.Http.Json;

namespace Nodus.Client.Services;

/// <summary>
/// Uploads locally-saved votes that are still in <see cref="SyncStatus.Pending"/> state
/// to the Nodus REST API when an internet connection is available.
/// This is the HTTP fallback when BLE sync has not yet delivered the votes to the Server node.
/// </summary>
public class CloudVoteSyncService
{
    private readonly HttpClient _http;
    private readonly IDatabaseService _db;
    private readonly ILogger<CloudVoteSyncService> _logger;

    public CloudVoteSyncService(HttpClient http, IDatabaseService db, ILogger<CloudVoteSyncService> logger)
    {
        _http = http;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Fetches all pending votes from local DB and POSTs them to <c>api/votes</c>.
    /// On success the local record is updated to <see cref="SyncStatus.Synced"/>.
    /// Network errors are logged but do not throw — the vote stays Pending for the next cycle.
    /// </summary>
    public async Task<(int synced, int failed)> SyncPendingVotesAsync(CancellationToken ct = default)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            _logger.LogInformation("[VoteSync] No internet — skipping HTTP vote upload.");
            return (0, 0);
        }

        var pendingResult = await _db.GetPendingVotesAsync(ct);
        if (!pendingResult.IsSuccess || (pendingResult.Value?.Count ?? 0) == 0)
            return (0, 0);

        int synced = 0, failed = 0;

        foreach (var vote in pendingResult.Value!)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/votes", vote, ct);
                if (response.IsSuccessStatusCode)
                {
                    vote.Status = SyncStatus.Synced;
                    await _db.SaveVoteAsync(vote, ct);
                    synced++;
                    _logger.LogInformation("[VoteSync] Vote {VoteId} synced to cloud.", vote.Id);
                }
                else
                {
                    vote.Status = SyncStatus.SyncError;
                    await _db.SaveVoteAsync(vote, ct);
                    failed++;
                    _logger.LogWarning("[VoteSync] Vote {VoteId} rejected by API. Status: {Status}", vote.Id, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "[VoteSync] Exception uploading vote {VoteId}", vote.Id);
            }
        }

        _logger.LogInformation("[VoteSync] Complete — {Synced} synced, {Failed} failed", synced, failed);
        return (synced, failed);
    }
}
