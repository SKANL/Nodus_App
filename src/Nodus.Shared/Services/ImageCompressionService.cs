using SkiaSharp;
using Microsoft.Extensions.Logging;

namespace Nodus.Shared.Services;

using Nodus.Shared.Abstractions;

public class ImageCompressionService : IImageCompressionService
{
    private readonly ILogger<ImageCompressionService> _logger;
    private const int TargetSize = 512 * 1024; // 512 KB
    private const int MaxQuality = 85;
    private const int MinQuality = 40;

    public ImageCompressionService(ILogger<ImageCompressionService> logger)
    {
        _logger = logger;
    }

    public byte[] Compress(byte[] originalImage)
    {
        if (originalImage.Length <= TargetSize)
        {
            return originalImage; // No compression needed
        }

        try
        {
            using var inputStream = new MemoryStream(originalImage);
            using var bitmap = SKBitmap.Decode(inputStream);

            if (bitmap == null)
            {
                _logger.LogWarning("Failed to decode image for compression");
                return originalImage; // Return original if decode fails
            }

            // Attempt 1: Just re-encode with high quality
            using var outputStream = new MemoryStream();
            bitmap.Encode(outputStream, SKEncodedImageFormat.Jpeg, MaxQuality);

            if (outputStream.Length <= TargetSize)
            {
                return outputStream.ToArray();
            }

            // Attempt 2: Resize if too big (e.g. > 4MB or just simply massive)
            // Or Step down quality

            int quality = MaxQuality;
            byte[] result = outputStream.ToArray();

            while (result.Length > TargetSize && quality > MinQuality)
            {
                quality -= 10;
                outputStream.SetLength(0); // Reset
                bitmap.Encode(outputStream, SKEncodedImageFormat.Jpeg, quality);
                result = outputStream.ToArray();
            }

            // If still too big, resize
            if (result.Length > TargetSize)
            {
                // Resize to 75%
                int newWidth = (int)(bitmap.Width * 0.75);
                int newHeight = (int)(bitmap.Height * 0.75);

                using var scaled = bitmap.Resize(new SKImageInfo(newWidth, newHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Nearest));
                if (scaled != null)
                {
                    outputStream.SetLength(0);
                    scaled.Encode(outputStream, SKEncodedImageFormat.Jpeg, quality);
                    result = outputStream.ToArray();
                }
            }

            _logger.LogInformation("Compressed image from {OldSize} to {NewSize} bytes", originalImage.Length, result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compressing image");
            return originalImage;
        }
    }
}
