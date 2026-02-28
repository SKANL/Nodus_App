using Blazored.LocalStorage;
using Microsoft.Extensions.Logging;
using Nodus.Shared.Abstractions;

namespace Nodus.Web.Services;

/// <summary>
/// Blazor WASM implementation of ISettingsService using LocalStorage.
/// Settings survive page reloads because they are persisted in the browser's localStorage.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ILogger<SettingsService> _logger;
    private readonly EventService _eventService;
    private readonly ILocalStorageService _localStorage;
    private const string CURRENT_EVENT_KEY = "CurrentEventId";

    public SettingsService(ILogger<SettingsService> logger, EventService eventService, ILocalStorageService localStorage)
    {
        _logger = logger;
        _eventService = eventService;
        _localStorage = localStorage;
    }

    public async Task<string?> GetCurrentEventIdAsync()
    {
        // 1. Check if manually overridden in localStorage
        var stored = await _localStorage.GetItemAsync<string>(CURRENT_EVENT_KEY);
        if (!string.IsNullOrWhiteSpace(stored))
            return stored;

        // 2. Otherwise discover from EventService
        var activeEvent = await _eventService.GetActiveEventAsync();
        if (activeEvent != null)
        {
            // Persist discovered event so subsequent reads are fast
            await _localStorage.SetItemAsync(CURRENT_EVENT_KEY, activeEvent.Id);
            return activeEvent.Id;
        }

        _logger.LogWarning("No current event ID found and discovery failed.");
        return null;
    }

    public async Task SetCurrentEventIdAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            _logger.LogWarning("Attempted to set empty event ID");
            return;
        }

        await _localStorage.SetItemAsync(CURRENT_EVENT_KEY, eventId);
        _logger.LogInformation("Current event ID set to: {EventId}", eventId);
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        return await _localStorage.GetItemAsync<string>(key);
    }

    public async Task SetSettingAsync(string key, string value)
    {
        await _localStorage.SetItemAsync(key, value);
    }
}
