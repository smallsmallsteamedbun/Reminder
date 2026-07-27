using Reminder.App.Logic.Services;

namespace Reminder.App.SystemModule.Persistence;

public sealed class ReminderPersistenceCoordinator : IDisposable
{
    private static readonly TimeSpan SaveInterval =
        TimeSpan.FromHours(1);
    private readonly ReminderEngine _engine;
    private readonly ProtectedReminderStateStore _store;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly System.Threading.Timer _timer;
    private int _immediateSaveRequested;
    private int _immediateSaveWorkerRunning;
    private bool _started;
    private volatile bool _disposed;

    public ReminderPersistenceCoordinator(
        ReminderEngine engine,
        ProtectedReminderStateStore store)
    {
        _engine = engine;
        _store = store;
        _engine.DurableStateChanged += OnDurableStateChanged;
        _timer = new System.Threading.Timer(
            _ => SaveCheckpoint(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        if (_disposed || _started)
        {
            return;
        }

        _started = true;
        _timer.Change(SaveInterval, SaveInterval);
    }

    public ReminderStateSaveResult SaveFinal()
    {
        if (_disposed)
        {
            return new ReminderStateSaveResult
            {
                IsSuccess = false,
                ErrorMessage = "持久化协调器已经停止。"
            };
        }

        _saveGate.Wait();
        try
        {
            return _store.Save(_engine.ExportState());
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _engine.DurableStateChanged -= OnDurableStateChanged;
        _disposed = true;
        _timer.Dispose();
        _saveGate.Wait();
        _saveGate.Release();
    }

    private void OnDurableStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Exchange(ref _immediateSaveRequested, 1);
        StartImmediateSaveWorker();
    }

    private void StartImmediateSaveWorker()
    {
        if (_disposed ||
            Interlocked.CompareExchange(
                ref _immediateSaveWorkerRunning,
                1,
                0) != 0)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(
            static state =>
                ((ReminderPersistenceCoordinator)state!)
                .RunImmediateSaveWorker(),
            this);
    }

    private void RunImmediateSaveWorker()
    {
        try
        {
            while (!_disposed &&
                   Interlocked.Exchange(
                       ref _immediateSaveRequested,
                       0) != 0)
            {
                _saveGate.Wait();
                try
                {
                    if (!_disposed)
                    {
                        _ = _store.Save(_engine.ExportState());
                    }
                }
                finally
                {
                    _saveGate.Release();
                }
            }
        }
        finally
        {
            Interlocked.Exchange(
                ref _immediateSaveWorkerRunning,
                0);
            if (!_disposed &&
                Volatile.Read(ref _immediateSaveRequested) != 0)
            {
                StartImmediateSaveWorker();
            }
        }
    }

    private void SaveCheckpoint()
    {
        if (_disposed || !_saveGate.Wait(0))
        {
            return;
        }

        try
        {
            _ = _store.Save(_engine.ExportState());
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
