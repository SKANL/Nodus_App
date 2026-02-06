using SQLite;

namespace Nodus.Shared.Models;

public class Project
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty; // "PROJ-XYZ"
    public string Name { get; set; } = string.Empty;
    [Indexed]
    public string EventId { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string Description { get; set; } = string.Empty;
}
