namespace Reminder.App.SystemModule.Runtime;

public sealed class ReminderSingleInstanceCoordinator : IDisposable
{
    private const string DefaultIdentity =
        "Reminder.Desktop.SingleInstance.v1";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly RegisteredWaitHandle _registeredWait;
    private bool _ownsMutex;
    private bool _disposed;

    private ReminderSingleInstanceCoordinator(
        Mutex mutex,
        EventWaitHandle activationEvent,
        Action activationRequested)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            static (state, timedOut) =>
            {
                if (!timedOut)
                {
                    ((Action)state!).Invoke();
                }
            },
            activationRequested,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);
        _ownsMutex = true;
    }

    public static bool TryAcquire(
        Action activationRequested,
        out ReminderSingleInstanceCoordinator? coordinator)
    {
        return TryAcquire(
            DefaultIdentity,
            activationRequested,
            out coordinator);
    }

    internal static bool TryAcquire(
        string identity,
        Action activationRequested,
        out ReminderSingleInstanceCoordinator? coordinator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(activationRequested);

        var mutexName = $@"Local\{identity}.Mutex";
        var eventName = $@"Local\{identity}.Activate";
        var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            eventName);
        var mutex = new Mutex(
            initiallyOwned: false,
            mutexName);

        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            activationEvent.Set();
            activationEvent.Dispose();
            mutex.Dispose();
            coordinator = null;
            return false;
        }

        coordinator = new ReminderSingleInstanceCoordinator(
            mutex,
            activationEvent,
            activationRequested);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registeredWait.Unregister(waitObject: null);
        _activationEvent.Dispose();
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
