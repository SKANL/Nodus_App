namespace Nodus.Infrastructure.Models;

/// <summary>
/// Equivalente al modelo Event de SQLite, adaptado para MongoDB.
/// </summary>
public class EventDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// En MongoDB se almacena como objeto nativo en lugar de string JSON.
    /// </summary>
    public string? Rubric { get; set; }

    public string GlobalSalt { get; set; } = string.Empty;
    public string SharedAesKeyEncrypted { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Equivalente al modelo Project de SQLite, adaptado para MongoDB.
/// </summary>
public class ProjectDocument
{
    public string Id { get; set; } = string.Empty; // "PROJ-XYZ"

    /// <summary>Indexado en la colección.</summary>
    public string EventId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string Description { get; set; } = string.Empty;
    public string Authors { get; set; } = string.Empty;
    public string? GithubUrl { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Equivalente al modelo Vote de SQLite, adaptado para MongoDB.
/// </summary>
public class VoteDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Indexado en la colección.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>Indexado en la colección.</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Indexado en la colección.</summary>
    public string JudgeId { get; set; } = string.Empty;

    /// <summary>
    /// VENTAJA vs SQLite: PayloadJson era un string. Aquí es un objeto nativo.
    /// Permite queries como: db.votes.find({"payload.Design": {$gt: 7}})
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Pending | Synced | SyncError</summary>
    public string Status { get; set; } = "Pending";

    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public string? LocalPhotoPath { get; set; }
    public bool IsMediaSynced { get; set; } = false;

    public DateTime? SyncedAtUtc { get; set; }
}
