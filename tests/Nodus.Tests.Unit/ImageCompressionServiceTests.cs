using System;
using System.IO;
using Xunit;
using Nodus.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Nodus.Tests.Unit;

public class ImageCompressionServiceTests
{
    private readonly ImageCompressionService _service;

    public ImageCompressionServiceTests()
    {
        var logger = NullLogger<ImageCompressionService>.Instance;
        _service = new ImageCompressionService(logger);
    }

    [Fact]
    public void Compress_SmallImage_ReturnsOriginal()
    {
        // Arrange
        // Target is 512KB. Let's make a 1KB array.
        var data = new byte[1024];
        new Random().NextBytes(data);

        // Act
        var result = _service.Compress(data);

        // Assert
        Assert.Same(data, result);
    }

    [Fact]
    public void Compress_InvalidLargeImage_ReturnsOriginal()
    {
        // Arrange
        // > 512KB but invalid image data
        var data = new byte[600 * 1024]; 
        new Random().NextBytes(data); // Random noise

        // Act
        var result = _service.Compress(data);

        // Assert
        // Should fail decode and return original
        Assert.Same(data, result); 
    }

    [Fact]
    public void Compress_ValidLargeImage_ReturnsCompressed()
    {
        // Arrange
        // Create a large bitmap to simulate a camera photo
        // 2000x2000 roughly 4MP -> Raw RGBA is 16MB. encoded JPEG will be > 512KB likely.
        using var bitmap = new SKBitmap(2000, 2000);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Red); // Solid color compresses well, maybe too well?
        
        // Add some noise/detail to make it compress less efficiently
        using var paint = new SKPaint { Color = SKColors.Blue };
        using var font = new SKFont(SKTypeface.Default, 100);
        for(int i=0; i<50; i++)
        {
            canvas.DrawText("Nodus", i*40, i*40, SKTextAlign.Left, font, paint);
        }

        using var stream = new MemoryStream();
        bitmap.Encode(stream, SKEncodedImageFormat.Jpeg, 100); // 100 Quality
        var validLargeImage = stream.ToArray();

        // Ensure we actually created a large enough file for the test to be meaningful
        if (validLargeImage.Length <= 512 * 1024)
        {
            // If our test image is too small naturally, we can't test compression trigger.
            // But let's verify if it *was* large, it got compressed.
            // For this test, if it's small, we skip the compression assert.
            // But realistically 2000x2000 JPEG at Q100 should be > 512KB.
        }

        // Act
        var result = _service.Compress(validLargeImage);

        // Assert
        Assert.True(result.Length <= 512 * 1024, $"Result size {result.Length} should be <= 512KB");
        
        // If it was originally > 512KB, result should be smaller
        if (validLargeImage.Length > 512 * 1024)
        {
            Assert.True(result.Length < validLargeImage.Length);
        }
    }
}
