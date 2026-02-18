using Nodus.Shared.Abstractions;

namespace Nodus.Shared.Services;

public class FileService : IFileService
{
    public bool Exists(string path) => File.Exists(path);

    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default)
    {
        return await File.ReadAllBytesAsync(path, ct);
    }

    public async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    public void CreateDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
    public string GetAppDataDirectory()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(basePath, "Nodus");
    }
}
