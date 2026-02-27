using System.Net.Http.Json;
using Nodus.Shared.Common;
using Nodus.Shared.Models;

namespace Nodus.Web.Services;

public class BackendApiService
{
    private readonly HttpClient _httpClient;

    public BackendApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Result<List<Project>>> GetProjectsAsync(string eventId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/projects/event/{eventId}");
            if (response.IsSuccessStatusCode)
            {
                var projects = await response.Content.ReadFromJsonAsync<List<Project>>();
                return Result<List<Project>>.Success(projects ?? new List<Project>());
            }
            return Result<List<Project>>.Failure($"API Error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return Result<List<Project>>.Failure("Network error calling Backend API", ex);
        }
    }

    public virtual async Task<Result<List<Event>>> GetEventsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/events");
            if (response.IsSuccessStatusCode)
            {
                var events = await response.Content.ReadFromJsonAsync<List<Event>>();
                return Result<List<Event>>.Success(events ?? new List<Event>());
            }
            return Result<List<Event>>.Failure($"API Error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return Result<List<Event>>.Failure("Network error calling Backend API", ex);
        }
    }

    public async Task<Result> SaveProjectAsync(Project project)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/projects", project);
            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }
            return Result.Failure($"API Error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return Result.Failure("Network error calling Backend API", ex);
        }
    }
}
