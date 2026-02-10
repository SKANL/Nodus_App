using SQLite;

namespace Nodus.Shared.Models;

public enum SyncStatus
{
    Pending,
    Synced,
    SyncError
}

public class Vote
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Indexed]
    public string EventId { get; set; } = string.Empty;
    
    [Indexed]
    public string ProjectId { get; set; } = string.Empty;
    
    [Indexed]
    public string JudgeId { get; set; } = string.Empty;

    // JSON string containing scores e.g. {"Design": 8, "Functionality": 9}
    public string PayloadJson { get; set; } = "{}"; 

    public SyncStatus Status { get; set; } = SyncStatus.Pending;
    
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    
    // Media Sync Fields
    public string? LocalPhotoPath { get; set; }
    public bool IsMediaSynced { get; set; }
    public DateTime? SyncedAtUtc { get; set; }
}
