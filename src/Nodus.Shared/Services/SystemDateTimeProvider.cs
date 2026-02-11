using Nodus.Shared.Abstractions;

namespace Nodus.Shared.Services;

public class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;

    public Task Delay(TimeSpan delay, CancellationToken ct = default)
    {
        return Task.Delay(delay, ct);
    }
}
