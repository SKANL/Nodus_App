namespace Nodus.Shared.Models;

public enum SyncStatus
{
    Pending,
    Synced,
    SyncError
}

/// <summary>
/// Modelo compartido de Voto — POCO limpio sin dependencias de base de datos.
///
/// En MongoDB, el mapeo se hace en MongoDbService via VoteDocument:
///   - PayloadJson (string) → VoteDocument.Payload (BsonDocument nativo)
///   - Status (SyncStatus enum) → VoteDocument.Status (string "Pending"|"Synced"|"SyncError")
///
/// Esto permite queries directas en Mongo: db.votes.find({"payload.Design": {$gt: 7}})
/// </summary>
public class Vote
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EventId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string JudgeId { get; set; } = string.Empty;

    /// <summary>
    /// JSON con scores del voto. Ej: {"Design": 8, "Functionality": 9}
    /// MongoDbService lo deserializa a BsonDocument al guardar.
    /// </summary>
    public string PayloadJson { get; set; } = "{}";

    public SyncStatus Status { get; set; } = SyncStatus.Pending;
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // Media Sync Fields
    public string? LocalPhotoPath { get; set; }
    public bool IsMediaSynced { get; set; } = false;
    public DateTime? SyncedAtUtc { get; set; }
}

