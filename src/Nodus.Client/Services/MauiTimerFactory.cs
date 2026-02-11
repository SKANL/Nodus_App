using Nodus.Shared.Abstractions;

namespace Nodus.Client.Services;

public class MauiTimerFactory : ITimerFactory
{
    public IAppTimer CreateTimer()
    {
        return new MauiTimerWrapper();
    }
}

public class MauiTimerWrapper : IAppTimer
{
    private readonly IDispatcherTimer _timer;

    public MauiTimerWrapper()
    {
        _timer = Application.Current!.Dispatcher.CreateTimer();
        _timer.Tick += (s, e) => Tick?.Invoke(this, EventArgs.Empty);
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public event EventHandler? Tick;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}
