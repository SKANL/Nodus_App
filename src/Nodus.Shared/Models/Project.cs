namespace Nodus.Shared.Models;

/// <summary>
/// Modelo compartido de Proyecto — POCO limpio sin dependencias de base de datos.
/// Los atributos BSON se manejan en MongoDbService via ProjectDocument.
/// </summary>
public class Project
{
    public string Id { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string Description { get; set; } = string.Empty;
    public string Authors { get; set; } = string.Empty;
    public string? GithubUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
