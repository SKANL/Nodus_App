using QRCoder;
using System.Drawing;

namespace Nodus.Web.Services;

/// <summary>
/// Service for generating QR codes for project voting.
/// Uses QRCoder library for high-quality QR generation.
/// </summary>
public class QrGeneratorService
{
    /// <summary>
    /// Generates a QR code as Base64 PNG image.
    /// </summary>
    /// <param name="content">Content to encode (e.g., nodus://vote?pid=PROJ-ABC)</param>
    /// <param name="pixelsPerModule">Size of each QR module (default: 20)</param>
    /// <returns>Base64-encoded PNG image</returns>
    public string GenerateQrCodeBase64(string content, int pixelsPerModule = 20)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        
        var qrCodeBytes = qrCode.GetGraphic(pixelsPerModule);
        return Convert.ToBase64String(qrCodeBytes);
    }
    
    /// <summary>
    /// Generates a voting QR code for a specific project.
    /// Format: nodus://vote?pid={projectId}&cat={category}
    /// </summary>
    public string GenerateVotingQrCode(string projectId, string category, int size = 20)
    {
        var content = $"nodus://vote?pid={projectId}&cat={Uri.EscapeDataString(category)}";
        return GenerateQrCodeBase64(content, size);
    }
    
    /// <summary>
    /// Generates an event registration QR code.
    /// Format: nodus://event?id={eventId}&key={encryptedKey}
    /// </summary>
    public string GenerateEventQrCode(string eventId, string encryptedKey, int size = 20)
    {
        var content = $"nodus://event?id={eventId}&key={Uri.EscapeDataString(encryptedKey)}";
        return GenerateQrCodeBase64(content, size);
    }
    
    /// <summary>
    /// Generates a project display QR code with custom branding.
    /// </summary>
    public string GenerateProjectDisplayQrCode(string projectId, string projectName, string category, int size = 25)
    {
        var content = $"nodus://vote?pid={projectId}&name={Uri.EscapeDataString(projectName)}&cat={Uri.EscapeDataString(category)}";
        return GenerateQrCodeBase64(content, size);
    }
}
