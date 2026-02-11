using Blazored.LocalStorage;
using Nodus.Shared.Models;

namespace Nodus.Web.Services;

public class ProjectService
{
    private readonly Nodus.Shared.Abstractions.IDatabaseService _databaseService;

    // TODO: In a real scenario, this should come from a setting or user selection
    private const string DEFAULT_EVENT_ID = "LOCAL-EVENT";

    public ProjectService(Nodus.Shared.Abstractions.IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    /// <summary>
    /// Gets the first registered project (assuming single project per device for now)
    /// </summary>
    public async Task<Project?> GetCurrentProjectAsync()
    {
        var result = await _databaseService.GetAllProjectsAsync();
        return result.IsSuccess ? result.Value.FirstOrDefault() : null;
    }

    public async Task<Project?> GetProjectAsync(string id)
    {
        var result = await _databaseService.GetProjectAsync(id);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<List<Project>> GetAllProjectsAsync()
    {
        var result = await _databaseService.GetAllProjectsAsync();
        return result.IsSuccess ? result.Value : new List<Project>();
    }

    public async Task<Project> RegisterProjectAsync(Project project)
    {
        if (string.IsNullOrEmpty(project.Id))
        {
            project.Id = GenerateProjectId();
        }

        // Ensure EventId is set for consistency with Shared schema
        if (string.IsNullOrEmpty(project.EventId))
        {
            project.EventId = DEFAULT_EVENT_ID;
        }

        // DatabaseService handles InsertOrReplace
        await _databaseService.SaveProjectAsync(project);
        
        return project;
    }

    private string GenerateProjectId()
    {
        var random = new Random();
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var randomString = new string(Enumerable.Repeat(chars, 3)
            .Select(s => s[random.Next(s.Length)]).ToArray());
        return $"PROJ-{randomString}";
    }
}
