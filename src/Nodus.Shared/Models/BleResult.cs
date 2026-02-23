namespace Nodus.Shared.Models;

public class BleResult
{
    public byte[]? Data { get; set; }
    public string? CharacteristicUuid { get; set; }
    public string? ServiceUuid { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
