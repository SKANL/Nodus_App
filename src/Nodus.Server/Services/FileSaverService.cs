using Microsoft.Extensions.Logging;
using Nodus.Shared.Abstractions;

namespace Nodus.Server.Services;

/// <summary>
/// Native implementation of IFileSaverService for .NET MAUI.
/// Saves files to the app's Documents directory.
/// </summary>
public class FileSaverService : IFileSaverService
{
    private readonly ILogger<FileSaverService> _logger;

    public FileSaverService(ILogger<FileSaverService> logger)
    {
        _logger = logger;
    }

    public async Task<FileSaveResult> SaveAsync(string fileName, byte[] data, string contentType = "application/octet-stream")
    {
        try
        {
            // Use the app's Documents directory for exported files
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var appFolder = Path.Combine(documentsPath, "NodusExports");

            // Create directory if it doesn't exist
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
                _logger.LogInformation("Created exports directory: {Path}", appFolder);
            }

            // Generate unique filename if file already exists
            var filePath = Path.Combine(appFolder, fileName);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var counter = 1;

            while (File.Exists(filePath))
            {
                var newFileName = $"{fileNameWithoutExt}_{counter}{extension}";
                filePath = Path.Combine(appFolder, newFileName);
                counter++;
            }

            // Write the file
            await File.WriteAllBytesAsync(filePath, data);

            _logger.LogInformation("File saved successfully: {FilePath} ({Size} bytes)",
                filePath, data.Length);

            return FileSaveResult.Success(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save file: {FileName}", fileName);
            return FileSaveResult.Failure(ex);
        }
    }
}
