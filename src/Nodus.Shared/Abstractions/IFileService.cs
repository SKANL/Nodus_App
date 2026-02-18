namespace Nodus.Shared.Abstractions;

public interface IFileService
{
    bool Exists(string path);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default);
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default);
    void CreateDirectory(string path);
    void Delete(string path);
    string GetAppDataDirectory();
}
