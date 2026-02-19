namespace Nodus.Shared.Models;

/// <summary>
/// Modelo compartido de Evento — POCO limpio sin dependencias de base de datos.
///
/// En MongoDB, el mapeo se hace en MongoDbService via EventDocument:
///   - RubricJson (string) → EventDocument.Rubric (BsonDocument nativo)
/// </summary>
public class Event
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// JSON con la rúbrica del evento. Ej: {"Design": 10, "Functionality": 10}
    /// MongoDbService lo deserializa a BsonDocument al guardar.
    /// </summary>
    public string RubricJson { get; set; } = "{}";

    public string GlobalSalt { get; set; } = string.Empty;
    public string SharedAesKeyEncrypted { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
