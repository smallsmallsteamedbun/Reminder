using System.Collections.ObjectModel;
using System.Windows.Threading;
using Reminder.App.Logic.Services;
using Reminder.App.SystemModule.AppInfo;

namespace Reminder.App.UI.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ReminderEngine _engine;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;
    private int _activeEventCount;

    public MainViewModel(ReminderEngine engine, Dispatcher dispatcher)
    {
        _engine = engine;
        _dispatcher = dispatcher;
        _engine.StateChanged += OnEngineStateChanged;

        AddEventCommand = new RelayCommand(AddEvent);
        PauseAllCommand = new RelayCommand(_engine.PauseAll);
        ResumeAllCommand = new RelayCommand(_engine.ResumeAll);
        RestartAllCommand = new RelayCommand(_engine.RestartAll);

        Refresh();
    }

    public string AppName => AppMetadata.Name;

    public string VersionText => $"版本 {AppMetadata.Version}";

    public string NotificationStatus => _engine.NotificationStatus;

    public event Action<Guid>? EventAdded;

    public event Action<EventViewModel>? DeleteRequested;

    public ObservableCollection<EventViewModel> Events { get; } = [];

    public RelayCommand AddEventCommand { get; }

    public RelayCommand PauseAllCommand { get; }

    public RelayCommand ResumeAllCommand { get; }

    public RelayCommand RestartAllCommand { get; }

    public int EventCount => Events.Count;

    public int ActiveEventCount
    {
        get => _activeEventCount;
        private set => SetProperty(ref _activeEventCount, value);
    }

    public string EventSummary => $"共 {EventCount} 个事件 · {ActiveEventCount} 个运行中";

    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var snapshots = _engine.GetSnapshots(DateTimeOffset.Now);
        var snapshotIds = snapshots.Select(item => item.Id).ToHashSet();

        for (var index = Events.Count - 1; index >= 0; index--)
        {
            if (!snapshotIds.Contains(Events[index].Id))
            {
                Events.RemoveAt(index);
            }
        }

        foreach (var snapshot in snapshots)
        {
            var viewModel = Events.FirstOrDefault(item => item.Id == snapshot.Id);
            if (viewModel is null)
            {
                viewModel = new EventViewModel(
                    _engine,
                    snapshot,
                    eventViewModel => DeleteRequested?.Invoke(eventViewModel));
                Events.Add(viewModel);
            }
            else
            {
                viewModel.ApplySnapshot(snapshot);
            }
        }

        ActiveEventCount = snapshots.Count(
            item => item.IsEnabled && !item.IsPaused && !item.IsAwaitingAction);
        OnPropertyChanged(nameof(EventCount));
        OnPropertyChanged(nameof(EventSummary));
        OnPropertyChanged(nameof(NotificationStatus));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine.StateChanged -= OnEngineStateChanged;
    }

    private void AddEvent()
    {
        var eventId = _engine.AddDefaultEvent();
        Refresh();
        EventAdded?.Invoke(eventId);
    }

    public void ConfirmDelete(Guid eventId)
    {
        _engine.Delete(eventId);
        Refresh();
    }

    private void OnEngineStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(Refresh, DispatcherPriority.DataBind);
    }
}
