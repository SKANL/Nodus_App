using Blazored.LocalStorage;
using Nodus.Shared.Models;

namespace Nodus.Web.Services;

public class ProjectService
{
    private readonly ILocalStorageService _localStorage;
    private const string STORAGE_KEY = "nodus_projects"; // Using the same key as the original service to potential future sync? Or just "registered_project"

    // The roadmap implies we might have multiple projects, but for a student device likely just one.
    // However, keeping it as a list allows for flexibility.
    // But for the specific requirement of "Show MY project", we need to know which one is "mine".
    // For now, let's assume the device holds relevant projects.

    public ProjectService(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    /// <summary>
    /// Gets the first registered project (assuming single project per device for now)
    /// or logic to find "my" project.
    /// </summary>
    public async Task<Project?> GetCurrentProjectAsync()
    {
        var projects = await GetAllProjectsAsync();
        return projects.FirstOrDefault();
    }

    public async Task<Project?> GetProjectAsync(string id)
    {
        var projects = await GetAllProjectsAsync();
        return projects.FirstOrDefault(p => p.Id == id);
    }

    public async Task<List<Project>> GetAllProjectsAsync()
    {
        return await _localStorage.GetItemAsync<List<Project>>(STORAGE_KEY) ?? new List<Project>();
    }

    public async Task SaveProjectAsync(Project project)
    {
        var projects = await GetAllProjectsAsync();

        if (string.IsNullOrEmpty(project.Id))
        {
            project.Id = GenerateProjectId();
            // Note: CreatedAt is not in the Shared model yet, skipping for now or adding later if needed.
        }

        // Remove existing with same ID
        var existing = projects.FirstOrDefault(p => p.Id == project.Id);
        if (existing != null)
        {
            projects.Remove(existing);
        }
        else
        {
            // If new, ensuring we don't have duplicates or overwrite logic if single-project enforced?
            // For now, just add.
        }
        
        projects.Add(project);
        
        await _localStorage.SetItemAsync(STORAGE_KEY, projects);
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
