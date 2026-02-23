using Microsoft.Extensions.Logging;
using Nodus.Shared.Abstractions;

namespace Nodus.Web.Services;

/// <summary>
/// Blazor WASM implementation of ISettingsService using LocalStorage.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ILogger<SettingsService> _logger;
    private readonly EventService _eventService;
    private const string CURRENT_EVENT_KEY = "CurrentEventId";

    // Simple in-memory storage for Blazor WASM
    // In production, this could use Blazored.LocalStorage
    private readonly Dictionary<string, string> _settings = new();

    public SettingsService(ILogger<SettingsService> logger, EventService eventService)
    {
        _logger = logger;
        _eventService = eventService;
    }

    public async Task<string?> GetCurrentEventIdAsync()
    {
        // 1. Check if manually overridden in settings
        if (_settings.TryGetValue(CURRENT_EVENT_KEY, out var eventId))
        {
            return eventId;
        }
        
        // 2. Otherwise discovery from EventService
        var activeEvent = await _eventService.GetActiveEventAsync();
        if (activeEvent != null)
        {
            return activeEvent.Id;
        }

        _logger.LogWarning("No current event ID found and discovery failed.");
        return null;
    }

    public Task SetCurrentEventIdAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            _logger.LogWarning("Attempted to set empty event ID");
            return Task.CompletedTask;
        }

        _settings[CURRENT_EVENT_KEY] = eventId;
        _logger.LogInformation("Current event ID set to: {EventId}", eventId);
        return Task.CompletedTask;
    }

    public Task<string?> GetSettingAsync(string key)
    {
        if (_settings.TryGetValue(key, out var value))
        {
            return Task.FromResult<string?>(value);
        }
        
        return Task.FromResult<string?>(null);
    }

    public Task SetSettingAsync(string key, string value)
    {
        _settings[key] = value;
        return Task.CompletedTask;
    }
}
