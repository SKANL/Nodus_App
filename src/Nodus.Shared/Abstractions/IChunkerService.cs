namespace Nodus.Shared.Abstractions;

public interface IChunkerService
{
    List<byte[]> Split(byte[] payload, byte messageId);
}
