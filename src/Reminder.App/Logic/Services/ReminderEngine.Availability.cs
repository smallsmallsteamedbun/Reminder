using Reminder.App.Logic.Models;
using Reminder.App.Logic.Scheduling;

namespace Reminder.App.Logic.Services;

public sealed partial class ReminderEngine
{
    private DateTimeOffset Now =>
        TimeZoneInfo.ConvertTime(
            _timeProvider.GetUtcNow(),
            _timeProvider.LocalTimeZone);

    public ReminderGlobalPauseSnapshot GetGlobalPauseSnapshot(
        DateTimeOffset now)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var remaining = _globalPauseEndsAt - now;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            return new ReminderGlobalPauseSnapshot
            {
                IsPaused = _isGlobalPaused,
                Duration = _globalPauseDuration,
                EndsAt = _globalPauseEndsAt,
                Remaining = _isGlobalPaused &&
                            _globalPauseEndsAt is not null
                    ? remaining
                    : null
            };
        }
    }

    public void PauseAll()
    {
        PauseAll(ReminderGlobalPauseDuration.UntilManualResume);
    }

    public void PauseAll(ReminderGlobalPauseDuration duration)
    {
        List<Guid> notificationsToRemove = [];
        lock (_gate)
        {
            ThrowIfDisposed();
            var now = Now;
            if (!_isGlobalPaused)
            {
                _isGlobalPaused = true;
                foreach (var reminderEvent in _events.Where(
                             item => item.IsEnabled))
                {
                    reminderEvent.IsBlockedByGlobalPause = true;
                    if (reminderEvent.Schedule is FixedIntervalSchedule)
                    {
                        AddFixedClockBlockLocked(
                            reminderEvent,
                            FixedClockBlockReason.GlobalPause,
                            now,
                            resetToFullInterval: false,
                            notificationsToRemove);
                    }
                    else
                    {
                        SuppressActiveScheduledNotificationLocked(
                            reminderEvent,
                            now,
                            notificationsToRemove);
                    }
                }
            }

            SetGlobalPauseDurationLocked(duration, now);
            RescheduleLocked(now);
        }

        RemoveNotifications(notificationsToRemove);
        RaiseStateChanged();
        RaiseDurableStateChanged();
    }

    public void SetGlobalPauseDuration(
        ReminderGlobalPauseDuration duration)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_isGlobalPaused)
            {
                return;
            }

            var now = Now;
            SetGlobalPauseDurationLocked(duration, now);
            RescheduleLocked(now);
        }

        RaiseStateChanged();
        RaiseDurableStateChanged();
    }

    public void ResumeAll()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var now = Now;
            ResumeAllLocked(now);
            RescheduleLocked(now);
        }

        RaiseStateChanged();
        RaiseDurableStateChanged();
    }

    private void ResumeAllLocked(DateTimeOffset now)
    {
        if (_isGlobalPaused)
        {
            ProcessSuppressedDueEventsLocked(
                now,
                strictlyBefore: true,
                forceSuppressAll: false);
            EndGlobalPauseLocked(now);
            return;
        }

        foreach (var reminderEvent in _events.Where(
                     item => item.IsEnabled &&
                             item.IsPaused &&
                             item.Schedule is FixedIntervalSchedule)
                 .ToArray())
        {
            ResumeLocked(reminderEvent, now);
        }
    }

    public bool UpdateFixedUnavailablePolicy(
        Guid eventId,
        FixedUnavailablePolicy policy)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent?.Schedule is not FixedIntervalSchedule)
            {
                return false;
            }

            if (reminderEvent.FixedUnavailablePolicy == policy)
            {
                return true;
            }

            var now = Now;
            reminderEvent.FixedUnavailablePolicy = policy;
            ActivateAfterEditLocked(reminderEvent, now);
            ReconcileFixedSystemBlockLocked(reminderEvent, now);
            RescheduleLocked(now);
        }

        RaiseStateChanged();
        RaiseDurableStateChanged();
        return true;
    }

    public bool UpdateFixedUnavailableNotificationPolicy(
        Guid eventId,
        UnavailableNotificationPolicy policy)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent?.Schedule is not FixedIntervalSchedule)
            {
                return false;
            }

            if (reminderEvent.FixedUnavailableNotificationPolicy == policy)
            {
                return true;
            }

            var now = Now;
            reminderEvent.FixedUnavailableNotificationPolicy = policy;
            ActivateAfterEditLocked(reminderEvent, now);
            ReconcileFixedSystemBlockLocked(reminderEvent, now);
            RescheduleLocked(now);
        }

        RaiseStateChanged();
        RaiseDurableStateChanged();
        return true;
    }

    public bool UpdateScheduledUnavailableNotificationPolicy(
        Guid eventId,
        UnavailableNotificationPolicy policy)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent?.Schedule is not ScheduledTimeSchedule)
            {
                return false;
            }

            if (reminderEvent.ScheduledUnavailableNotificationPolicy == policy)
            {
                return true;
            }

            var now = Now;
            reminderEvent.ScheduledUnavailableNotificationPolicy = policy;
            ActivateAfterEditLocked(reminderEvent, now);
            RescheduleLocked(now);
        }

        RaiseStateChanged();
        RaiseDurableStateChanged();
        return true;
    }

    public void UpdateSystemState(ReminderSystemState state)
    {
        UpdateSystemState(state, Now);
    }

    public void UpdateSystemState(
        ReminderSystemState state,
        DateTimeOffset occurredAt)
    {
        List<Guid> notificationsToRemove = [];
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_systemState == state)
            {
                return;
            }

            var previous = _systemState;
            var enteringLock =
                !previous.IsSessionLocked && state.IsSessionLocked;
            var enteringSleep =
                !previous.IsSleeping && state.IsSleeping;
            var leavingSleep =
                previous.IsSleeping && !state.IsSleeping;
            var wasScreenOrLockUnavailable =
                previous.IsScreenOrLockUnavailable;
            var isScreenOrLockUnavailable =
                state.IsScreenOrLockUnavailable;

            if (enteringLock || enteringSleep)
            {
                DelayActiveNotificationsLocked(
                    occurredAt,
                    notificationsToRemove);
            }

            if (enteringLock)
            {
                foreach (var reminderEvent in _events.Where(
                             item => item
                                 .SystemBlockInterruptedActiveNotification))
                {
                    reminderEvent.FrozenRemaining =
                        GetSnoozeDuration(reminderEvent);
                    reminderEvent
                        .SystemBlockInterruptedActiveNotification = false;
                }
            }

            if (enteringSleep)
            {
                foreach (var reminderEvent in _events.Where(
                             item => item.Schedule is FixedIntervalSchedule))
                {
                    if (reminderEvent
                        .SystemBlockInterruptedActiveNotification)
                    {
                        reminderEvent.FrozenRemaining =
                            GetSnoozeDuration(reminderEvent);
                        reminderEvent
                            .SystemBlockInterruptedActiveNotification =
                            false;
                    }

                    RemoveFixedClockBlockLocked(
                        reminderEvent,
                        FixedClockBlockReason.SystemUnavailable,
                        occurredAt);
                }
            }

            if (leavingSleep)
            {
                if (_isGlobalPaused &&
                    _globalPauseEndsAt is not null &&
                    _globalPauseEndsAt.Value <= occurredAt)
                {
                    var pauseEndedAt = _globalPauseEndsAt.Value;
                    ProcessSuppressedDueEventsLocked(
                        pauseEndedAt,
                        strictlyBefore: true,
                        forceSuppressAll: false);
                    EndGlobalPauseLocked(pauseEndedAt);
                }

                ProcessSuppressedDueEventsLocked(
                    occurredAt,
                    strictlyBefore: true,
                    forceSuppressAll: true);
            }

            if (!leavingSleep &&
                wasScreenOrLockUnavailable &&
                !isScreenOrLockUnavailable)
            {
                ProcessSuppressedDueEventsLocked(
                    occurredAt,
                    strictlyBefore: true,
                    forceSuppressAll: false);
            }

            _systemState = state;

            if (leavingSleep)
            {
                if (isScreenOrLockUnavailable)
                {
                    foreach (var reminderEvent in _events.Where(
                                 item => item.IsEnabled &&
                                         item.Schedule is
                                             FixedIntervalSchedule))
                    {
                        ApplyFixedSystemBlockLocked(
                            reminderEvent,
                            occurredAt,
                            notificationsToRemove);
                    }
                }
            }
            else if (!state.IsSleeping)
            {
                if (!wasScreenOrLockUnavailable &&
                    isScreenOrLockUnavailable)
                {
                    foreach (var reminderEvent in _events.Where(
                                 item => item.IsEnabled &&
                                         item.Schedule is
                                             FixedIntervalSchedule))
                    {
                        ApplyFixedSystemBlockLocked(
                            reminderEvent,
                            occurredAt,
                            notificationsToRemove);
                    }
                }
                else if (wasScreenOrLockUnavailable &&
                         !isScreenOrLockUnavailable)
                {
                    foreach (var reminderEvent in _events.Where(
                                 item => item.Schedule is
                                     FixedIntervalSchedule))
                    {
                        RemoveFixedClockBlockLocked(
                            reminderEvent,
                            FixedClockBlockReason.SystemUnavailable,
                            occurredAt);
                    }
                }
            }

            RescheduleLocked(occurredAt);
        }

        RemoveNotifications(notificationsToRemove);
        RaiseStateChanged();
    }

    private void SetGlobalPauseDurationLocked(
        ReminderGlobalPauseDuration duration,
        DateTimeOffset now)
    {
        _globalPauseDuration = duration;
        var pauseLength = duration.ToTimeSpan();
        _globalPauseEndsAt = pauseLength is null
            ? null
            : now + pauseLength.Value;
    }

    private void EndGlobalPauseLocked(DateTimeOffset now)
    {
        _isGlobalPaused = false;
        _globalPauseEndsAt = null;
        foreach (var reminderEvent in _events)
        {
            reminderEvent.IsBlockedByGlobalPause = false;
            if (reminderEvent.IsEnabled)
            {
                reminderEvent.IsPaused = false;
                reminderEvent.IsExpired = false;
                reminderEvent.ShowExpiredEasterEgg = false;
            }

            if (reminderEvent.Schedule is FixedIntervalSchedule)
            {
                RemoveFixedClockBlockLocked(
                    reminderEvent,
                    FixedClockBlockReason.GlobalPause,
                    now);
            }
        }
    }

    private void ReconcileGlobalPauseAfterParticipantChangeLocked(
        DateTimeOffset now)
    {
        if (!_isGlobalPaused)
        {
            return;
        }

        var hasPausedEvent = _events.Any(item =>
            item.IsEnabled &&
            (item.IsPaused || item.IsBlockedByGlobalPause));
        if (!hasPausedEvent)
        {
            EndGlobalPauseLocked(now);
        }
    }

    private void ReconcileFixedSystemBlockLocked(
        ReminderEvent reminderEvent,
        DateTimeOffset now)
    {
        RemoveFixedClockBlockLocked(
            reminderEvent,
            FixedClockBlockReason.SystemUnavailable,
            now);

        if (!_systemState.IsScreenOrLockUnavailable ||
            !reminderEvent.IsEnabled)
        {
            return;
        }

        ApplyFixedSystemBlockLocked(
            reminderEvent,
            now,
            notificationsToRemove: null);
    }

    private void ApplyFixedSystemBlockLocked(
        ReminderEvent reminderEvent,
        DateTimeOffset now,
        List<Guid>? notificationsToRemove)
    {
        switch (reminderEvent.FixedUnavailablePolicy)
        {
            case FixedUnavailablePolicy.ContinueTiming:
                RemoveFixedClockBlockLocked(
                    reminderEvent,
                    FixedClockBlockReason.SystemUnavailable,
                    now);
                break;
            case FixedUnavailablePolicy.RestartTiming:
                AddFixedClockBlockLocked(
                    reminderEvent,
                    FixedClockBlockReason.SystemUnavailable,
                    now,
                    resetToFullInterval: true,
                    notificationsToRemove);
                break;
            case FixedUnavailablePolicy.PauseTiming:
                AddFixedClockBlockLocked(
                    reminderEvent,
                    FixedClockBlockReason.SystemUnavailable,
                    now,
                    resetToFullInterval: false,
                    notificationsToRemove);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(reminderEvent.FixedUnavailablePolicy),
                    reminderEvent.FixedUnavailablePolicy,
                    "不支持的息屏和锁屏策略");
        }
    }

    private static void AddFixedClockBlockLocked(
        ReminderEvent reminderEvent,
        FixedClockBlockReason reason,
        DateTimeOffset now,
        bool resetToFullInterval,
        List<Guid>? notificationsToRemove)
    {
        if (reminderEvent.Schedule is not FixedIntervalSchedule fixedInterval)
        {
            return;
        }

        if ((reminderEvent.FixedClockBlockReasons & reason) != 0)
        {
            if (resetToFullInterval)
            {
                reminderEvent.FrozenRemaining = fixedInterval.Interval;
            }

            return;
        }

        if (reason == FixedClockBlockReason.SystemUnavailable &&
            reminderEvent.ActiveNotificationId is not null &&
            !reminderEvent.IsNotificationDeferred)
        {
            reminderEvent.SystemBlockInterruptedActiveNotification = true;
        }

        if (reminderEvent.FixedClockBlockReasons ==
                FixedClockBlockReason.None &&
            !reminderEvent.IsPaused)
        {
            if (resetToFullInterval)
            {
                reminderEvent.FrozenRemaining = fixedInterval.Interval;
            }
            else if (reminderEvent.DueAt is not null)
            {
                reminderEvent.FrozenRemaining =
                    reminderEvent.DueAt.Value - now;
                if (reminderEvent.FrozenRemaining < TimeSpan.Zero)
                {
                    reminderEvent.FrozenRemaining = TimeSpan.Zero;
                }
            }
            else if (reminderEvent.ActiveNotificationId is not null &&
                     !reminderEvent.IsNotificationDeferred)
            {
                reminderEvent.FrozenRemaining = TimeSpan.Zero;
            }
        }
        else if (resetToFullInterval)
        {
            reminderEvent.FrozenRemaining = fixedInterval.Interval;
        }

        if (reminderEvent.ActiveNotificationId is { } notificationId)
        {
            notificationsToRemove?.Add(notificationId);
        }

        reminderEvent.DueAt = null;
        ClearNotificationStateLocked(reminderEvent);
        reminderEvent.FixedClockBlockReasons |= reason;
    }

    private static void RemoveFixedClockBlockLocked(
        ReminderEvent reminderEvent,
        FixedClockBlockReason reason,
        DateTimeOffset now)
    {
        if ((reminderEvent.FixedClockBlockReasons & reason) == 0)
        {
            return;
        }

        reminderEvent.FixedClockBlockReasons &= ~reason;
        if (reason == FixedClockBlockReason.SystemUnavailable)
        {
            reminderEvent.SystemBlockInterruptedActiveNotification = false;
        }

        if (reminderEvent.Schedule is not FixedIntervalSchedule ||
            !reminderEvent.IsEnabled ||
            reminderEvent.IsPaused ||
            reminderEvent.FixedClockBlockReasons !=
                FixedClockBlockReason.None)
        {
            return;
        }

        ResumeFixedClockLocked(reminderEvent, now);
    }

    private static void ResumeFixedClockLocked(
        ReminderEvent reminderEvent,
        DateTimeOffset now)
    {
        if (reminderEvent.Schedule is not FixedIntervalSchedule fixedInterval)
        {
            return;
        }

        var remaining = reminderEvent.FrozenRemaining;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }
        else if (remaining == TimeSpan.Zero)
        {
            remaining = fixedInterval.Interval;
        }

        reminderEvent.FrozenRemaining = remaining;
        reminderEvent.DueAt = now + remaining;
        reminderEvent.IsExpired = false;
    }

    private void SuppressActiveScheduledNotificationLocked(
        ReminderEvent reminderEvent,
        DateTimeOffset now,
        List<Guid> notificationsToRemove)
    {
        if (reminderEvent.ActiveNotificationId is not { } notificationId)
        {
            return;
        }

        notificationsToRemove.Add(notificationId);
        if (reminderEvent.IsNotificationDeferred &&
            reminderEvent.DueAt is not null)
        {
            ClearActiveNotificationKeepingOccurrenceLocked(reminderEvent);
            return;
        }

        var occurrenceAt =
            reminderEvent.ActiveOccurrenceAt ??
            reminderEvent.NotificationShownAt ??
            now;
        ClearNotificationStateLocked(reminderEvent);
        reminderEvent.DueAt = occurrenceAt;
        ProcessMissedOccurrenceLocked(
            reminderEvent,
            now,
            strictlyBefore: false);
    }

    private void DelayActiveNotificationsLocked(
        DateTimeOffset now,
        List<Guid> notificationsToRemove)
    {
        foreach (var reminderEvent in _events.Where(
                     item => item.ActiveNotificationId is not null))
        {
            var notificationId =
                reminderEvent.ActiveNotificationId!.Value;
            notificationsToRemove.Add(notificationId);

            if (!reminderEvent.IsNotificationDeferred ||
                reminderEvent.DueAt is null)
            {
                var snoozeDuration = GetSnoozeDuration(reminderEvent);
                reminderEvent.DueAt = now + snoozeDuration;
                reminderEvent.FrozenRemaining = snoozeDuration;
            }

            ClearActiveNotificationKeepingOccurrenceLocked(reminderEvent);
        }
    }

    private static void ClearActiveNotificationKeepingOccurrenceLocked(
        ReminderEvent reminderEvent)
    {
        reminderEvent.ActiveNotificationId = null;
        reminderEvent.IsNotificationDeferred = false;
        reminderEvent.NotificationShownAt = null;
    }

    private bool IsOrdinaryNotificationAllowedLocked(
        ReminderEvent reminderEvent)
    {
        if (!reminderEvent.IsEnabled ||
            reminderEvent.IsPaused ||
            reminderEvent.IsBlockedByGlobalPause ||
            _systemState.IsSleeping)
        {
            return false;
        }

        if (!_systemState.IsScreenOrLockUnavailable)
        {
            return true;
        }

        return reminderEvent.Schedule switch
        {
            FixedIntervalSchedule =>
                reminderEvent.FixedUnavailablePolicy ==
                    FixedUnavailablePolicy.ContinueTiming &&
                reminderEvent.FixedUnavailableNotificationPolicy ==
                    UnavailableNotificationPolicy.Notify,
            ScheduledTimeSchedule =>
                reminderEvent.ScheduledUnavailableNotificationPolicy ==
                    UnavailableNotificationPolicy.Notify,
            _ => false
        };
    }

    private void ProcessSuppressedDueEventsLocked(
        DateTimeOffset cutoff,
        bool strictlyBefore,
        bool forceSuppressAll)
    {
        foreach (var reminderEvent in _events.Where(
                     item => item.IsEnabled &&
                             !item.IsPaused &&
                             item.DueAt is not null &&
                             IsDueForSuppression(
                                 item.DueAt.Value,
                                 cutoff,
                                 strictlyBefore))
                 .ToArray())
        {
            if (!forceSuppressAll &&
                IsOrdinaryNotificationAllowedLocked(reminderEvent))
            {
                continue;
            }

            ProcessMissedOccurrenceLocked(
                reminderEvent,
                cutoff,
                strictlyBefore);
        }
    }

    private void ProcessMissedOccurrenceLocked(
        ReminderEvent reminderEvent,
        DateTimeOffset cutoff,
        bool strictlyBefore)
    {
        if (reminderEvent.DueAt is null ||
            !IsDueForSuppression(
                reminderEvent.DueAt.Value,
                cutoff,
                strictlyBefore))
        {
            return;
        }

        _pendingMissedEventIds.Add(reminderEvent.Id);
        ClearNotificationStateLocked(reminderEvent);

        if (reminderEvent.Schedule is FixedIntervalSchedule fixedInterval)
        {
            reminderEvent.DueAt = GetFirstFixedOccurrenceAfterCutoff(
                reminderEvent.DueAt.Value,
                fixedInterval.Interval,
                cutoff,
                strictlyBefore);
            reminderEvent.FrozenRemaining =
                reminderEvent.DueAt.Value - cutoff;
            return;
        }

        var schedule = (ScheduledTimeSchedule)reminderEvent.Schedule;
        if (schedule.Settings.Recurrence == ReminderRecurrence.Once)
        {
            reminderEvent.DueAt = null;
            reminderEvent.FrozenRemaining = TimeSpan.Zero;
            return;
        }

        reminderEvent.DueAt = ScheduledTimeCalculator.GetNextOccurrence(
            schedule.Settings,
            cutoff,
            inclusive: strictlyBefore);
        reminderEvent.FrozenRemaining =
            reminderEvent.DueAt is null
                ? TimeSpan.Zero
                : reminderEvent.DueAt.Value - cutoff;
    }

    private static bool IsDueForSuppression(
        DateTimeOffset dueAt,
        DateTimeOffset cutoff,
        bool strictlyBefore)
    {
        return strictlyBefore
            ? dueAt < cutoff
            : dueAt <= cutoff;
    }

    private static DateTimeOffset GetFirstFixedOccurrenceAfterCutoff(
        DateTimeOffset occurrenceAt,
        TimeSpan interval,
        DateTimeOffset cutoff,
        bool allowOccurrenceAtCutoff)
    {
        var elapsed = cutoff - occurrenceAt;
        if (elapsed < TimeSpan.Zero)
        {
            return occurrenceAt;
        }

        var intervalTicks = interval.Ticks;
        var elapsedTicks = elapsed.Ticks;
        var steps = allowOccurrenceAtCutoff
            ? (elapsedTicks + intervalTicks - 1) / intervalTicks
            : elapsedTicks / intervalTicks + 1;
        steps = Math.Max(1, steps);

        try
        {
            return occurrenceAt.AddTicks(
                checked(steps * intervalTicks));
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.MaxValue;
        }
        catch (OverflowException)
        {
            return DateTimeOffset.MaxValue;
        }
    }

    private List<string> TakeReadyMissedEventNamesLocked()
    {
        if (_isGlobalPaused ||
            _systemState.IsSleeping ||
            _systemState.IsScreenOrLockUnavailable ||
            _pendingMissedEventIds.Count == 0)
        {
            return [];
        }

        List<string> names = [];
        foreach (var eventId in _pendingMissedEventIds.ToArray())
        {
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null || !reminderEvent.IsEnabled)
            {
                _pendingMissedEventIds.Remove(eventId);
                continue;
            }

            if (reminderEvent.IsPaused ||
                reminderEvent.FixedClockBlockReasons !=
                    FixedClockBlockReason.None)
            {
                continue;
            }

            names.Add(reminderEvent.Name);
            _pendingMissedEventIds.Remove(eventId);

            var isOneTime = reminderEvent.Schedule is ScheduledTimeSchedule
            {
                Settings.Recurrence: ReminderRecurrence.Once
            };
            if (isOneTime ||
                reminderEvent.Termination.ConsumeOccurrence())
            {
                CloseEventLocked(reminderEvent);
            }
        }

        return names;
    }

    private bool IsBlockedBySystemStateLocked(
        ReminderEvent reminderEvent)
    {
        if (_systemState.IsSleeping)
        {
            return true;
        }

        if (!_systemState.IsScreenOrLockUnavailable)
        {
            return false;
        }

        return reminderEvent.Schedule switch
        {
            FixedIntervalSchedule =>
                reminderEvent.FixedUnavailablePolicy !=
                    FixedUnavailablePolicy.ContinueTiming ||
                reminderEvent.FixedUnavailableNotificationPolicy ==
                    UnavailableNotificationPolicy.Suppress,
            ScheduledTimeSchedule =>
                reminderEvent.ScheduledUnavailableNotificationPolicy ==
                    UnavailableNotificationPolicy.Suppress,
            _ => true
        };
    }

    private void RemoveNotifications(IEnumerable<Guid> notificationIds)
    {
        foreach (var notificationId in notificationIds.Distinct())
        {
            _notificationService.Remove(notificationId);
        }
    }
}
