using Reminder.App.Logic.Models;
using Reminder.App.Logic.Scheduling;
using Reminder.App.SystemModule.AppInfo;

namespace Reminder.App.Logic.Services;

public sealed partial class ReminderEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly List<ReminderEvent> _events = [];
    private readonly HashSet<Guid> _pendingMissedEventIds = [];
    private readonly IReminderNotificationService _notificationService;
    private readonly ReminderScheduler _scheduler;
    private readonly TimeProvider _timeProvider;
    private ReminderGlobalPauseDuration _globalPauseDuration =
        ReminderGlobalPauseDuration.UntilManualResume;
    private DateTimeOffset? _globalPauseEndsAt;
    private bool _isGlobalPaused;
    private ReminderSystemState _systemState;
    private bool _disposed;

    public ReminderEngine(
        IReminderNotificationService notificationService,
        TimeProvider? timeProvider = null)
    {
        _notificationService = notificationService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _notificationService.ResponseReceived += OnNotificationResponseReceived;
        _scheduler = new ReminderScheduler(OnSchedulerElapsed, _timeProvider);
    }

    public event EventHandler? StateChanged;

    public event EventHandler? DurableStateChanged;

    public bool NotificationsAvailable => _notificationService.IsAvailable;

    public string NotificationStatus => _notificationService.StatusMessage;

    public string NotificationStatusHelp =>
        _notificationService.StatusHelpMessage;

    public void InitializeDefaultEvents()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_events.Count != 0)
            {
                return;
            }

            AddEventCore("喝水", ReminderDefaults.NewEventIntervalMinutes, isEnabled: false);
            AddEventCore("休息眼睛", ReminderDefaults.NewEventIntervalMinutes, isEnabled: false);
            RescheduleLocked(Now);
        }

        RaiseStateChanged();
    }

    public Guid AddDefaultEvent()
    {
        Guid id;
        lock (_gate)
        {
            ThrowIfDisposed();
            id = AddEventCore("新建事件", ReminderDefaults.NewEventIntervalMinutes, isEnabled: true);
            RescheduleLocked(Now);
        }

        RaiseStateChanged();
        RaiseDurableStateChanged();
        return id;
    }

    public IReadOnlyList<ReminderEventSnapshot> GetSnapshots(DateTimeOffset now)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return _events.Select(reminderEvent => CreateSnapshot(reminderEvent, now)).ToArray();
        }
    }

    public bool UpdateName(Guid eventId, string name)
    {
        if (!ReminderInputValidator.TryValidateName(name, out var validatedName, out _))
        {
            return false;
        }

        string? missedEventName = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null)
            {
                return false;
            }

            if (reminderEvent.Name == validatedName)
            {
                return true;
            }

            reminderEvent.Name = validatedName;
            var now = Now;
            ActivateAfterEditLocked(reminderEvent, now);
            if (IsPastOneTimeEvent(reminderEvent, now))
            {
                missedEventName = reminderEvent.Name;
            }

            RescheduleLocked(now);
        }

        if (missedEventName is not null)
        {
            _notificationService.ShowMissedEvents([missedEventName]);
        }

        RaiseStateChanged();
        RaiseDurableStateChanged();
        return true;
    }

    public bool UpdateInterval(Guid eventId, int intervalMinutes)
    {
        if (intervalMinutes is < ReminderDefaults.MinimumIntervalMinutes or
            > ReminderDefaults.MaximumIntervalMinutes)
        {
            return false;
        }

        Guid? notificationToRemove;
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null ||
                reminderEvent.Schedule is not FixedIntervalSchedule fixedInterval)
            {
                return false;
            }

            if (fixedInterval.Interval ==
                TimeSpan.FromMinutes(intervalMinutes))
            {
                return true;
            }

            reminderEvent.Schedule = new FixedIntervalSchedule(
                TimeSpan.FromMinutes(intervalMinutes));
            PrepareActiveScheduleEditLocked(reminderEvent);
            notificationToRemove = ApplyScheduleEditLocked(
                reminderEvent,
                Now);
            RescheduleLocked(Now);
        }

        RemoveNotification(notificationToRemove);
        RaiseStateChanged();
        RaiseDurableStateChanged();
        return true;
    }

    public bool UpdateEventType(Guid eventId, ReminderEventType eventType)
    {
        Guid? notificationToRemove;
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null || reminderEvent.Schedule.Type == eventType)
            {
                return reminderEvent is not null;
            }

            var now = Now;
            reminderEvent.Schedule = eventType switch
            {
                ReminderEventType.FixedInterval => new FixedIntervalSchedule(
                    TimeSpan.FromMinutes(ReminderDefaults.NewEventIntervalMinutes)),
                ReminderEventType.ScheduledTime => new ScheduledTimeSchedule(
                    ScheduledTimeSettings.CreateDefault(ReminderRecurrence.Once, now)),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(eventType),
                    eventType,
                    "不支持的事件类型")
            };

            if (eventType == ReminderEventType.ScheduledTime)
            {
                reminderEvent.Termination.SetRemaining(null);
            }

            PrepareActiveScheduleEditLocked(reminderEvent);
            notificationToRemove = ApplyScheduleEditLocked(reminderEvent, now);
            RescheduleLocked(now);
        }

        RemoveNotification(notificationToRemove);
        RaiseStateChanged();
        RaiseDurableStateChanged();
        return true;
    }

    public bool UpdateRecurrence(Guid eventId, ReminderRecurrence recurrence)
    {
        Guid? notificationToRemove;
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent?.Schedule is not ScheduledTimeSchedule schedule)
            {
                return false;
            }

            if (schedule.Settings.Recurrence == recurrence)
            {
                return true;
            }

            var now = Now;
            reminderEvent.Schedule = new ScheduledTimeSchedule(
                ScheduledTimeSettings.CreateDefault(recurrence, now));
            if (recurrence == ReminderRecurrence.Once)
            {
                reminderEvent.Termination.SetRemaining(null);
            }

            PrepareActiveScheduleEditLocked(reminderEvent);
            notificationToRemove = ApplyScheduleEditLocked(reminderEvent, now);
            RescheduleLocked(now);
        }

        RemoveNotification(notificationToRemove);
        RaiseStateChanged();
        RaiseDurableStateChanged();
        return true;
    }

    public bool UpdateOneTime(Guid eventId, DateTimeOffset oneTimeAt)
    {
        return UpdateScheduledSettings(
            eventId,
            settings => settings.Recurrence == ReminderRecurrence.Once
                ? settings with { OneTimeAt = oneTimeAt }
                : null);
    }

    public bool UpdateTimeOfDay(Guid eventId, TimeOnly timeOfDay)
    {
        return UpdateScheduledSettings(
            eventId,
            settings => settings with { TimeOfDay = timeOfDay });
    }

    public bool UpdateMonthlySchedule(
        Guid eventId,
        int dayOfMonth,
        TimeOnly timeOfDay)
    {
        if (dayOfMonth is < 1 or > 31)
        {
            return false;
        }

        return UpdateScheduledSettings(
            eventId,
            settings => settings.Recurrence == ReminderRecurrence.Monthly
                ? settings with
                {
                    DayOfMonth = dayOfMonth,
                    TimeOfDay = timeOfDay
                }
                : null);
    }

    public bool UpdateYearlySchedule(
        Guid eventId,
        int monthOfYear,
        int dayOfMonth,
        TimeOnly timeOfDay)
    {
        if (monthOfYear is < 1 or > 12 ||
            dayOfMonth is < 1 ||
            dayOfMonth > DateTime.DaysInMonth(2_000, monthOfYear))
        {
            return false;
        }

        return UpdateScheduledSettings(
            eventId,
            settings => settings.Recurrence == ReminderRecurrence.Yearly
                ? settings with
                {
                    MonthOfYear = monthOfYear,
                    DayOfMonth = dayOfMonth,
                    TimeOfDay = timeOfDay
                }
                : null);
    }

    public bool UpdateDayOfWeek(Guid eventId, DayOfWeek dayOfWeek)
    {
        return UpdateScheduledSettings(
            eventId,
            settings => settings.Recurrence == ReminderRecurrence.Weekly
                ? settings with { DayOfWeek = dayOfWeek }
                : null);
    }

    public bool UpdateDayOfMonth(Guid eventId, int dayOfMonth)
    {
        if (dayOfMonth is < 1 or > 31)
        {
            return false;
        }

        return UpdateScheduledSettings(
            eventId,
            settings => settings.Recurrence is
                ReminderRecurrence.Monthly or ReminderRecurrence.Yearly
                ? settings with { DayOfMonth = dayOfMonth }
                : null);
    }

    public bool UpdateMonthOfYear(Guid eventId, int monthOfYear)
    {
        if (monthOfYear is < 1 or > 12)
        {
            return false;
        }

        return UpdateScheduledSettings(
            eventId,
            settings => settings.Recurrence == ReminderRecurrence.Yearly &&
                        settings.DayOfMonth <= DateTime.DaysInMonth(2_000, monthOfYear)
                ? settings with { MonthOfYear = monthOfYear }
                : null);
    }

    public bool UpdateYearlyDate(
        Guid eventId,
        int monthOfYear,
        int dayOfMonth)
    {
        if (monthOfYear is < 1 or > 12 ||
            dayOfMonth is < 1 ||
            dayOfMonth > DateTime.DaysInMonth(2_000, monthOfYear))
        {
            return false;
        }

        return UpdateScheduledSettings(
            eventId,
            settings => settings.Recurrence == ReminderRecurrence.Yearly
                ? settings with
                {
                    MonthOfYear = monthOfYear,
                    DayOfMonth = dayOfMonth
                }
                : null);
    }

    public bool UpdateTermination(Guid eventId, int? remainingOccurrences)
    {
        if (remainingOccurrences is <= 0 or
            > ReminderDefaults.MaximumTerminationOccurrences)
        {
            return false;
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null ||
                reminderEvent.Schedule is ScheduledTimeSchedule
                {
                    Settings.Recurrence: ReminderRecurrence.Once
                })
            {
                return false;
            }

            if (reminderEvent.Termination.RemainingOccurrences ==
                remainingOccurrences)
            {
                return true;
            }

            reminderEvent.Termination.SetRemaining(remainingOccurrences);
            var now = Now;
            ActivateAfterEditLocked(reminderEvent, now);
            RescheduleLocked(now);
        }

        RaiseStateChanged();
        RaiseDurableStateChanged();
        return true;
    }

    public void TogglePause(Guid eventId)
    {
        Guid? notificationToRemove = null;
        string? missedEventName = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null ||
                reminderEvent.Schedule.Type != ReminderEventType.FixedInterval ||
                !reminderEvent.IsEnabled)
            {
                return;
            }

            var now = Now;
            if (reminderEvent.IsPaused)
            {
                ResumeLocked(reminderEvent, now);
                if (IsPastOneTimeEvent(reminderEvent, now))
                {
                    missedEventName = reminderEvent.Name;
                }
            }
            else
            {
                notificationToRemove = PauseLocked(reminderEvent, now);
            }

            RescheduleLocked(now);
        }

        RemoveNotification(notificationToRemove);
        if (missedEventName is not null)
        {
            _notificationService.ShowMissedEvents([missedEventName]);
        }

        RaiseStateChanged();
    }

    public void Restart(Guid eventId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null ||
                reminderEvent.Schedule is not FixedIntervalSchedule fixedInterval ||
                !reminderEvent.IsEnabled ||
                reminderEvent.IsPaused ||
                reminderEvent.FixedClockBlockReasons !=
                    FixedClockBlockReason.None ||
                reminderEvent.ActiveNotificationId is not null)
            {
                return;
            }

            var now = Now;
            reminderEvent.FrozenRemaining = fixedInterval.Interval;
            reminderEvent.DueAt = now + fixedInterval.Interval;
            reminderEvent.IsExpired = false;
            RescheduleLocked(now);
        }

        RaiseStateChanged();
    }

    public void SetEnabled(Guid eventId, bool isEnabled)
    {
        Guid? notificationToRemove = null;
        string? missedEventName = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null || reminderEvent.IsEnabled == isEnabled)
            {
                return;
            }

            var now = Now;
            if (isEnabled)
            {
                reminderEvent.IsEnabled = true;
                if (reminderEvent.Schedule.Type ==
                    ReminderEventType.ScheduledTime)
                {
                    ResumeLocked(reminderEvent, now);
                    if (IsPastOneTimeEvent(reminderEvent, now))
                    {
                        missedEventName = reminderEvent.Name;
                    }
                }
            }
            else
            {
                notificationToRemove = PauseLocked(reminderEvent, now);
                reminderEvent.IsEnabled = false;
            }

            RescheduleLocked(now);
        }

        RemoveNotification(notificationToRemove);
        if (missedEventName is not null)
        {
            _notificationService.ShowMissedEvents([missedEventName]);
        }

        RaiseStateChanged();
        RaiseDurableStateChanged();
    }

    public void Delete(Guid eventId)
    {
        Guid? notificationToRemove = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null)
            {
                return;
            }

            notificationToRemove = reminderEvent.ActiveNotificationId;
            _pendingMissedEventIds.Remove(eventId);
            _events.Remove(reminderEvent);
            RescheduleLocked(Now);
        }

        RemoveNotification(notificationToRemove);
        RaiseStateChanged();
        RaiseDurableStateChanged();
    }

    public void RestartAll()
    {
        List<Guid> notificationsToRemove = [];
        lock (_gate)
        {
            ThrowIfDisposed();
            var now = Now;
            foreach (var reminderEvent in _events.Where(
                         item => item.Schedule is FixedIntervalSchedule &&
                                 item.IsEnabled))
            {
                var fixedInterval = (FixedIntervalSchedule)reminderEvent.Schedule;
                if (reminderEvent.ActiveNotificationId is { } notificationId)
                {
                    notificationsToRemove.Add(notificationId);
                }

                ClearNotificationStateLocked(reminderEvent);
                reminderEvent.SystemBlockInterruptedActiveNotification = false;
                reminderEvent.IsPaused = false;
                reminderEvent.FrozenRemaining = fixedInterval.Interval;
                reminderEvent.DueAt =
                    reminderEvent.FixedClockBlockReasons ==
                        FixedClockBlockReason.None
                        ? now + fixedInterval.Interval
                        : null;
                reminderEvent.IsExpired = false;
                reminderEvent.ShowExpiredEasterEgg = false;
            }

            RescheduleLocked(now);
        }

        RemoveNotifications(notificationsToRemove);
        RaiseStateChanged();
        RaiseDurableStateChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notificationService.ResponseReceived -= OnNotificationResponseReceived;
        _scheduler.Dispose();
        _notificationService.Dispose();
    }

    private Guid AddEventCore(string name, int intervalMinutes, bool isEnabled)
    {
        var now = Now;
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        var reminderEvent = new ReminderEvent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Schedule = new FixedIntervalSchedule(interval),
            Termination = new ReminderTermination(),
            IsEnabled = isEnabled,
            IsPaused = !isEnabled,
            DueAt = isEnabled ? now + interval : null,
            FrozenRemaining = interval
        };

        _events.Add(reminderEvent);
        if (isEnabled && _isGlobalPaused)
        {
            AddFixedClockBlockLocked(
                reminderEvent,
                FixedClockBlockReason.GlobalPause,
                now,
                resetToFullInterval: false,
                notificationsToRemove: null);
        }

        if (isEnabled && _systemState.IsScreenOrLockUnavailable)
        {
            ApplyFixedSystemBlockLocked(
                reminderEvent,
                now,
                notificationsToRemove: null);
        }

        return reminderEvent.Id;
    }

    private ReminderEventSnapshot CreateSnapshot(
        ReminderEvent reminderEvent,
        DateTimeOffset now)
    {
        var fixedInterval = reminderEvent.Schedule as FixedIntervalSchedule;
        var scheduledTime = reminderEvent.Schedule as ScheduledTimeSchedule;
        var scheduledSettings = scheduledTime?.Settings ??
                                ScheduledTimeSettings.CreateDefault(
                                    ReminderRecurrence.Once,
                                    now);

        TimeSpan? remaining;
        if (reminderEvent.IsExpired)
        {
            remaining = null;
        }
        else if (reminderEvent.DueAt is not null)
        {
            remaining = reminderEvent.DueAt.Value - now;
        }
        else if (fixedInterval is not null)
        {
            remaining = reminderEvent.FrozenRemaining;
        }
        else
        {
            var nextOccurrence = ScheduledTimeCalculator.GetNextOccurrence(
                scheduledSettings,
                now,
                inclusive: true);
            remaining = nextOccurrence - now;
        }

        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        var isAwaitingAction =
            reminderEvent.ActiveNotificationId is not null &&
            !reminderEvent.IsNotificationDeferred;
        var isBlockedByGlobalPause =
            reminderEvent.IsEnabled && _isGlobalPaused;
        var isBlockedBySystemState =
            reminderEvent.IsEnabled &&
            IsBlockedBySystemStateLocked(reminderEvent);
        var isEffectivelyRunning =
            reminderEvent.IsEnabled &&
            !reminderEvent.IsPaused &&
            !isBlockedByGlobalPause &&
            !isBlockedBySystemState &&
            !isAwaitingAction;

        return new ReminderEventSnapshot
        {
            Id = reminderEvent.Id,
            Name = reminderEvent.Name,
            EventType = reminderEvent.Schedule.Type,
            IntervalMinutes = fixedInterval is null
                ? ReminderDefaults.NewEventIntervalMinutes
                : checked((int)fixedInterval.Interval.TotalMinutes),
            ScheduledTime = scheduledSettings,
            RemainingOccurrences =
                reminderEvent.Termination.RemainingOccurrences,
            IsEnabled = reminderEvent.IsEnabled,
            IsPaused = reminderEvent.IsPaused,
            FixedUnavailablePolicy =
                reminderEvent.FixedUnavailablePolicy,
            FixedUnavailableNotificationPolicy =
                reminderEvent.FixedUnavailableNotificationPolicy,
            ScheduledUnavailableNotificationPolicy =
                reminderEvent.ScheduledUnavailableNotificationPolicy,
            IsBlockedByGlobalPause = isBlockedByGlobalPause,
            IsBlockedBySystemState = isBlockedBySystemState,
            IsEffectivelyRunning = isEffectivelyRunning,
            IsAwaitingAction = isAwaitingAction,
            HasPendingMissedOccurrence =
                _pendingMissedEventIds.Contains(reminderEvent.Id),
            CanRestart =
                reminderEvent.Schedule.Type == ReminderEventType.FixedInterval &&
                reminderEvent.IsEnabled &&
                !reminderEvent.IsPaused &&
                reminderEvent.FixedClockBlockReasons ==
                    FixedClockBlockReason.None &&
                !_isGlobalPaused &&
                !_systemState.IsSleeping &&
                reminderEvent.ActiveNotificationId is null,
            IsExpired = reminderEvent.IsExpired,
            ShowExpiredEasterEgg = reminderEvent.ShowExpiredEasterEgg,
            Remaining = remaining
        };
    }

    private void OnSchedulerElapsed()
    {
        List<ReminderNotificationRequest> requests = [];
        List<Guid> notificationsToReplace = [];
        var stateChanged = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var now = Now;
            if (_isGlobalPaused &&
                _globalPauseEndsAt is not null &&
                _globalPauseEndsAt.Value <= now)
            {
                ProcessSuppressedDueEventsLocked(
                    now,
                    strictlyBefore: true,
                    forceSuppressAll: false);
                EndGlobalPauseLocked(now);
                stateChanged = true;
            }

            foreach (var reminderEvent in _events.Where(
                         item => item.IsEnabled &&
                                 !item.IsPaused &&
                                 (item.ActiveNotificationId is null ||
                                  item.IsNotificationDeferred) &&
                                 item.DueAt is not null &&
                                 item.DueAt.Value <= now)
                     .ToArray())
            {
                if (!IsOrdinaryNotificationAllowedLocked(reminderEvent))
                {
                    if (reminderEvent.ActiveNotificationId is { } activeId)
                    {
                        notificationsToReplace.Add(activeId);
                    }

                    ProcessMissedOccurrenceLocked(
                        reminderEvent,
                        now,
                        strictlyBefore: false);
                    stateChanged = true;
                    continue;
                }

                if (reminderEvent.ActiveNotificationId is not null)
                {
                    notificationsToReplace.Add(
                        reminderEvent.ActiveNotificationId.Value);
                }

                reminderEvent.ActiveOccurrenceAt ??= reminderEvent.DueAt;
                var notificationId = Guid.NewGuid();
                reminderEvent.ActiveNotificationId = notificationId;
                reminderEvent.IsNotificationDeferred = false;
                reminderEvent.NotificationShownAt = now;
                reminderEvent.DueAt = null;
                reminderEvent.FrozenRemaining = TimeSpan.Zero;

                requests.Add(
                    new ReminderNotificationRequest(
                        reminderEvent.Id,
                        notificationId,
                        reminderEvent.Name,
                        now,
                        ReminderDefaults.NotificationVisibleDuration,
                        RequestDisplayAttention:
                            _systemState.IsDisplayOff));
                stateChanged = true;
            }

            RescheduleLocked(now);
        }

        foreach (var notificationId in notificationsToReplace)
        {
            _notificationService.Remove(notificationId);
        }

        if (stateChanged)
        {
            RaiseStateChanged();
        }

        foreach (var request in requests)
        {
            if (!_notificationService.Show(request))
            {
                ApplyNotificationResponse(
                    new ReminderNotificationResponse(
                        request.EventId,
                        request.NotificationId,
                        ReminderNotificationAction.DeliveryFailed,
                        Now));
            }
        }
    }

    private void OnNotificationResponseReceived(
        object? sender,
        ReminderNotificationResponse response)
    {
        ApplyNotificationResponse(response);
    }

    private void ApplyNotificationResponse(ReminderNotificationResponse response)
    {
        var shouldRemoveNotification = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var reminderEvent = FindEventLocked(response.EventId);
            if (reminderEvent is null ||
                reminderEvent.ActiveNotificationId != response.NotificationId)
            {
                return;
            }

            var notificationShownAt =
                reminderEvent.NotificationShownAt ?? response.OccurredAt;
            var occurrenceAt =
                reminderEvent.ActiveOccurrenceAt ?? notificationShownAt;
            var snoozeDuration = GetSnoozeDuration(reminderEvent);
            var isAutomaticTimeout =
                response.Action == ReminderNotificationAction.TimedOut;

            if (isAutomaticTimeout)
            {
                if (reminderEvent.IsNotificationDeferred)
                {
                    return;
                }

                reminderEvent.IsNotificationDeferred = true;
                reminderEvent.DueAt = response.OccurredAt + snoozeDuration;
                reminderEvent.FrozenRemaining = snoozeDuration;
                RescheduleLocked(Now);
                RaiseStateChanged();
                return;
            }

            reminderEvent.ActiveNotificationId = null;
            reminderEvent.IsNotificationDeferred = false;
            reminderEvent.NotificationShownAt = null;
            shouldRemoveNotification = response.Action is
                ReminderNotificationAction.Complete or
                ReminderNotificationAction.Snooze or
                ReminderNotificationAction.Skip;

            var consumesOccurrence = response.Action is
                ReminderNotificationAction.Complete or
                ReminderNotificationAction.Skip or
                ReminderNotificationAction.UserClosed;
            var isOneTime = reminderEvent.Schedule is ScheduledTimeSchedule
            {
                Settings.Recurrence: ReminderRecurrence.Once
            };

            if (consumesOccurrence &&
                (isOneTime ||
                 reminderEvent.Termination.ConsumeOccurrence()))
            {
                CloseEventLocked(reminderEvent);
            }
            else if (!reminderEvent.IsEnabled || reminderEvent.IsPaused)
            {
                reminderEvent.DueAt = null;
                if (reminderEvent.Schedule is FixedIntervalSchedule fixedInterval)
                {
                    reminderEvent.FrozenRemaining =
                        response.Action is ReminderNotificationAction.Snooze or
                            ReminderNotificationAction.DeliveryFailed
                            ? snoozeDuration
                            : fixedInterval.Interval;
                }
            }
            else
            {
                ScheduleAfterResponseLocked(
                    reminderEvent,
                    response,
                    notificationShownAt,
                    occurrenceAt,
                    snoozeDuration);
            }

            if (response.Action is not
                ReminderNotificationAction.Snooze and not
                ReminderNotificationAction.DeliveryFailed)
            {
                reminderEvent.ActiveOccurrenceAt = null;
            }

            RescheduleLocked(Now);
        }

        if (shouldRemoveNotification)
        {
            _notificationService.Remove(response.NotificationId);
        }

        RaiseStateChanged();
    }

    private bool UpdateScheduledSettings(
        Guid eventId,
        Func<ScheduledTimeSettings, ScheduledTimeSettings?> update)
    {
        Guid? notificationToRemove;
        string? missedEventName = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent?.Schedule is not ScheduledTimeSchedule schedule)
            {
                return false;
            }

            var settings = update(schedule.Settings);
            if (settings is null || !AreScheduledSettingsValid(settings))
            {
                return false;
            }

            if (settings == schedule.Settings)
            {
                return true;
            }

            reminderEvent.Schedule = new ScheduledTimeSchedule(settings);
            PrepareActiveScheduleEditLocked(reminderEvent);
            var now = Now;
            notificationToRemove = ApplyScheduleEditLocked(reminderEvent, now);
            if (settings.Recurrence == ReminderRecurrence.Once &&
                settings.OneTimeAt < now &&
                reminderEvent.IsExpired)
            {
                missedEventName = reminderEvent.Name;
            }

            RescheduleLocked(now);
        }

        RemoveNotification(notificationToRemove);
        if (missedEventName is not null)
        {
            _notificationService.ShowMissedEvents([missedEventName]);
        }

        RaiseStateChanged();
        RaiseDurableStateChanged();
        return true;
    }

    private static bool AreScheduledSettingsValid(
        ScheduledTimeSettings settings)
    {
        if (settings.DayOfMonth is < 1 or > 31 ||
            settings.MonthOfYear is < 1 or > 12)
        {
            return false;
        }

        return settings.Recurrence != ReminderRecurrence.Yearly ||
               settings.DayOfMonth <=
               DateTime.DaysInMonth(2_000, settings.MonthOfYear);
    }

    private static bool IsPastOneTimeEvent(
        ReminderEvent reminderEvent,
        DateTimeOffset now)
    {
        return reminderEvent.IsExpired &&
               reminderEvent.Schedule is ScheduledTimeSchedule
               {
                   Settings:
                   {
                       Recurrence: ReminderRecurrence.Once
                   } settings
               } &&
               settings.OneTimeAt < now;
    }

    private Guid? ApplyScheduleEditLocked(
        ReminderEvent reminderEvent,
        DateTimeOffset now)
    {
        var notificationId = reminderEvent.ActiveNotificationId;
        ClearNotificationStateLocked(reminderEvent);
        reminderEvent.IsExpired = false;
        reminderEvent.ShowExpiredEasterEgg = false;

        if (reminderEvent.Schedule is FixedIntervalSchedule fixedInterval)
        {
            reminderEvent.FixedClockBlockReasons =
                FixedClockBlockReason.None;
            reminderEvent.FrozenRemaining = fixedInterval.Interval;
            reminderEvent.DueAt =
                reminderEvent.IsEnabled && !reminderEvent.IsPaused
                    ? now + fixedInterval.Interval
                    : null;

            if (reminderEvent.IsEnabled && _isGlobalPaused)
            {
                AddFixedClockBlockLocked(
                    reminderEvent,
                    FixedClockBlockReason.GlobalPause,
                    now,
                    resetToFullInterval: false,
                    notificationsToRemove: null);
            }

            if (reminderEvent.IsEnabled &&
                _systemState.IsScreenOrLockUnavailable)
            {
                ApplyFixedSystemBlockLocked(
                    reminderEvent,
                    now,
                    notificationsToRemove: null);
            }
        }
        else if (reminderEvent.IsEnabled && !reminderEvent.IsPaused)
        {
            reminderEvent.FixedClockBlockReasons =
                FixedClockBlockReason.None;
            ScheduleNextOccurrenceLocked(reminderEvent, now, inclusive: true);
        }
        else
        {
            reminderEvent.DueAt = null;
        }

        return notificationId;
    }

    private static void ScheduleAfterResponseLocked(
        ReminderEvent reminderEvent,
        ReminderNotificationResponse response,
        DateTimeOffset notificationShownAt,
        DateTimeOffset occurrenceAt,
        TimeSpan snoozeDuration)
    {
        if (reminderEvent.Schedule is FixedIntervalSchedule fixedInterval)
        {
            var nextDueAt = response.Action switch
            {
                ReminderNotificationAction.Complete =>
                    response.OccurredAt + fixedInterval.Interval,
                ReminderNotificationAction.Snooze =>
                    response.OccurredAt + snoozeDuration,
                ReminderNotificationAction.Skip =>
                    notificationShownAt + fixedInterval.Interval,
                ReminderNotificationAction.UserClosed =>
                    notificationShownAt + fixedInterval.Interval,
                ReminderNotificationAction.DeliveryFailed =>
                    response.OccurredAt + snoozeDuration,
                _ => response.OccurredAt + fixedInterval.Interval
            };

            reminderEvent.DueAt = nextDueAt;
            reminderEvent.FrozenRemaining =
                nextDueAt - response.OccurredAt;
            return;
        }

        if (response.Action is
            ReminderNotificationAction.Snooze or
            ReminderNotificationAction.DeliveryFailed)
        {
            reminderEvent.DueAt = response.OccurredAt + snoozeDuration;
            reminderEvent.FrozenRemaining = snoozeDuration;
            return;
        }

        ScheduleNextOccurrenceLocked(
            reminderEvent,
            response.OccurredAt > occurrenceAt
                ? response.OccurredAt
                : occurrenceAt,
            inclusive: false);
    }

    private static Guid? PauseLocked(
        ReminderEvent reminderEvent,
        DateTimeOffset now)
    {
        var notificationId = reminderEvent.ActiveNotificationId;
        if (reminderEvent.Schedule is FixedIntervalSchedule fixedInterval)
        {
            if (reminderEvent.FixedClockBlockReasons ==
                    FixedClockBlockReason.None &&
                reminderEvent.DueAt is not null)
            {
                reminderEvent.FrozenRemaining =
                    reminderEvent.DueAt.Value - now;
                if (reminderEvent.FrozenRemaining < TimeSpan.Zero)
                {
                    reminderEvent.FrozenRemaining = TimeSpan.Zero;
                }
            }
            else if (reminderEvent.FixedClockBlockReasons ==
                         FixedClockBlockReason.None &&
                     notificationId is not null)
            {
                reminderEvent.FrozenRemaining = TimeSpan.Zero;
            }
            else if (reminderEvent.FrozenRemaining <= TimeSpan.Zero)
            {
                reminderEvent.FrozenRemaining = fixedInterval.Interval;
            }
        }

        reminderEvent.DueAt = null;
        ClearNotificationStateLocked(reminderEvent);
        reminderEvent.IsPaused = true;
        return notificationId;
    }

    private static void ResumeLocked(
        ReminderEvent reminderEvent,
        DateTimeOffset now)
    {
        reminderEvent.IsPaused = false;
        reminderEvent.IsExpired = false;
        reminderEvent.ShowExpiredEasterEgg = false;

        if (reminderEvent.Schedule is FixedIntervalSchedule)
        {
            if (reminderEvent.FixedClockBlockReasons !=
                FixedClockBlockReason.None)
            {
                reminderEvent.DueAt = null;
                return;
            }

            ResumeFixedClockLocked(reminderEvent, now);
            return;
        }

        ScheduleNextOccurrenceLocked(reminderEvent, now, inclusive: true);
    }

    private static void ScheduleNextOccurrenceLocked(
        ReminderEvent reminderEvent,
        DateTimeOffset reference,
        bool inclusive)
    {
        if (reminderEvent.Schedule is not ScheduledTimeSchedule schedule)
        {
            return;
        }

        var nextOccurrence = ScheduledTimeCalculator.GetNextOccurrence(
            schedule.Settings,
            reference,
            inclusive: inclusive);
        if (nextOccurrence is null)
        {
            CloseEventLocked(reminderEvent);
            return;
        }

        reminderEvent.DueAt = nextOccurrence;
        reminderEvent.FrozenRemaining =
            nextOccurrence.Value - reference;
        reminderEvent.IsExpired = false;
    }

    private static void CloseEventLocked(ReminderEvent reminderEvent)
    {
        reminderEvent.IsEnabled = false;
        reminderEvent.IsPaused = true;
        reminderEvent.FixedClockBlockReasons =
            FixedClockBlockReason.None;
        reminderEvent.SystemBlockInterruptedActiveNotification = false;
        reminderEvent.DueAt = null;
        reminderEvent.FrozenRemaining = TimeSpan.Zero;
        ClearNotificationStateLocked(reminderEvent);
        reminderEvent.IsExpired = true;
        reminderEvent.ShowExpiredEasterEgg =
            Random.Shared.Next(100) < 5;
    }

    private static void ClearNotificationStateLocked(
        ReminderEvent reminderEvent)
    {
        reminderEvent.ActiveNotificationId = null;
        reminderEvent.IsNotificationDeferred = false;
        reminderEvent.NotificationShownAt = null;
        reminderEvent.ActiveOccurrenceAt = null;
    }

    private static void PrepareActiveScheduleEditLocked(
        ReminderEvent reminderEvent)
    {
        reminderEvent.IsEnabled = true;
        reminderEvent.IsPaused = false;
    }

    private void ActivateAfterEditLocked(
        ReminderEvent reminderEvent,
        DateTimeOffset now)
    {
        reminderEvent.IsEnabled = true;
        if (reminderEvent.IsPaused)
        {
            ResumeLocked(reminderEvent, now);
        }

        if (reminderEvent.Schedule is not FixedIntervalSchedule)
        {
            return;
        }

        if (_isGlobalPaused)
        {
            AddFixedClockBlockLocked(
                reminderEvent,
                FixedClockBlockReason.GlobalPause,
                now,
                resetToFullInterval: false,
                notificationsToRemove: null);
        }

        if (_systemState.IsScreenOrLockUnavailable)
        {
            ApplyFixedSystemBlockLocked(
                reminderEvent,
                now,
                notificationsToRemove: null);
        }
    }

    private ReminderEvent? FindEventLocked(Guid eventId)
    {
        return _events.FirstOrDefault(item => item.Id == eventId);
    }

    private void RescheduleLocked(DateTimeOffset now)
    {
        var dueDates = _events
            .Where(item =>
                item.IsEnabled &&
                !item.IsPaused &&
                (item.Schedule is not FixedIntervalSchedule ||
                 item.FixedClockBlockReasons ==
                     FixedClockBlockReason.None) &&
                (item.ActiveNotificationId is null ||
                 item.IsNotificationDeferred) &&
                item.DueAt is not null)
            .Select(item => item.DueAt!.Value)
            .ToArray();

        var nextDueAt = dueDates.Length == 0
            ? (DateTimeOffset?)null
            : dueDates.Min();
        if (_isGlobalPaused &&
            _globalPauseEndsAt is not null &&
            (nextDueAt is null ||
             _globalPauseEndsAt.Value < nextDueAt.Value))
        {
            nextDueAt = _globalPauseEndsAt;
        }

        _scheduler.Schedule(nextDueAt, now);
    }

    private void RemoveNotification(Guid? notificationId)
    {
        if (notificationId is not null)
        {
            _notificationService.Remove(notificationId.Value);
        }
    }

    private static TimeSpan GetSnoozeDuration(ReminderEvent reminderEvent)
    {
        return reminderEvent.Schedule is FixedIntervalSchedule fixedInterval &&
               fixedInterval.Interval < ReminderDefaults.SnoozeDuration
            ? fixedInterval.Interval
            : ReminderDefaults.SnoozeDuration;
    }

    private void RaiseStateChanged()
    {
        List<string> missedEventNames = [];
        if (!Monitor.IsEntered(_gate))
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    missedEventNames =
                        TakeReadyMissedEventNamesLocked();
                    if (missedEventNames.Count != 0)
                    {
                        RescheduleLocked(Now);
                    }
                }
            }
        }

        if (missedEventNames.Count != 0)
        {
            _notificationService.ShowMissedEvents(missedEventNames);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseDurableStateChanged()
    {
        DurableStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
