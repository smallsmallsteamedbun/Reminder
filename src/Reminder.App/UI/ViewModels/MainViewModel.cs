using System.Collections.ObjectModel;
using System.Windows.Threading;
using Reminder.App.Logic.Models;
using Reminder.App.Logic.Services;
using Reminder.App.SystemModule.AppInfo;

namespace Reminder.App.UI.ViewModels;

public sealed record GlobalPauseChoice(
    ReminderGlobalPauseDuration Value,
    string Label);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly IReadOnlyList<GlobalPauseChoice>
        GlobalPauseChoiceValues =
    [
        new(ReminderGlobalPauseDuration.UntilManualResume, "直到手动恢复"),
        new(ReminderGlobalPauseDuration.OneMinute, "1 分钟"),
        new(ReminderGlobalPauseDuration.FiveMinutes, "5 分钟"),
        new(ReminderGlobalPauseDuration.TenMinutes, "10 分钟"),
        new(ReminderGlobalPauseDuration.FifteenMinutes, "15 分钟"),
        new(ReminderGlobalPauseDuration.ThirtyMinutes, "30 分钟"),
        new(ReminderGlobalPauseDuration.OneHour, "1 小时"),
        new(ReminderGlobalPauseDuration.TwoHours, "2 小时"),
        new(ReminderGlobalPauseDuration.FiveHours, "5 小时")
    ];

    private readonly ReminderEngine _engine;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;
    private int _activeEventCount;
    private bool _isGlobalPaused;
    private bool _showGlobalPauseCountdown;
    private string _globalPauseRemainingText = string.Empty;
    private GlobalPauseChoice _selectedGlobalPauseChoice =
        GlobalPauseChoiceValues[0];
    private bool _synchronizingGlobalPause;

    public MainViewModel(ReminderEngine engine, Dispatcher dispatcher)
    {
        _engine = engine;
        _dispatcher = dispatcher;
        _engine.StateChanged += OnEngineStateChanged;

        AddEventCommand = new RelayCommand(AddEvent);
        ToggleGlobalPauseCommand =
            new RelayCommand(ToggleGlobalPause);
        RestartAllCommand = new RelayCommand(_engine.RestartAll);

        Refresh();
    }

    public string AppName => AppMetadata.Name;

    public string VersionText => $"版本 {AppMetadata.Version}";

    public string NotificationStatus => _engine.NotificationStatus;

    public string NotificationStatusHelp => _engine.NotificationStatusHelp;

    public bool NotificationsAvailable => _engine.NotificationsAvailable;

    public event Action<Guid>? EventAdded;

    public event Action<EventViewModel>? DeleteRequested;

    public ObservableCollection<EventViewModel> Events { get; } = [];

    public RelayCommand AddEventCommand { get; }

    public RelayCommand ToggleGlobalPauseCommand { get; }

    public RelayCommand RestartAllCommand { get; }

    public IReadOnlyList<GlobalPauseChoice> GlobalPauseChoices =>
        GlobalPauseChoiceValues;

    public int EventCount => Events.Count;

    public int ActiveEventCount
    {
        get => _activeEventCount;
        private set => SetProperty(ref _activeEventCount, value);
    }

    public string EventSummary => $"共 {EventCount} 个事件 · {ActiveEventCount} 个运行中";

    public bool IsGlobalPaused
    {
        get => _isGlobalPaused;
        private set
        {
            if (SetProperty(ref _isGlobalPaused, value))
            {
                OnPropertyChanged(nameof(GlobalPauseToggleText));
                OnPropertyChanged(nameof(GlobalPauseToggleHint));
            }
        }
    }

    public string GlobalPauseToggleText =>
        IsGlobalPaused ? "全部恢复" : "全部暂停";

    public string GlobalPauseToggleHint =>
        IsGlobalPaused
            ? "继续所有已开启事件的倒计时"
            : "暂停所有已开启事件的倒计时";

    public GlobalPauseChoice SelectedGlobalPauseChoice
    {
        get => _selectedGlobalPauseChoice;
        set
        {
            if (value is null ||
                !SetProperty(ref _selectedGlobalPauseChoice, value) ||
                _synchronizingGlobalPause)
            {
                return;
            }

            _engine.SetGlobalPauseDuration(value.Value);
        }
    }

    public bool ShowGlobalPauseCountdown
    {
        get => _showGlobalPauseCountdown;
        private set => SetProperty(ref _showGlobalPauseCountdown, value);
    }

    public string GlobalPauseRemainingText
    {
        get => _globalPauseRemainingText;
        private set => SetProperty(ref _globalPauseRemainingText, value);
    }

    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var snapshots = _engine.GetSnapshots(now);
        var globalPause = _engine.GetGlobalPauseSnapshot(now);
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

        ActiveEventCount = snapshots.Count(item => item.IsEffectivelyRunning);
        _synchronizingGlobalPause = true;
        try
        {
            IsGlobalPaused = globalPause.IsPaused;
            SelectedGlobalPauseChoice =
                GlobalPauseChoiceValues.First(
                    item => item.Value == globalPause.Duration);
        }
        finally
        {
            _synchronizingGlobalPause = false;
        }

        ShowGlobalPauseCountdown =
            globalPause.IsPaused && globalPause.Remaining is not null;
        GlobalPauseRemainingText = globalPause.Remaining is null
            ? string.Empty
            : $"剩余 {FormatCountdown(globalPause.Remaining.Value)}";
        OnPropertyChanged(nameof(EventCount));
        OnPropertyChanged(nameof(EventSummary));
        OnPropertyChanged(nameof(NotificationStatus));
        OnPropertyChanged(nameof(NotificationStatusHelp));
        OnPropertyChanged(nameof(NotificationsAvailable));
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

    private void ToggleGlobalPause()
    {
        if (IsGlobalPaused)
        {
            _engine.ResumeAll();
            return;
        }

        _engine.PauseAll(
            ReminderGlobalPauseDuration.UntilManualResume);
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

    private static string FormatCountdown(TimeSpan remaining)
    {
        var totalSeconds = Math.Max(
            0,
            (long)Math.Ceiling(remaining.TotalSeconds));
        var hours = totalSeconds / 3_600;
        var minutes = totalSeconds % 3_600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? $"{hours}时{minutes:00}分{seconds:00}秒"
            : $"{minutes:00}分{seconds:00}秒";
    }
}
