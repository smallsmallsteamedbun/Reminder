using System.Globalization;
using Reminder.App.Logic.Models;
using Reminder.App.Logic.Services;

namespace Reminder.App.UI.ViewModels;

public sealed record RecurrenceChoice(
    ReminderRecurrence Value,
    string Label);

public sealed record WeekdayChoice(
    DayOfWeek Value,
    string Label);

public sealed record TerminationChoice(
    int Value,
    string Label);

public sealed class EventViewModel : ObservableObject
{
    private static readonly IReadOnlyList<RecurrenceChoice> RecurrenceChoiceValues =
    [
        new(ReminderRecurrence.Once, "仅一次"),
        new(ReminderRecurrence.Daily, "每天"),
        new(ReminderRecurrence.Weekly, "每周"),
        new(ReminderRecurrence.Monthly, "每月"),
        new(ReminderRecurrence.Yearly, "每年")
    ];

    private static readonly IReadOnlyList<WeekdayChoice> WeekdayChoiceValues =
    [
        new(DayOfWeek.Monday, "星期一"),
        new(DayOfWeek.Tuesday, "星期二"),
        new(DayOfWeek.Wednesday, "星期三"),
        new(DayOfWeek.Thursday, "星期四"),
        new(DayOfWeek.Friday, "星期五"),
        new(DayOfWeek.Saturday, "星期六"),
        new(DayOfWeek.Sunday, "星期日")
    ];

    private static readonly IReadOnlyList<TerminationChoice> TerminationChoiceValues =
        Enumerable.Range(0, 101)
            .Select(value => new TerminationChoice(
                value,
                value == 0 ? "不终止" : $"{value} 次"))
            .ToArray();

    private static readonly IReadOnlyList<string> FixedSystemPolicyChoiceValues =
    [
        "继续计时",
        "重新计时",
        "暂停计时"
    ];

    private static readonly IReadOnlyList<string> SystemNotificationChoiceValues =
    [
        "提醒",
        "不提醒"
    ];

    private readonly ReminderEngine _engine;
    private ReminderEventSnapshot _snapshot;
    private string _nameInput;
    private string _intervalInput;
    private string _yearInput;
    private string _monthInput;
    private string _dayInput;
    private string _hourInput;
    private string _minuteInput;
    private string _nameError = string.Empty;
    private string _intervalError = string.Empty;
    private string _scheduleError = string.Empty;
    private string _countdownText = string.Empty;
    private string _statusText = string.Empty;
    private string _scheduleSummary = string.Empty;
    private bool _isEnabled;
    private bool _isPaused;
    private bool _isAwaitingAction;
    private bool _canRestart;
    private bool _isHighlighted;
    private bool _isScheduledTime;
    private RecurrenceChoice _selectedRecurrenceChoice;
    private WeekdayChoice _selectedWeekdayChoice;
    private TerminationChoice _selectedTerminationChoice;
    private string _selectedFixedSystemPolicy = "暂停计时";
    private string _selectedFixedSystemNotification = "不提醒";
    private string _selectedScheduledSystemNotification = "不提醒";
    private bool _synchronizing;

    public EventViewModel(
        ReminderEngine engine,
        ReminderEventSnapshot snapshot,
        Action<EventViewModel> deleteRequested)
    {
        _engine = engine;
        _snapshot = snapshot;
        _nameInput = snapshot.Name;
        _intervalInput = FormatInteger(snapshot.IntervalMinutes);
        _yearInput = FormatInteger(GetYear(snapshot));
        _monthInput = FormatInteger(GetMonth(snapshot));
        _dayInput = FormatInteger(GetDay(snapshot));
        _hourInput = FormatClockPart(GetHour(snapshot));
        _minuteInput = FormatClockPart(GetMinute(snapshot));
        _isScheduledTime =
            snapshot.EventType == ReminderEventType.ScheduledTime;
        _selectedRecurrenceChoice =
            FindRecurrenceChoice(snapshot.ScheduledTime.Recurrence);
        _selectedWeekdayChoice =
            FindWeekdayChoice(snapshot.ScheduledTime.DayOfWeek);
        _selectedTerminationChoice =
            FindTerminationChoice(snapshot.RemainingOccurrences);

        PauseCommand = new RelayCommand(
            () => _engine.TogglePause(Id),
            () => CanPauseOrResume);
        RestartCommand = new RelayCommand(
            () => _engine.Restart(Id),
            () => CanRestart);
        DeleteCommand = new RelayCommand(() => deleteRequested(this));

        ApplySnapshot(snapshot);
    }

    public Guid Id => _snapshot.Id;

    public RelayCommand PauseCommand { get; }

    public RelayCommand RestartCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public IReadOnlyList<RecurrenceChoice> RecurrenceChoices =>
        RecurrenceChoiceValues;

    public IReadOnlyList<WeekdayChoice> WeekdayChoices =>
        WeekdayChoiceValues;

    public IReadOnlyList<TerminationChoice> TerminationChoices =>
        TerminationChoiceValues;

    public IReadOnlyList<string> FixedSystemPolicyChoices =>
        FixedSystemPolicyChoiceValues;

    public IReadOnlyList<string> SystemNotificationChoices =>
        SystemNotificationChoiceValues;

    public string NameInput
    {
        get => _nameInput;
        set
        {
            if (SetProperty(ref _nameInput, value) && NameError.Length != 0)
            {
                NameError = string.Empty;
            }
        }
    }

    public string IntervalInput
    {
        get => _intervalInput;
        set
        {
            if (SetProperty(ref _intervalInput, value) && IntervalError.Length != 0)
            {
                IntervalError = string.Empty;
            }
        }
    }

    public string YearInput
    {
        get => _yearInput;
        set
        {
            if (SetProperty(ref _yearInput, value))
            {
                ClearScheduleError();
            }
        }
    }

    public string MonthInput
    {
        get => _monthInput;
        set
        {
            if (SetProperty(ref _monthInput, value))
            {
                ClearScheduleError();
            }
        }
    }

    public string DayInput
    {
        get => _dayInput;
        set
        {
            if (SetProperty(ref _dayInput, value))
            {
                ClearScheduleError();
            }
        }
    }

    public string HourInput
    {
        get => _hourInput;
        set
        {
            if (SetProperty(ref _hourInput, value))
            {
                ClearScheduleError();
            }
        }
    }

    public string MinuteInput
    {
        get => _minuteInput;
        set
        {
            if (SetProperty(ref _minuteInput, value))
            {
                ClearScheduleError();
            }
        }
    }

    public string NameError
    {
        get => _nameError;
        private set
        {
            if (SetProperty(ref _nameError, value))
            {
                OnPropertyChanged(nameof(HasNameError));
            }
        }
    }

    public bool HasNameError => NameError.Length != 0;

    public string IntervalError
    {
        get => _intervalError;
        private set
        {
            if (SetProperty(ref _intervalError, value))
            {
                OnPropertyChanged(nameof(HasIntervalError));
            }
        }
    }

    public bool HasIntervalError => IntervalError.Length != 0;

    public string ScheduleError
    {
        get => _scheduleError;
        private set
        {
            if (SetProperty(ref _scheduleError, value))
            {
                OnPropertyChanged(nameof(HasScheduleError));
            }
        }
    }

    public bool HasScheduleError => ScheduleError.Length != 0;

    public string CountdownText
    {
        get => _countdownText;
        private set => SetProperty(ref _countdownText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ScheduleSummary
    {
        get => _scheduleSummary;
        private set => SetProperty(ref _scheduleSummary, value);
    }

    public string PauseButtonText => IsPaused ? "恢复" : "暂停";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetProperty(ref _isEnabled, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CardOpacity));
            OnPropertyChanged(nameof(CanPauseOrResume));
            OnPropertyChanged(nameof(CountdownLabel));
            PauseCommand.RaiseCanExecuteChanged();

            if (!_synchronizing)
            {
                _engine.SetEnabled(Id, value);
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
                OnPropertyChanged(nameof(CardOpacity));
            }
        }
    }

    public bool IsAwaitingAction
    {
        get => _isAwaitingAction;
        private set => SetProperty(ref _isAwaitingAction, value);
    }

    public bool CanRestart
    {
        get => _canRestart;
        private set
        {
            if (SetProperty(ref _canRestart, value))
            {
                RestartCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanPauseOrResume => IsEnabled && IsFixedInterval;

    public bool IsHighlighted
    {
        get => _isHighlighted;
        private set => SetProperty(ref _isHighlighted, value);
    }

    public bool IsScheduledTime
    {
        get => _isScheduledTime;
        set
        {
            if (!SetProperty(ref _isScheduledTime, value))
            {
                return;
            }

            RaiseScheduleVisibilityChanged();
            OnPropertyChanged(nameof(CanPauseOrResume));
            OnPropertyChanged(nameof(CountdownLabel));
            PauseCommand.RaiseCanExecuteChanged();
            if (!_synchronizing)
            {
                _engine.UpdateEventType(
                    Id,
                    value
                        ? ReminderEventType.ScheduledTime
                        : ReminderEventType.FixedInterval);
            }
        }
    }

    public bool IsFixedInterval
    {
        get => !IsScheduledTime;
        set
        {
            if (value)
            {
                IsScheduledTime = false;
            }
        }
    }

    public bool ShowOneTimeFields =>
        IsScheduledTime &&
        SelectedRecurrenceChoice.Value == ReminderRecurrence.Once;

    public bool ShowWeeklyFields =>
        IsScheduledTime &&
        SelectedRecurrenceChoice.Value == ReminderRecurrence.Weekly;

    public bool ShowYearField => ShowOneTimeFields;

    public bool ShowMonthField =>
        IsScheduledTime &&
        SelectedRecurrenceChoice.Value is
            ReminderRecurrence.Once or ReminderRecurrence.Yearly;

    public bool ShowDayField =>
        IsScheduledTime &&
        SelectedRecurrenceChoice.Value is
            ReminderRecurrence.Once or
            ReminderRecurrence.Monthly or
            ReminderRecurrence.Yearly;

    public bool ShowTimeFields => IsScheduledTime;

    public bool ShowPauseButton => IsFixedInterval;

    public bool ShowRestartButton => IsFixedInterval;

    public int CountdownColumn => IsScheduledTime ? 1 : 3;

    public int CountdownColumnSpan => IsScheduledTime ? 3 : 1;

    public bool ShowTermination =>
        IsFixedInterval ||
        SelectedRecurrenceChoice.Value != ReminderRecurrence.Once;

    public string CountdownLabel =>
        IsScheduledTime && !IsEnabled
            ? "距离下次发生但不提醒"
            : "距离下次提醒";

    public RecurrenceChoice SelectedRecurrenceChoice
    {
        get => _selectedRecurrenceChoice;
        set
        {
            if (value is null ||
                !SetProperty(ref _selectedRecurrenceChoice, value))
            {
                return;
            }

            RaiseScheduleVisibilityChanged();
            ClearScheduleError();
            if (!_synchronizing)
            {
                _engine.UpdateRecurrence(Id, value.Value);
            }
        }
    }

    public WeekdayChoice SelectedWeekdayChoice
    {
        get => _selectedWeekdayChoice;
        set
        {
            if (value is null ||
                !SetProperty(ref _selectedWeekdayChoice, value))
            {
                return;
            }

            ClearScheduleError();
            if (!_synchronizing &&
                !_engine.UpdateDayOfWeek(Id, value.Value))
            {
                ScheduleError = "无法应用星期设置";
            }
        }
    }

    public TerminationChoice SelectedTerminationChoice
    {
        get => _selectedTerminationChoice;
        set
        {
            if (value is null ||
                !SetProperty(ref _selectedTerminationChoice, value))
            {
                return;
            }

            if (!_synchronizing &&
                !_engine.UpdateTermination(
                    Id,
                    value.Value == 0 ? null : value.Value))
            {
                ScheduleError = "无法应用终止次数";
            }
        }
    }

    public string SelectedFixedSystemPolicy
    {
        get => _selectedFixedSystemPolicy;
        set
        {
            if (SetProperty(ref _selectedFixedSystemPolicy, value))
            {
                OnPropertyChanged(
                    nameof(ShowFixedSystemNotificationChoice));
            }
        }
    }

    public string SelectedFixedSystemNotification
    {
        get => _selectedFixedSystemNotification;
        set => SetProperty(ref _selectedFixedSystemNotification, value);
    }

    public string SelectedScheduledSystemNotification
    {
        get => _selectedScheduledSystemNotification;
        set => SetProperty(ref _selectedScheduledSystemNotification, value);
    }

    public bool ShowFixedSystemNotificationChoice =>
        IsFixedInterval &&
        string.Equals(
            SelectedFixedSystemPolicy,
            "继续计时",
            StringComparison.Ordinal);

    public double CardOpacity => IsEnabled && !IsPaused ? 1.0 : 0.64;

    public void SetHighlighted(bool isHighlighted)
    {
        IsHighlighted = isHighlighted;
    }

    public void CommitName()
    {
        if (!ReminderInputValidator.TryValidateName(NameInput, out var value, out var error))
        {
            NameError = error;
            return;
        }

        if (!_engine.UpdateName(Id, value))
        {
            NameError = "无法应用事件名称";
            return;
        }

        NameError = string.Empty;
        NameInput = value;
    }

    public void CommitInterval()
    {
        if (!ReminderInputValidator.TryValidateInterval(
                IntervalInput,
                out var value,
                out var error))
        {
            IntervalError = error;
            return;
        }

        if (!_engine.UpdateInterval(Id, value))
        {
            IntervalError = "无法应用提醒间隔";
            return;
        }

        IntervalError = string.Empty;
        IntervalInput = FormatInteger(value);
    }

    public void CommitScheduleParts()
    {
        if (!ReminderInputValidator.TryValidateHour(
                HourInput,
                out var hour,
                out var error) ||
            !ReminderInputValidator.TryValidateMinute(
                MinuteInput,
                out var minute,
                out error))
        {
            ScheduleError = error;
            return;
        }

        var recurrence = SelectedRecurrenceChoice.Value;
        var updated = recurrence switch
        {
            ReminderRecurrence.Once =>
                CommitOneTimeParts(hour, minute, out error),
            ReminderRecurrence.Daily or ReminderRecurrence.Weekly =>
                _engine.UpdateTimeOfDay(Id, new TimeOnly(hour, minute)),
            ReminderRecurrence.Monthly =>
                CommitMonthlyParts(hour, minute, out error),
            ReminderRecurrence.Yearly =>
                CommitYearlyParts(hour, minute, out error),
            _ => false
        };

        if (!updated)
        {
            ScheduleError = error.Length == 0
                ? "无法应用指定时间设置"
                : error;
            return;
        }

        ScheduleError = string.Empty;
        NormalizeScheduleInputs();
    }

    private bool CommitOneTimeParts(
        int hour,
        int minute,
        out string error)
    {
        if (!ReminderInputValidator.TryValidateYear(
                YearInput,
                out var year,
                out error) ||
            !ReminderInputValidator.TryValidateMonth(
                MonthInput,
                out var month,
                out error) ||
            !ReminderInputValidator.TryValidateDayOfMonth(
                DayInput,
                out var day,
                out error) ||
            !ReminderInputValidator.TryCreateLocalDateTime(
                year,
                month,
                day,
                hour,
                minute,
                out var value,
                out error))
        {
            return false;
        }

        return _engine.UpdateOneTime(Id, value);
    }

    private bool CommitMonthlyParts(
        int hour,
        int minute,
        out string error)
    {
        if (!ReminderInputValidator.TryValidateDayOfMonth(
                DayInput,
                out var day,
                out error))
        {
            return false;
        }

        return _engine.UpdateMonthlySchedule(
            Id,
            day,
            new TimeOnly(hour, minute));
    }

    private bool CommitYearlyParts(
        int hour,
        int minute,
        out string error)
    {
        if (!ReminderInputValidator.TryValidateMonth(
                MonthInput,
                out var month,
                out error) ||
            !ReminderInputValidator.TryValidateDayOfMonth(
                DayInput,
                out var day,
                out error))
        {
            return false;
        }

        if (day > DateTime.DaysInMonth(2_000, month))
        {
            error = "当前月份不存在所选日期";
            return false;
        }

        return _engine.UpdateYearlySchedule(
            Id,
            month,
            day,
            new TimeOnly(hour, minute));
    }

    private void NormalizeScheduleInputs()
    {
        if (int.TryParse(YearInput, out var year))
        {
            YearInput = FormatInteger(year);
        }

        if (int.TryParse(MonthInput, out var month))
        {
            MonthInput = FormatInteger(month);
        }

        if (int.TryParse(DayInput, out var day))
        {
            DayInput = FormatInteger(day);
        }

        if (int.TryParse(HourInput, out var hour))
        {
            HourInput = FormatClockPart(hour);
        }

        if (int.TryParse(MinuteInput, out var minute))
        {
            MinuteInput = FormatClockPart(minute);
        }
    }

    public void ApplySnapshot(ReminderEventSnapshot snapshot)
    {
        var previousSnapshot = _snapshot;
        var nameInputWasUnmodified = NameInput == previousSnapshot.Name;
        var intervalInputWasUnmodified =
            IntervalInput == FormatInteger(previousSnapshot.IntervalMinutes);
        var yearInputWasUnmodified =
            YearInput == FormatInteger(GetYear(previousSnapshot));
        var monthInputWasUnmodified =
            MonthInput == FormatInteger(GetMonth(previousSnapshot));
        var dayInputWasUnmodified =
            DayInput == FormatInteger(GetDay(previousSnapshot));
        var hourInputWasUnmodified =
            HourInput == FormatClockPart(GetHour(previousSnapshot));
        var minuteInputWasUnmodified =
            MinuteInput == FormatClockPart(GetMinute(previousSnapshot));

        _snapshot = snapshot;
        _synchronizing = true;
        try
        {
            IsEnabled = snapshot.IsEnabled;
            IsPaused = snapshot.IsPaused;
            IsAwaitingAction = snapshot.IsAwaitingAction;
            CanRestart = snapshot.CanRestart;
            IsScheduledTime =
                snapshot.EventType == ReminderEventType.ScheduledTime;
            SelectedRecurrenceChoice =
                FindRecurrenceChoice(snapshot.ScheduledTime.Recurrence);
            SelectedWeekdayChoice =
                FindWeekdayChoice(snapshot.ScheduledTime.DayOfWeek);
            SelectedTerminationChoice =
                FindTerminationChoice(snapshot.RemainingOccurrences);

            if (nameInputWasUnmodified)
            {
                NameInput = snapshot.Name;
            }

            if (intervalInputWasUnmodified)
            {
                IntervalInput = FormatInteger(snapshot.IntervalMinutes);
            }

            if (yearInputWasUnmodified)
            {
                YearInput = FormatInteger(GetYear(snapshot));
            }

            if (monthInputWasUnmodified)
            {
                MonthInput = FormatInteger(GetMonth(snapshot));
            }

            if (dayInputWasUnmodified)
            {
                DayInput = FormatInteger(GetDay(snapshot));
            }

            if (hourInputWasUnmodified)
            {
                HourInput = FormatClockPart(GetHour(snapshot));
            }

            if (minuteInputWasUnmodified)
            {
                MinuteInput = FormatClockPart(GetMinute(snapshot));
            }
        }
        finally
        {
            _synchronizing = false;
        }

        CountdownText = snapshot.IsAwaitingAction
            ? "等待处理"
            : snapshot.Remaining is not null
                ? FormatCountdown(snapshot.Remaining.Value)
                : snapshot.ShowExpiredEasterEgg
                    ? "弹一万遍反方向的钟"
                    : string.Empty;

        StatusText = snapshot.IsExpired
            ? "已失效"
            : !snapshot.IsEnabled
                ? "已关闭"
                : snapshot.IsPaused
                    ? "已暂停"
                    : snapshot.IsAwaitingAction
                        ? "等待处理"
                        : "运行中";

        ScheduleSummary = CreateScheduleSummary(snapshot);

        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(CardOpacity));
        OnPropertyChanged(nameof(CanPauseOrResume));
        OnPropertyChanged(nameof(CountdownLabel));
        RaiseScheduleVisibilityChanged();
        PauseCommand.RaiseCanExecuteChanged();
    }

    private static string CreateScheduleSummary(
        ReminderEventSnapshot snapshot)
    {
        if (snapshot.EventType == ReminderEventType.FixedInterval)
        {
            return $"每 {snapshot.IntervalMinutes} 分钟";
        }

        var settings = snapshot.ScheduledTime;
        var time = FormatTime(settings.TimeOfDay);
        return settings.Recurrence switch
        {
            ReminderRecurrence.Once =>
                $"仅一次 · {FormatOneTime(settings.OneTimeAt)}",
            ReminderRecurrence.Daily =>
                $"每天 {time}",
            ReminderRecurrence.Weekly =>
                $"{FindWeekdayChoice(settings.DayOfWeek).Label} {time}",
            ReminderRecurrence.Monthly =>
                $"每月 {settings.DayOfMonth} 日 {time}",
            ReminderRecurrence.Yearly =>
                $"每年 {settings.MonthOfYear} 月 {settings.DayOfMonth} 日 {time}",
            _ => string.Empty
        };
    }

    private void RaiseScheduleVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsFixedInterval));
        OnPropertyChanged(nameof(ShowOneTimeFields));
        OnPropertyChanged(nameof(ShowWeeklyFields));
        OnPropertyChanged(nameof(ShowYearField));
        OnPropertyChanged(nameof(ShowMonthField));
        OnPropertyChanged(nameof(ShowDayField));
        OnPropertyChanged(nameof(ShowTimeFields));
        OnPropertyChanged(nameof(ShowPauseButton));
        OnPropertyChanged(nameof(ShowRestartButton));
        OnPropertyChanged(nameof(CountdownColumn));
        OnPropertyChanged(nameof(CountdownColumnSpan));
        OnPropertyChanged(nameof(ShowTermination));
        OnPropertyChanged(nameof(ShowFixedSystemNotificationChoice));
    }

    private void ClearScheduleError()
    {
        if (ScheduleError.Length != 0)
        {
            ScheduleError = string.Empty;
        }
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        var totalSeconds = Math.Max(0, (long)Math.Ceiling(remaining.TotalSeconds));
        var days = totalSeconds / 86_400;
        var hours = totalSeconds % 86_400 / 3_600;
        var minutes = totalSeconds % 3_600 / 60;
        var seconds = totalSeconds % 60;

        if (days > 0)
        {
            return $"{days}天{hours:00}时{minutes:00}分{seconds:00}秒";
        }

        if (hours > 0)
        {
            return $"{hours:00}时{minutes:00}分{seconds:00}秒";
        }

        return $"{minutes:00}分{seconds:00}秒";
    }

    private static string FormatInteger(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatClockPart(int value)
    {
        return value.ToString("00", CultureInfo.InvariantCulture);
    }

    private static int GetYear(ReminderEventSnapshot snapshot)
    {
        return snapshot.ScheduledTime.OneTimeAt.ToLocalTime().Year;
    }

    private static int GetMonth(ReminderEventSnapshot snapshot)
    {
        return snapshot.ScheduledTime.Recurrence == ReminderRecurrence.Once
            ? snapshot.ScheduledTime.OneTimeAt.ToLocalTime().Month
            : snapshot.ScheduledTime.MonthOfYear;
    }

    private static int GetDay(ReminderEventSnapshot snapshot)
    {
        return snapshot.ScheduledTime.Recurrence == ReminderRecurrence.Once
            ? snapshot.ScheduledTime.OneTimeAt.ToLocalTime().Day
            : snapshot.ScheduledTime.DayOfMonth;
    }

    private static int GetHour(ReminderEventSnapshot snapshot)
    {
        return snapshot.ScheduledTime.Recurrence == ReminderRecurrence.Once
            ? snapshot.ScheduledTime.OneTimeAt.ToLocalTime().Hour
            : snapshot.ScheduledTime.TimeOfDay.Hour;
    }

    private static int GetMinute(ReminderEventSnapshot snapshot)
    {
        return snapshot.ScheduledTime.Recurrence == ReminderRecurrence.Once
            ? snapshot.ScheduledTime.OneTimeAt.ToLocalTime().Minute
            : snapshot.ScheduledTime.TimeOfDay.Minute;
    }

    private static string FormatOneTime(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm",
            CultureInfo.InvariantCulture);
    }

    private static string FormatTime(TimeOnly value)
    {
        return value.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static RecurrenceChoice FindRecurrenceChoice(
        ReminderRecurrence recurrence)
    {
        return RecurrenceChoiceValues.First(item => item.Value == recurrence);
    }

    private static WeekdayChoice FindWeekdayChoice(DayOfWeek dayOfWeek)
    {
        return WeekdayChoiceValues.First(item => item.Value == dayOfWeek);
    }

    private static TerminationChoice FindTerminationChoice(int? remaining)
    {
        var value = remaining ?? 0;
        return TerminationChoiceValues[value];
    }
}
