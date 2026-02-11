namespace Nodus.Shared.Abstractions;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    Task Delay(TimeSpan delay, CancellationToken ct = default);
}
