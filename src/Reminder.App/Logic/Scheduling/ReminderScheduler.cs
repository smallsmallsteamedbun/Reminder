namespace Reminder.App.Logic.Scheduling;

internal sealed class ReminderScheduler : IDisposable
{
    private static readonly TimeSpan MaximumSingleWait = TimeSpan.FromDays(20);
    private readonly System.Threading.Timer _timer;
    private readonly Action _onElapsed;
    private bool _disposed;

    public ReminderScheduler(Action onElapsed)
    {
        _onElapsed = onElapsed;
        _timer = new System.Threading.Timer(OnTimerElapsed);
    }

    public void Schedule(DateTimeOffset? dueAt, DateTimeOffset now)
    {
        if (_disposed)
        {
            return;
        }

        if (dueAt is null)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var delay = dueAt.Value - now;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }
        else if (delay > MaximumSingleWait)
        {
            delay = MaximumSingleWait;
        }

        _timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }

    private void OnTimerElapsed(object? state)
    {
        if (!_disposed)
        {
            _onElapsed();
        }
    }
}
