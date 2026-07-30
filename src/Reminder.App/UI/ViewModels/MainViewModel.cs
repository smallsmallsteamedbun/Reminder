using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using Reminder.App.Logic.Models;
using Reminder.App.Logic.Services;
using Reminder.App.SystemModule.AppInfo;
using Reminder.App.SystemModule.Settings;
using Reminder.App.Windows.Startup;

namespace Reminder.App.UI.ViewModels;

public sealed record GlobalPauseChoice(
    ReminderGlobalPauseDuration Value,
    string Label);

public sealed record RenderingModeChoice(
    ReminderRenderingMode Value,
    string Label);

public sealed record ThemeModeChoice(
    ReminderThemeMode Value,
    string Label);

public sealed record SnoozeOverflowPolicyChoice(
    ReminderSnoozeOverflowPolicy Value,
    string Label);

public sealed record NotificationDisplayDurationChoice(
    ReminderNotificationDisplayDuration Value,
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

    private static readonly IReadOnlyList<RenderingModeChoice>
        RenderingModeChoiceValues =
    [
        new(
            ReminderRenderingMode.SoftwareCompatibility,
            "软件渲染（兼容模式）"),
        new(
            ReminderRenderingMode.HardwarePreferred,
            "硬件优先渲染")
    ];

    private static readonly IReadOnlyList<ThemeModeChoice>
        ThemeModeChoiceValues =
    [
        new(ReminderThemeMode.FollowSystem, "跟随系统"),
        new(ReminderThemeMode.Light, "浅色"),
        new(ReminderThemeMode.Dark, "深色")
    ];

    private static readonly IReadOnlyList<SnoozeOverflowPolicyChoice>
        SnoozeOverflowPolicyChoiceValues =
    [
        new(
            ReminderSnoozeOverflowPolicy.ShortenToFixedInterval,
            "缩短为事件提醒间隔"),
        new(
            ReminderSnoozeOverflowPolicy.UseUnifiedDuration,
            "仍使用统一延迟时间")
    ];

    private static readonly
        IReadOnlyList<NotificationDisplayDurationChoice>
        NotificationDisplayDurationChoiceValues =
    [
        new(
            ReminderNotificationDisplayDuration.Short,
            "较短（Windows 默认，约 7 秒）"),
        new(
            ReminderNotificationDisplayDuration.Long,
            "较长（约 25 秒）")
    ];

    private readonly ReminderEngine _engine;
    private readonly Dispatcher _dispatcher;
    private readonly ReminderApplicationSettingsService _settings;
    private readonly IWindowsStartupRegistrationService
        _startupRegistration;
    private bool _disposed;
    private int _activeEventCount;
    private bool _isGlobalPaused;
    private bool _showGlobalPauseCountdown;
    private string _globalPauseRemainingText = string.Empty;
    private GlobalPauseChoice _selectedGlobalPauseChoice =
        GlobalPauseChoiceValues[0];
    private bool _synchronizingGlobalPause;
    private ThemeModeChoice _selectedThemeModeChoice;
    private RenderingModeChoice _selectedRenderingModeChoice;
    private bool _isStartWithWindows;
    private bool _isSilentStart;
    private string _startupRegistrationError = string.Empty;
    private SnoozeOverflowPolicyChoice
        _selectedSnoozeOverflowPolicyChoice;
    private NotificationDisplayDurationChoice
        _selectedNotificationDisplayDurationChoice;
    private string _snoozeDurationDaysInput;
    private string _snoozeDurationHoursInput;
    private string _snoozeDurationMinutesInput;
    private string _snoozeDurationError = string.Empty;
    private bool _showSnoozeDurationDays;
    private bool _showSnoozeDurationHours;
    private bool _snoozeDurationInputDirty;
    private bool _synchronizingSettings;
    private string _searchText = string.Empty;
    private HomeReminderViewModel? _selectedHomeEvent;
    private Guid? _previewedHomeEventId;

    public MainViewModel(
        ReminderEngine engine,
        Dispatcher dispatcher,
        ReminderApplicationSettingsService settings,
        IWindowsStartupRegistrationService startupRegistration)
    {
        _engine = engine;
        _dispatcher = dispatcher;
        _settings = settings;
        _startupRegistration = startupRegistration;
        _selectedThemeModeChoice =
            FindThemeModeChoice(settings.ThemeMode);
        _selectedRenderingModeChoice =
            FindRenderingModeChoice(settings.RenderingMode);
        _isStartWithWindows = settings.StartWithWindows;
        _isSilentStart = settings.SilentStart;
        _selectedSnoozeOverflowPolicyChoice =
            FindSnoozeOverflowPolicyChoice(
                settings.SnoozeOverflowPolicy);
        _selectedNotificationDisplayDurationChoice =
            FindNotificationDisplayDurationChoice(
                settings.NotificationDisplayDuration);
        var snoozeParts = SplitDuration(
            settings.SnoozeDurationMinutes);
        _snoozeDurationDaysInput =
            snoozeParts.Days.ToString();
        _snoozeDurationHoursInput =
            snoozeParts.Hours.ToString();
        _snoozeDurationMinutesInput =
            snoozeParts.Minutes.ToString();
        _showSnoozeDurationDays = snoozeParts.ShowDays;
        _showSnoozeDurationHours = snoozeParts.ShowHours;
        _engine.StateChanged += OnEngineStateChanged;
        ReconcileStartupRegistration();

        AddEventCommand = new RelayCommand(AddEvent);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        ClearSearchHistoryCommand =
            new RelayCommand(ClearSearchHistory);
        ToggleGlobalPauseCommand =
            new RelayCommand(ToggleGlobalPause);
        RestartAllCommand = new RelayCommand(_engine.RestartAll);

        Refresh();
        SynchronizeSearchHistory();
    }

    public MainViewModel(
        ReminderEngine engine,
        Dispatcher dispatcher,
        ReminderApplicationSettingsService settings)
        : this(
            engine,
            dispatcher,
            settings,
            new NullWindowsStartupRegistrationService())
    {
    }

    public MainViewModel(
        ReminderEngine engine,
        Dispatcher dispatcher)
        : this(
            engine,
            dispatcher,
            new ReminderApplicationSettingsService())
    {
    }

    public string AppName => AppMetadata.Name;

    public string VersionText => $"版本 {AppMetadata.Version}";

    public string NotificationStatus => _engine.NotificationStatus;

    public string NotificationStatusHelp => _engine.NotificationStatusHelp;

    public bool NotificationsAvailable => _engine.NotificationsAvailable;

    public event Action<Guid>? EventAdded;

    public event Action<EventViewModel>? DeleteRequested;

    public event Action<ReminderRenderingMode>? RenderingModeChangeRequested;

    public event Action<IReadOnlyCollection<Guid>>?
        HomePresentationChanged;

    public ObservableCollection<EventViewModel> Events { get; } = [];

    public ObservableCollection<EventViewModel> FilteredEvents { get; } = [];

    public ObservableCollection<string> SearchHistory { get; } = [];

    public ObservableCollection<HomeReminderViewModel> HomeEvents { get; } = [];

    public RelayCommand AddEventCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    public RelayCommand ClearSearchHistoryCommand { get; }

    public RelayCommand ToggleGlobalPauseCommand { get; }

    public RelayCommand RestartAllCommand { get; }

    public IReadOnlyList<GlobalPauseChoice> GlobalPauseChoices =>
        GlobalPauseChoiceValues;

    public IReadOnlyList<RenderingModeChoice> RenderingModeChoices =>
        RenderingModeChoiceValues;

    public IReadOnlyList<ThemeModeChoice> ThemeModeChoices =>
        ThemeModeChoiceValues;

    public IReadOnlyList<SnoozeOverflowPolicyChoice>
        SnoozeOverflowPolicyChoices =>
        SnoozeOverflowPolicyChoiceValues;

    public IReadOnlyList<NotificationDisplayDurationChoice>
        NotificationDisplayDurationChoices =>
        NotificationDisplayDurationChoiceValues;

    public int EventCount => Events.Count;

    public int ActiveEventCount
    {
        get => _activeEventCount;
        private set => SetProperty(ref _activeEventCount, value);
    }

    public string EventSummary => $"共 {EventCount} 个事件 · {ActiveEventCount} 个运行中";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSearchText));
            RefreshFilteredEvents();
        }
    }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    public bool HasSearchHistory => SearchHistory.Count > 0;

    public bool ShowNoSearchResults =>
        HasSearchText && FilteredEvents.Count == 0;

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

    public RenderingModeChoice SelectedRenderingModeChoice
    {
        get => _selectedRenderingModeChoice;
        set
        {
            if (value is null ||
                !SetProperty(ref _selectedRenderingModeChoice, value) ||
                _synchronizingSettings)
            {
                return;
            }

            if (_settings.SetRenderingMode(value.Value))
            {
                RenderingModeChangeRequested?.Invoke(value.Value);
            }
        }
    }

    public ThemeModeChoice SelectedThemeModeChoice
    {
        get => _selectedThemeModeChoice;
        set
        {
            if (value is null ||
                !SetProperty(ref _selectedThemeModeChoice, value) ||
                _synchronizingSettings)
            {
                return;
            }

            _settings.SetThemeMode(value.Value);
        }
    }

    public bool IsStartWithWindows
    {
        get => _isStartWithWindows;
        set
        {
            if (_synchronizingSettings)
            {
                SetProperty(ref _isStartWithWindows, value);
                return;
            }

            if (_isStartWithWindows == value)
            {
                return;
            }

            if (!_startupRegistration.TrySetEnabled(
                    value,
                    out var errorMessage))
            {
                StartupRegistrationError =
                    $"无法更改开机自动启动：{errorMessage}";
                OnPropertyChanged();
                return;
            }

            StartupRegistrationError = string.Empty;
            SetProperty(ref _isStartWithWindows, value);
            _settings.SetStartWithWindows(value);
        }
    }

    public bool IsSilentStart
    {
        get => _isSilentStart;
        set
        {
            if (!SetProperty(ref _isSilentStart, value) ||
                _synchronizingSettings)
            {
                return;
            }

            _settings.SetSilentStart(value);
        }
    }

    public string StartupRegistrationError
    {
        get => _startupRegistrationError;
        private set
        {
            if (SetProperty(ref _startupRegistrationError, value))
            {
                OnPropertyChanged(nameof(HasStartupRegistrationError));
            }
        }
    }

    public bool HasStartupRegistrationError =>
        StartupRegistrationError.Length != 0;

    public SnoozeOverflowPolicyChoice SelectedSnoozeOverflowPolicyChoice
    {
        get => _selectedSnoozeOverflowPolicyChoice;
        set
        {
            if (value is null ||
                !SetProperty(
                    ref _selectedSnoozeOverflowPolicyChoice,
                    value) ||
                _synchronizingSettings)
            {
                return;
            }

            _settings.SetSnoozeOverflowPolicy(value.Value);
        }
    }

    public NotificationDisplayDurationChoice
        SelectedNotificationDisplayDurationChoice
    {
        get => _selectedNotificationDisplayDurationChoice;
        set
        {
            if (value is null ||
                !SetProperty(
                    ref _selectedNotificationDisplayDurationChoice,
                    value) ||
                _synchronizingSettings)
            {
                return;
            }

            _settings.SetNotificationDisplayDuration(value.Value);
        }
    }

    public string SnoozeDurationDaysInput
    {
        get => _snoozeDurationDaysInput;
        set
        {
            if (SetProperty(
                    ref _snoozeDurationDaysInput,
                    value ?? string.Empty))
            {
                MarkSnoozeDurationInputDirty();
            }
        }
    }

    public string SnoozeDurationHoursInput
    {
        get => _snoozeDurationHoursInput;
        set
        {
            if (SetProperty(
                    ref _snoozeDurationHoursInput,
                    value ?? string.Empty))
            {
                MarkSnoozeDurationInputDirty();
            }
        }
    }

    public string SnoozeDurationMinutesInput
    {
        get => _snoozeDurationMinutesInput;
        set
        {
            if (SetProperty(
                    ref _snoozeDurationMinutesInput,
                    value ?? string.Empty))
            {
                MarkSnoozeDurationInputDirty();
            }
        }
    }

    public bool ShowSnoozeDurationDays
    {
        get => _showSnoozeDurationDays;
        private set => SetProperty(ref _showSnoozeDurationDays, value);
    }

    public bool ShowSnoozeDurationHours
    {
        get => _showSnoozeDurationHours;
        private set => SetProperty(ref _showSnoozeDurationHours, value);
    }

    public string SnoozeDurationError
    {
        get => _snoozeDurationError;
        private set
        {
            if (SetProperty(ref _snoozeDurationError, value))
            {
                OnPropertyChanged(nameof(HasSnoozeDurationError));
            }
        }
    }

    public bool HasSnoozeDurationError =>
        SnoozeDurationError.Length != 0;

    public HomeReminderViewModel? SelectedHomeEvent
    {
        get => _selectedHomeEvent;
        private set => SetProperty(ref _selectedHomeEvent, value);
    }

    public HomeReminderViewModel? HomeTopEvent =>
        HomeEvents.Count switch
        {
            2 or >= 3 => HomeEvents[0],
            _ => null
        };

    public HomeReminderViewModel? HomeMiddleEvent =>
        HomeEvents.Count switch
        {
            1 => HomeEvents[0],
            >= 3 => HomeEvents[1],
            _ => null
        };

    public HomeReminderViewModel? HomeBottomEvent =>
        HomeEvents.Count switch
        {
            2 => HomeEvents[1],
            >= 3 => HomeEvents[2],
            _ => null
        };

    public bool HasHomeEvents => HomeEvents.Count > 0;

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
                Events[index].PropertyChanged -=
                    OnEventViewModelPropertyChanged;
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
                viewModel.PropertyChanged +=
                    OnEventViewModelPropertyChanged;
                Events.Add(viewModel);
            }
            else
            {
                viewModel.ApplySnapshot(snapshot);
            }
        }

        RefreshFilteredEvents();
        UpdateHomeEvents(snapshots);
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
        foreach (var eventViewModel in Events)
        {
            eventViewModel.PropertyChanged -=
                OnEventViewModelPropertyChanged;
        }
    }

    private void AddEvent()
    {
        ClearSearch();
        var eventId = _engine.AddDefaultEvent();
        Refresh();
        EventAdded?.Invoke(eventId);
    }

    public void CommitSearch()
    {
        var normalized =
            ReminderApplicationSettingsService.NormalizeSearchQuery(
                SearchText);
        if (normalized.Length == 0)
        {
            return;
        }

        SearchText = normalized;
        _settings.RecordSearchQuery(normalized);
        SynchronizeSearchHistory();
    }

    public void SelectSearchHistory(string query)
    {
        SearchText =
            ReminderApplicationSettingsService.NormalizeSearchQuery(query);
        CommitSearch();
    }

    public void RemoveSearchHistory(string query)
    {
        _settings.RemoveSearchQuery(query);
        SynchronizeSearchHistory();
    }

    public void ClearSearch()
    {
        SearchText = string.Empty;
    }

    public void ClearSearchHistory()
    {
        _settings.ClearSearchHistory();
        SynchronizeSearchHistory();
    }

    public void CommitSnoozeDuration()
    {
        if (!_snoozeDurationInputDirty)
        {
            return;
        }

        if (!ReminderInputValidator.TryValidateIntervalParts(
                SnoozeDurationDaysInput,
                SnoozeDurationHoursInput,
                SnoozeDurationMinutesInput,
                out var minutes,
                out var error))
        {
            SnoozeDurationError = error;
            return;
        }

        _settings.SetSnoozeDurationMinutes(minutes);
        SnoozeDurationError = string.Empty;
        ApplyCanonicalSnoozeDuration(minutes);
    }

    public bool RestoreDefaultSettings()
    {
        var previousRenderingMode = _settings.RenderingMode;
        var wasStartWithWindows = _settings.StartWithWindows;
        var startupDisabled = _startupRegistration.TrySetEnabled(
            enabled: false,
            out var startupError);
        var changed = _settings.RestoreDefaults();
        if (!startupDisabled && wasStartWithWindows)
        {
            _settings.SetStartWithWindows(true);
            StartupRegistrationError =
                $"无法关闭开机自动启动：{startupError}";
        }
        else
        {
            StartupRegistrationError = string.Empty;
        }

        SynchronizeSettings();
        if (previousRenderingMode != _settings.RenderingMode)
        {
            RenderingModeChangeRequested?.Invoke(
                _settings.RenderingMode);
        }

        return changed;
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

    public void PreviewHomeEvent(Guid? eventId)
    {
        _previewedHomeEventId = eventId;
        SelectedHomeEvent =
            eventId is null
                ? HomeEvents.FirstOrDefault()
                : HomeEvents.FirstOrDefault(item => item.Id == eventId) ??
                  HomeEvents.FirstOrDefault();
    }

    private void OnEngineStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(Refresh, DispatcherPriority.DataBind);
    }

    private void OnEventViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EventViewModel.Name))
        {
            RefreshFilteredEvents();
        }
    }

    private void RefreshFilteredEvents()
    {
        var tokens = SearchText.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        var desired = tokens.Length == 0
            ? Events.ToArray()
            : Events.Where(item =>
                    tokens.All(token =>
                        item.Name.Contains(
                            token,
                            StringComparison.OrdinalIgnoreCase)))
                .ToArray();

        for (var targetIndex = 0;
             targetIndex < desired.Length;
             targetIndex++)
        {
            var item = desired[targetIndex];
            var currentIndex = FilteredEvents.IndexOf(item);
            if (currentIndex < 0)
            {
                FilteredEvents.Insert(targetIndex, item);
            }
            else if (currentIndex != targetIndex)
            {
                FilteredEvents.Move(currentIndex, targetIndex);
            }
        }

        var desiredSet = desired.ToHashSet();
        for (var index = FilteredEvents.Count - 1;
             index >= 0;
             index--)
        {
            if (!desiredSet.Contains(FilteredEvents[index]))
            {
                FilteredEvents.RemoveAt(index);
            }
        }

        OnPropertyChanged(nameof(ShowNoSearchResults));
    }

    private void SynchronizeSearchHistory()
    {
        var desired = _settings.SearchHistory;
        if (SearchHistory.SequenceEqual(desired))
        {
            return;
        }

        SearchHistory.Clear();
        foreach (var query in desired)
        {
            SearchHistory.Add(query);
        }

        OnPropertyChanged(nameof(HasSearchHistory));
    }

    private void SynchronizeSettings()
    {
        _synchronizingSettings = true;
        try
        {
            SelectedThemeModeChoice =
                FindThemeModeChoice(_settings.ThemeMode);
            SelectedRenderingModeChoice =
                FindRenderingModeChoice(_settings.RenderingMode);
            IsStartWithWindows = _settings.StartWithWindows;
            IsSilentStart = _settings.SilentStart;
            SelectedSnoozeOverflowPolicyChoice =
                FindSnoozeOverflowPolicyChoice(
                    _settings.SnoozeOverflowPolicy);
            SelectedNotificationDisplayDurationChoice =
                FindNotificationDisplayDurationChoice(
                    _settings.NotificationDisplayDuration);
            ApplyCanonicalSnoozeDuration(
                _settings.SnoozeDurationMinutes);
        }
        finally
        {
            _synchronizingSettings = false;
        }
    }

    private void MarkSnoozeDurationInputDirty()
    {
        if (!_synchronizingSettings)
        {
            _snoozeDurationInputDirty = true;
        }

        if (SnoozeDurationError.Length != 0)
        {
            SnoozeDurationError = string.Empty;
        }
    }

    private void ApplyCanonicalSnoozeDuration(int totalMinutes)
    {
        var parts = SplitDuration(totalMinutes);
        SnoozeDurationDaysInput = parts.Days.ToString();
        SnoozeDurationHoursInput = parts.Hours.ToString();
        SnoozeDurationMinutesInput = parts.Minutes.ToString();
        ShowSnoozeDurationDays = parts.ShowDays;
        ShowSnoozeDurationHours = parts.ShowHours;
        _snoozeDurationInputDirty = false;
    }

    private void UpdateHomeEvents(
        IReadOnlyList<ReminderEventSnapshot> snapshots)
    {
        var oldPresentation = HomeEvents
            .Select((item, index) => new
            {
                item.Id,
                item.Name,
                item.StatusText,
                index
            })
            .ToDictionary(item => item.Id);
        var oldSelectedEventId = SelectedHomeEvent?.Id;
        var oldCount = HomeEvents.Count;
        var desired = snapshots
            .Select((snapshot, index) => new { snapshot, index })
            .Where(item =>
                item.snapshot.IsEnabled &&
                !item.snapshot.IsExpired &&
                item.snapshot.Remaining is not null &&
                (item.snapshot.EventType == ReminderEventType.ScheduledTime ||
                 !item.snapshot.IsPaused))
            .OrderBy(item => item.snapshot.IsAwaitingAction ? 0 : 1)
            .ThenBy(item => item.snapshot.Remaining)
            .ThenBy(item => item.index)
            .Take(3)
            .Select(item => item.snapshot)
            .ToArray();

        for (var targetIndex = 0;
             targetIndex < desired.Length;
             targetIndex++)
        {
            var snapshot = desired[targetIndex];
            var currentIndex = IndexOfHomeEvent(snapshot.Id);
            HomeReminderViewModel viewModel;
            if (currentIndex < 0)
            {
                viewModel = new HomeReminderViewModel(snapshot);
                HomeEvents.Insert(targetIndex, viewModel);
            }
            else
            {
                viewModel = HomeEvents[currentIndex];
                viewModel.ApplySnapshot(snapshot);
                if (currentIndex != targetIndex)
                {
                    HomeEvents.Move(currentIndex, targetIndex);
                }
            }
        }

        var desiredIds = desired.Select(item => item.Id).ToHashSet();
        for (var index = HomeEvents.Count - 1; index >= 0; index--)
        {
            if (!desiredIds.Contains(HomeEvents[index].Id))
            {
                HomeEvents.RemoveAt(index);
            }
        }

        OnPropertyChanged(nameof(HomeTopEvent));
        OnPropertyChanged(nameof(HomeMiddleEvent));
        OnPropertyChanged(nameof(HomeBottomEvent));
        OnPropertyChanged(nameof(HasHomeEvents));
        PreviewHomeEvent(_previewedHomeEventId);

        var changedEventIds = HomeEvents
            .Select((item, index) => new { item, index })
            .Where(item =>
                oldCount != HomeEvents.Count ||
                !oldPresentation.TryGetValue(
                    item.item.Id,
                    out var oldItem) ||
                oldItem.index != item.index ||
                oldItem.Name != item.item.Name ||
                oldItem.StatusText != item.item.StatusText)
            .Select(item => item.item.Id)
            .ToArray();
        if (changedEventIds.Length > 0 ||
            oldCount != HomeEvents.Count ||
            oldSelectedEventId != SelectedHomeEvent?.Id)
        {
            HomePresentationChanged?.Invoke(changedEventIds);
        }
    }

    private int IndexOfHomeEvent(Guid id)
    {
        for (var index = 0; index < HomeEvents.Count; index++)
        {
            if (HomeEvents[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private static RenderingModeChoice FindRenderingModeChoice(
        ReminderRenderingMode mode)
    {
        return RenderingModeChoiceValues.First(item => item.Value == mode);
    }

    private static ThemeModeChoice FindThemeModeChoice(
        ReminderThemeMode mode)
    {
        return ThemeModeChoiceValues.First(item => item.Value == mode);
    }

    private void ReconcileStartupRegistration()
    {
        if (_startupRegistration.TrySetEnabled(
                _settings.StartWithWindows,
                out var errorMessage))
        {
            StartupRegistrationError = string.Empty;
            return;
        }

        StartupRegistrationError =
            $"无法同步开机自动启动：{errorMessage}";
    }

    private static SnoozeOverflowPolicyChoice
        FindSnoozeOverflowPolicyChoice(
            ReminderSnoozeOverflowPolicy policy)
    {
        return SnoozeOverflowPolicyChoiceValues.First(
            item => item.Value == policy);
    }

    private static NotificationDisplayDurationChoice
        FindNotificationDisplayDurationChoice(
            ReminderNotificationDisplayDuration duration)
    {
        return NotificationDisplayDurationChoiceValues.First(
            item => item.Value == duration);
    }

    private static DurationParts SplitDuration(int totalMinutes)
    {
        var days = totalMinutes / (24 * 60);
        var hours = totalMinutes % (24 * 60) / 60;
        var minutes = totalMinutes % 60;
        return new DurationParts(
            days,
            hours,
            totalMinutes < 60 ? totalMinutes : minutes,
            ShowDays: totalMinutes >= 24 * 60,
            ShowHours: totalMinutes >= 60);
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

    private readonly record struct DurationParts(
        int Days,
        int Hours,
        int Minutes,
        bool ShowDays,
        bool ShowHours);
}
