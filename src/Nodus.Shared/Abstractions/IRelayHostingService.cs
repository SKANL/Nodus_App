using Nodus.Shared.Common;

namespace Nodus.Shared.Abstractions;

public interface IRelayHostingService
{
    bool IsAdvertising { get; }
    Task<Result> StartAdvertisingAsync(CancellationToken ct = default);
    void StopAdvertising();
}
