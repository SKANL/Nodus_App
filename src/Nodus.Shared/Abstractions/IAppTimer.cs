namespace Nodus.Shared.Abstractions;

public interface IAppTimer
{
    TimeSpan Interval { get; set; }
    event EventHandler Tick;
    void Start();
    void Stop();
}

public interface ITimerFactory
{
    IAppTimer CreateTimer();
}
