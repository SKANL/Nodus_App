using SQLite;

namespace Nodus.Shared.Models;

public class Event
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public string Name { get; set; } = string.Empty;
    public string RubricJson { get; set; } = string.Empty;
    public string GlobalSalt { get; set; } = string.Empty;
    public string SharedAesKeyEncrypted { get; set; } = string.Empty; // Encrypted for distribution
    
    public bool IsActive { get; set; } = true;
}
