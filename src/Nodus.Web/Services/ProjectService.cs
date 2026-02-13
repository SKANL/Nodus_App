using Blazored.LocalStorage;
using Microsoft.Extensions.Logging;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;

namespace Nodus.Web.Services;

public class ProjectService
{
    private readonly IDatabaseService _databaseService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(
        IDatabaseService databaseService, 
        ISettingsService settingsService,
        ILogger<ProjectService> logger)
    {
        _databaseService = databaseService;
        _settingsService = settingsService;
        _logger = logger;
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

        // Get EventId from settings instead of hardcoded value
        if (string.IsNullOrEmpty(project.EventId))
        {
            var eventId = await _settingsService.GetCurrentEventIdAsync();
            project.EventId = eventId ?? "LOCAL-EVENT"; // Fallback if settings not available
            _logger.LogInformation("Assigned Event ID {EventId} to project {ProjectId}", 
                project.EventId, project.Id);
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

    public Task<Project> SaveProjectAsync(Project project) => RegisterProjectAsync(project);
}
