namespace Nodus.Shared.Abstractions;

/// <summary>
/// Abstraction for application settings and configuration.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets the current active event ID.
    /// </summary>
    Task<string?> GetCurrentEventIdAsync();
    
    /// <summary>
    /// Sets the current active event ID.
    /// </summary>
    Task SetCurrentEventIdAsync(string eventId);
    
    /// <summary>
    /// Gets a setting value by key.
    /// </summary>
    Task<string?> GetSettingAsync(string key);
    
    /// <summary>
    /// Sets a setting value by key.
    /// </summary>
    Task SetSettingAsync(string key, string value);
}
