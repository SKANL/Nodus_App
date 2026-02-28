using Microsoft.Extensions.Logging;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Nodus.Shared.Models;
using System.Text.Json;

namespace Nodus.Web.Services;

public class EventService
{
    private readonly NodusApiService _apiService;
    private readonly IDatabaseService _localDb;
    private readonly ILogger<EventService> _logger;
    private Event? _activeEvent;

    public EventService(
        NodusApiService apiService,
        IDatabaseService localDb,
        ILogger<EventService> logger)
    {
        _apiService = apiService;
        _localDb = localDb;
        _logger = logger;
    }

    public async Task<Event?> GetActiveEventAsync()
    {
        if (_activeEvent != null) return _activeEvent;

        // 1. Try to find an active event in cloud
        var cloudResult = await _apiService.GetEventsAsync();
        if (cloudResult.IsSuccess && (cloudResult.Value ?? []).Any())
        {
            // Pick the most recent active event when IDs include unix timestamp
            // (e.g., EVENT-Name-1740622222). Falls back to stable first active.
            _activeEvent = (cloudResult.Value ?? [])
                .Where(e => e.IsActive)
                .OrderByDescending(e => TryExtractUnixTimestamp(e.Id) ?? 0)
                .FirstOrDefault();

            if (_activeEvent != null)
            {
                _logger.LogInformation("Discovered active event from cloud: {Name}", _activeEvent.Name);

                // Sync to local DB for offline support
                await _localDb.SaveEventAsync(_activeEvent);
                return _activeEvent;
            }
        }

        // 2. Fallback: Try local DB
        var localResult = await _localDb.GetEventsAsync();
        if (localResult.IsSuccess && (localResult.Value ?? []).Any())
        {
            _activeEvent = (localResult.Value ?? [])
                .Where(e => e.IsActive)
                .FirstOrDefault();

            if (_activeEvent != null)
            {
                _logger.LogInformation("Found active event in local storage: {Name}", _activeEvent.Name);
                return _activeEvent;
            }
        }

        return null;
    }

    private static long? TryExtractUnixTimestamp(string? eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return null;

        var lastDash = eventId.LastIndexOf('-');
        if (lastDash < 0 || lastDash == eventId.Length - 1) return null;

        var suffix = eventId[(lastDash + 1)..];
        return long.TryParse(suffix, out var ts) ? ts : null;
    }

    public async Task<List<string>> GetCategoriesAsync()
        => await GetCategoriesAsync(null);

    public async Task<List<string>> GetCategoriesAsync(string? eventId)
    {
        Event? evt = null;

        if (!string.IsNullOrWhiteSpace(eventId))
        {
            // Try to get the specific event first
            var localResult = await _localDb.GetEventAsync(eventId);
            if (localResult.IsSuccess) evt = localResult.Value;
        }

        // Fallback to the active event if not found
        evt ??= await GetActiveEventAsync();

        if (evt == null || string.IsNullOrWhiteSpace(evt.RubricJson))
        {
            return new List<string> { "Software", "Hardware", "Innovation" }; // Default fallback
        }

        try
        {
            // The rubric might be a comma-separated list or a JSON dictionary
            if (evt.RubricJson.Contains("{"))
            {
                // It's a JSON Rubric: {"Design": 10, "Function": 10} -> Keys are categories
                using var doc = JsonDocument.Parse(evt.RubricJson);
                return doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            }
            else
            {
                // It's a simple list: "Category1, Category2"
                return evt.RubricJson.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim())
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse rubric JSON: {Json}", evt.RubricJson);
            return new List<string> { "Software", "Hardware", "Innovation" };
        }
    }
}
