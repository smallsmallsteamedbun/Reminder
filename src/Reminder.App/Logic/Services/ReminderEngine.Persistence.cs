using Reminder.App.Logic.Models;
using Reminder.App.Logic.Scheduling;
using Reminder.App.Logic.State;
using Reminder.App.SystemModule.AppInfo;

namespace Reminder.App.Logic.Services;

public sealed partial class ReminderEngine
{
    public ReminderEngineState ExportState()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var now = Now;
            var pendingMissedEventIds =
                new HashSet<Guid>(_pendingMissedEventIds);
            var eventStates = _events
                .Select(reminderEvent => CreateStateForPersistenceLocked(
                    reminderEvent,
                    now,
                    pendingMissedEventIds))
                .ToArray();

            var globalPauseRemaining = _globalPauseEndsAt - now;
            if (globalPauseRemaining < TimeSpan.Zero)
            {
                globalPauseRemaining = TimeSpan.Zero;
            }

            return new ReminderEngineState
            {
                SavedAt = now,
                Events = eventStates,
                GlobalPause = new ReminderEngineGlobalPauseState
                {
                    IsPaused = _isGlobalPaused,
                    Duration = _globalPauseDuration,
                    Remaining = _isGlobalPaused &&
                                _globalPauseEndsAt is not null
                        ? globalPauseRemaining
                        : null
                },
                PendingMissedEventIds = pendingMissedEventIds.ToArray()
            };
        }
    }

    public bool TryImportState(
        ReminderEngineState state,
        out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_gate)
        {
            ThrowIfDisposed();
            var now = Now;
            if (!TryCreateImportedState(
                    state,
                    now,
                    out var importedEvents,
                    out var pendingMissedEventIds,
                    out var globalPauseDuration,
                    out var globalPauseEndsAt,
                    out errorMessage))
            {
                return false;
            }

            _scheduler.Schedule(null, now);
            _events.Clear();
            _events.AddRange(importedEvents);
            _pendingMissedEventIds.Clear();
            _pendingMissedEventIds.UnionWith(pendingMissedEventIds);
            _globalPauseDuration = globalPauseDuration;
            _isGlobalPaused = state.GlobalPause.IsPaused;
            _globalPauseEndsAt = globalPauseEndsAt;

            foreach (var reminderEvent in _events.Where(
                         item => item.IsEnabled &&
                                 item.Schedule is FixedIntervalSchedule))
            {
                if (reminderEvent.IsBlockedByGlobalPause)
                {
                    reminderEvent.FixedClockBlockReasons |=
                        FixedClockBlockReason.GlobalPause;
                    reminderEvent.DueAt = null;
                }

                if (_systemState.IsScreenOrLockUnavailable)
                {
                    ApplyFixedSystemBlockLocked(
                        reminderEvent,
                        now,
                        notificationsToRemove: null);
                }
            }

            RescheduleLocked(now);
            errorMessage = string.Empty;
            return true;
        }
    }

    public void ActivateRecoveredState()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            RescheduleLocked(Now);
        }

        RaiseStateChanged();
    }

    private ReminderEngineEventState CreateStateForPersistenceLocked(
        ReminderEvent reminderEvent,
        DateTimeOffset now,
        HashSet<Guid> pendingMissedEventIds)
    {
        var fixedInterval = reminderEvent.Schedule as FixedIntervalSchedule;
        var scheduledTime = reminderEvent.Schedule as ScheduledTimeSchedule;
        var scheduledSettings = scheduledTime?.Settings ??
                                ScheduledTimeSettings.CreateDefault(
                                    ReminderRecurrence.Once,
                                    now);
        var hasUnhandledNotification =
            reminderEvent.ActiveNotificationId is not null &&
            !reminderEvent.IsNotificationDeferred;
        var hasDeferredOccurrence =
            reminderEvent.ActiveOccurrenceAt is not null &&
            (reminderEvent.IsNotificationDeferred ||
             reminderEvent.ActiveNotificationId is null &&
             reminderEvent.DueAt is not null);

        if (hasUnhandledNotification)
        {
            pendingMissedEventIds.Add(reminderEvent.Id);
        }

        TimeSpan? frozenRemaining = null;
        DateTimeOffset? nextScheduledOccurrenceAt = null;
        DateTimeOffset? deferredOccurrenceAt = null;

        if (fixedInterval is not null)
        {
            if (hasUnhandledNotification)
            {
                var notificationShownAt =
                    reminderEvent.NotificationShownAt ?? now;
                frozenRemaining = ClampRemaining(
                    notificationShownAt + fixedInterval.Interval - now,
                    fixedInterval.Interval);
            }
            else
            {
                frozenRemaining = ClampRemaining(
                    reminderEvent.DueAt is not null
                        ? reminderEvent.DueAt.Value - now
                        : reminderEvent.FrozenRemaining,
                    TimeSpan.FromMinutes(
                        ReminderDefaults.MaximumIntervalMinutes));
            }

        }
        else if (hasUnhandledNotification)
        {
            if (scheduledSettings.Recurrence != ReminderRecurrence.Once)
            {
                nextScheduledOccurrenceAt =
                    ScheduledTimeCalculator.GetNextOccurrence(
                        scheduledSettings,
                        now,
                        inclusive: false);
            }
        }
        else if (hasDeferredOccurrence)
        {
            frozenRemaining = ClampRemaining(
                reminderEvent.DueAt is not null
                    ? reminderEvent.DueAt.Value - now
                    : reminderEvent.FrozenRemaining,
                TimeSpan.FromDays(3_650));
            deferredOccurrenceAt =
                reminderEvent.ActiveOccurrenceAt;
        }
        else
        {
            nextScheduledOccurrenceAt = reminderEvent.DueAt;
        }

        return new ReminderEngineEventState
        {
            Id = reminderEvent.Id,
            Name = reminderEvent.Name,
            EventType = reminderEvent.Schedule.Type,
            FixedInterval =
                fixedInterval?.Interval ??
                TimeSpan.FromMinutes(
                    ReminderDefaults.NewEventIntervalMinutes),
            ScheduledTime = scheduledSettings,
            RemainingOccurrences =
                reminderEvent.Termination.RemainingOccurrences,
            IsEnabled = reminderEvent.IsEnabled,
            IsPaused = reminderEvent.IsPaused,
            IsBlockedByGlobalPause =
                reminderEvent.IsBlockedByGlobalPause,
            FixedUnavailablePolicy =
                reminderEvent.FixedUnavailablePolicy,
            FixedUnavailableNotificationPolicy =
                reminderEvent.FixedUnavailableNotificationPolicy,
            ScheduledUnavailableNotificationPolicy =
                reminderEvent.ScheduledUnavailableNotificationPolicy,
            FrozenRemaining = frozenRemaining,
            NextScheduledOccurrenceAt = nextScheduledOccurrenceAt,
            DeferredOccurrenceAt = deferredOccurrenceAt,
            IsExpired = reminderEvent.IsExpired
        };
    }

    private bool TryCreateImportedState(
        ReminderEngineState state,
        DateTimeOffset now,
        out List<ReminderEvent> importedEvents,
        out HashSet<Guid> pendingMissedEventIds,
        out ReminderGlobalPauseDuration globalPauseDuration,
        out DateTimeOffset? globalPauseEndsAt,
        out string errorMessage)
    {
        importedEvents = [];
        pendingMissedEventIds = [];
        globalPauseDuration =
            ReminderGlobalPauseDuration.UntilManualResume;
        globalPauseEndsAt = null;
        errorMessage = string.Empty;

        if (state.Events is null ||
            state.GlobalPause is null ||
            state.PendingMissedEventIds is null)
        {
            errorMessage = "状态文档缺少必要字段。";
            return false;
        }

        if (!Enum.IsDefined(state.GlobalPause.Duration))
        {
            errorMessage = "全部暂停时长无效。";
            return false;
        }

        if (state.GlobalPause.IsPaused)
        {
            var expectedPauseLength =
                state.GlobalPause.Duration.ToTimeSpan();
            if (expectedPauseLength is null)
            {
                if (state.GlobalPause.Remaining is not null)
                {
                    errorMessage = "手动恢复暂停不能包含剩余时长。";
                    return false;
                }
            }
            else if (state.GlobalPause.Remaining is null ||
                     state.GlobalPause.Remaining < TimeSpan.Zero ||
                     state.GlobalPause.Remaining > expectedPauseLength)
            {
                errorMessage = "限时全部暂停的剩余时长无效。";
                return false;
            }

            globalPauseEndsAt = state.GlobalPause.Remaining is null
                ? null
                : now + state.GlobalPause.Remaining.Value;
        }
        else if (state.GlobalPause.Remaining is not null)
        {
            errorMessage = "未暂停状态不能包含暂停剩余时长。";
            return false;
        }

        globalPauseDuration = state.GlobalPause.Duration;
        if (state.Events.Any(item =>
                item.IsBlockedByGlobalPause &&
                (!state.GlobalPause.IsPaused || !item.IsEnabled)))
        {
            errorMessage = "事件的全部暂停状态与全局状态不一致。";
            return false;
        }

        var eventIds = new HashSet<Guid>();
        foreach (var eventState in state.Events)
        {
            if (!TryCreateImportedEvent(
                    eventState,
                    state.SavedAt,
                    now,
                    out var reminderEvent,
                    out var becameMissed,
                    out errorMessage))
            {
                importedEvents.Clear();
                return false;
            }

            if (!eventIds.Add(reminderEvent.Id))
            {
                importedEvents.Clear();
                errorMessage = "状态文档包含重复的事件 ID。";
                return false;
            }

            importedEvents.Add(reminderEvent);
            if (becameMissed)
            {
                pendingMissedEventIds.Add(reminderEvent.Id);
            }
        }

        foreach (var eventId in state.PendingMissedEventIds)
        {
            if (!eventIds.Contains(eventId))
            {
                errorMessage = "待汇总事件引用了不存在的事件。";
                importedEvents.Clear();
                pendingMissedEventIds.Clear();
                return false;
            }

            pendingMissedEventIds.Add(eventId);
        }

        return true;
    }

    private static bool TryCreateImportedEvent(
        ReminderEngineEventState state,
        DateTimeOffset savedAt,
        DateTimeOffset now,
        out ReminderEvent reminderEvent,
        out bool becameMissed,
        out string errorMessage)
    {
        reminderEvent = null!;
        becameMissed = false;
        errorMessage = string.Empty;

        if (state.Id == Guid.Empty ||
            !ReminderInputValidator.TryValidateName(
                state.Name,
                out var validatedName,
                out _))
        {
            errorMessage = "事件 ID 或名称无效。";
            return false;
        }

        if (!state.IsEnabled && !state.IsPaused ||
            state.IsExpired && state.IsEnabled)
        {
            errorMessage = "事件开关、暂停或失效状态不一致。";
            return false;
        }

        if (!Enum.IsDefined(state.EventType) ||
            !Enum.IsDefined(state.FixedUnavailablePolicy) ||
            !Enum.IsDefined(state.FixedUnavailableNotificationPolicy) ||
            !Enum.IsDefined(
                state.ScheduledUnavailableNotificationPolicy))
        {
            errorMessage = "事件包含不支持的类型或系统状态策略。";
            return false;
        }

        if (state.RemainingOccurrences is <= 0 or
            > ReminderDefaults.MaximumTerminationOccurrences)
        {
            errorMessage = "事件终止次数无效。";
            return false;
        }

        ReminderSchedule schedule;
        if (state.EventType == ReminderEventType.FixedInterval)
        {
            if (state.FixedInterval <
                    TimeSpan.FromMinutes(
                        ReminderDefaults.MinimumIntervalMinutes) ||
                state.FixedInterval >
                    TimeSpan.FromMinutes(
                        ReminderDefaults.MaximumIntervalMinutes) ||
                state.FrozenRemaining is null ||
                state.FrozenRemaining < TimeSpan.Zero ||
                state.FrozenRemaining >
                    TimeSpan.FromMinutes(
                        ReminderDefaults.MaximumIntervalMinutes) ||
                state.DeferredOccurrenceAt is not null ||
                state.NextScheduledOccurrenceAt is not null)
            {
                errorMessage = "固定时间事件的间隔或剩余时间无效。";
                return false;
            }

            schedule = new FixedIntervalSchedule(state.FixedInterval);
        }
        else
        {
            if (state.ScheduledTime is null ||
                !AreScheduledSettingsValid(state.ScheduledTime) ||
                state.IsEnabled && state.IsPaused ||
                !state.IsEnabled &&
                (state.DeferredOccurrenceAt is not null ||
                 state.NextScheduledOccurrenceAt is not null) ||
                state.DeferredOccurrenceAt is not null &&
                (!state.IsEnabled ||
                 state.IsPaused ||
                (state.FrozenRemaining is null ||
                 state.FrozenRemaining < TimeSpan.Zero ||
                 state.FrozenRemaining > TimeSpan.FromDays(3_650))) ||
                state.DeferredOccurrenceAt is null &&
                state.FrozenRemaining is not null)
            {
                errorMessage = "指定时间事件的计划或延迟剩余时间无效。";
                return false;
            }

            if (state.ScheduledTime.Recurrence ==
                    ReminderRecurrence.Once &&
                state.RemainingOccurrences is not null)
            {
                errorMessage = "一次性事件不能包含终止次数。";
                return false;
            }

            schedule = new ScheduledTimeSchedule(state.ScheduledTime);
        }

        var termination = new ReminderTermination();
        termination.SetRemaining(state.RemainingOccurrences);
        reminderEvent = new ReminderEvent
        {
            Id = state.Id,
            Name = validatedName,
            Schedule = schedule,
            Termination = termination,
            IsEnabled = state.IsEnabled,
            IsPaused = state.IsPaused,
            IsBlockedByGlobalPause =
                state.IsBlockedByGlobalPause,
            FixedUnavailablePolicy = state.FixedUnavailablePolicy,
            FixedUnavailableNotificationPolicy =
                state.FixedUnavailableNotificationPolicy,
            ScheduledUnavailableNotificationPolicy =
                state.ScheduledUnavailableNotificationPolicy,
            FrozenRemaining =
                state.FrozenRemaining ?? TimeSpan.Zero,
            IsExpired = state.IsExpired,
            ShowExpiredEasterEgg =
                state.IsExpired && Random.Shared.Next(100) < 5
        };

        if (!state.IsEnabled || state.IsPaused)
        {
            reminderEvent.DueAt = null;
            return true;
        }

        if (schedule is FixedIntervalSchedule)
        {
            reminderEvent.DueAt =
                now + reminderEvent.FrozenRemaining;
            return true;
        }

        if (state.DeferredOccurrenceAt is not null)
        {
            reminderEvent.ActiveOccurrenceAt =
                state.DeferredOccurrenceAt;
            reminderEvent.DueAt =
                now + state.FrozenRemaining!.Value;
            return true;
        }

        var scheduledTime = (ScheduledTimeSchedule)schedule;
        var nextOccurrence =
            state.NextScheduledOccurrenceAt ??
            ScheduledTimeCalculator.GetNextOccurrence(
                scheduledTime.Settings,
                savedAt,
                inclusive: true);
        if (nextOccurrence is not null &&
            nextOccurrence.Value <= now)
        {
            becameMissed = true;
            nextOccurrence =
                scheduledTime.Settings.Recurrence ==
                    ReminderRecurrence.Once
                    ? null
                    : ScheduledTimeCalculator.GetNextOccurrence(
                        scheduledTime.Settings,
                        now,
                        inclusive: false);
        }

        reminderEvent.DueAt = nextOccurrence;
        reminderEvent.FrozenRemaining =
            nextOccurrence is null
                ? TimeSpan.Zero
                : nextOccurrence.Value - now;
        return true;
    }

    private static TimeSpan ClampRemaining(
        TimeSpan value,
        TimeSpan maximum)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value > maximum ? maximum : value;
    }
}
