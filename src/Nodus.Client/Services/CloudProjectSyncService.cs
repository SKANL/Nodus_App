using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Nodus.Client.Services;

public class CloudProjectSyncService
{
    private readonly HttpClient _http;
    private readonly IDatabaseService _db;
    private readonly ILogger<CloudProjectSyncService> _logger;

    public CloudProjectSyncService(HttpClient http, IDatabaseService db, ILogger<CloudProjectSyncService> logger)
    {
        _http = http;
        _db = db;
        _logger = logger;
    }

    public async Task<bool> SyncProjectsAsync(string eventId, CancellationToken ct = default)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            _logger.LogInformation("No internet connection. Skipping cloud sync.");
            return false;
        }

        try
        {
            _logger.LogInformation("Attempting Cloud Sync for Event {EventId}", eventId);
            var projects = await _http.GetFromJsonAsync<List<Project>>($"api/projects/event/{eventId}", ct);
            if (projects != null && projects.Count > 0)
            {
                foreach (var proj in projects)
                {
                    await _db.SaveProjectAsync(proj, ct);
                }
                _logger.LogInformation("Successfully synced {Count} projects from cloud.", projects.Count);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync projects from cloud API.");
            return false;
        }
    }
}
