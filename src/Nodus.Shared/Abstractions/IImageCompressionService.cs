namespace Nodus.Shared.Abstractions;

public interface IImageCompressionService
{
    byte[] Compress(byte[] originalImage);
}
