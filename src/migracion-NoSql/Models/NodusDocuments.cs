using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Nodus.Migration.NoSql.Models;

/// <summary>
/// Equivalente al modelo Event de SQLite, adaptado para MongoDB.
/// </summary>
public class EventDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// En MongoDB se almacena como objeto nativo en lugar de string JSON.
    /// </summary>
    public BsonDocument? Rubric { get; set; }

    public string GlobalSalt { get; set; } = string.Empty;
    public string SharedAesKeyEncrypted { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Equivalente al modelo Project de SQLite, adaptado para MongoDB.
/// </summary>
public class ProjectDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty; // "PROJ-XYZ"

    /// <summary>Indexado en la colección.</summary>
    public string EventId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string Description { get; set; } = string.Empty;
    public string Authors { get; set; } = string.Empty;
    public string? GithubUrl { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Equivalente al modelo Vote de SQLite, adaptado para MongoDB.
/// </summary>
public class VoteDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
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
    public BsonDocument Payload { get; set; } = new BsonDocument();

    /// <summary>Pending | Synced | SyncError</summary>
    public string Status { get; set; } = "Pending";

    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public string? LocalPhotoPath { get; set; }
    public bool IsMediaSynced { get; set; } = false;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? SyncedAtUtc { get; set; }
}
