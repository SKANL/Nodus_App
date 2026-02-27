using System.Net.Http.Json;
using Nodus.Shared.Models;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;

namespace Nodus.Web.Services;

public class NodusApiService
{
    private readonly HttpClient _http;

    public NodusApiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<Result<List<Event>>> GetEventsAsync(CancellationToken ct = default)
    {
        try
        {
            var events = await _http.GetFromJsonAsync<List<Event>>("api/events", ct);
            return Result<List<Event>>.Success(events ?? new List<Event>());
        }
        catch (Exception ex)
        {
            return Result<List<Event>>.Failure($"Failed to fetch events from API: {ex.Message}");
        }
    }

    public async Task<Result<List<Project>>> GetProjectsAsync(string eventId, CancellationToken ct = default)
    {
        try
        {
            var projects = await _http.GetFromJsonAsync<List<Project>>($"api/projects/event/{eventId}", ct);
            return Result<List<Project>>.Success(projects ?? new List<Project>());
        }
        catch (Exception ex)
        {
            return Result<List<Project>>.Failure($"Failed to fetch projects from API: {ex.Message}");
        }
    }

    public async Task<Result<Project>> SaveProjectAsync(Project project, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/projects", project, ct);
            if (response.IsSuccessStatusCode)
            {
                var savedProject = await response.Content.ReadFromJsonAsync<Project>(cancellationToken: ct);
                return Result<Project>.Success(savedProject ?? project);
            }
            return Result<Project>.Failure($"Failed to save project. Status: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return Result<Project>.Failure($"Exception while saving project to API: {ex.Message}");
        }
    }
}
