namespace Nodus.Shared.Abstractions;

/// <summary>
/// Abstraction for saving files to the device.
/// </summary>
public interface IFileSaverService
{
    /// <summary>
    /// Saves a file to the device's storage.
    /// </summary>
    /// <param name="fileName">The name of the file to save</param>
    /// <param name="data">The file content as byte array</param>
    /// <param name="contentType">MIME type of the file (e.g., "text/csv", "application/json")</param>
    /// <returns>Result containing the file path if successful, or error message</returns>
    Task<FileSaveResult> SaveAsync(string fileName, byte[] data, string contentType = "application/octet-stream");
}

/// <summary>
/// Result of a file save operation.
/// </summary>
public class FileSaveResult
{
    public bool IsSuccessful { get; set; }
    public string? FilePath { get; set; }
    public Exception? Exception { get; set; }
    
    public static FileSaveResult Success(string filePath) => new()
    {
        IsSuccessful = true,
        FilePath = filePath
    };
    
    public static FileSaveResult Failure(Exception exception) => new()
    {
        IsSuccessful = false,
        Exception = exception
    };
}
