using Reminder.App.Logic.Models;
using Reminder.App.Logic.Scheduling;
using Reminder.App.SystemModule.AppInfo;

namespace Reminder.App.Logic.Services;

public sealed class ReminderEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly List<ReminderEvent> _events = [];
    private readonly IReminderNotificationService _notificationService;
    private readonly ReminderScheduler _scheduler;
    private bool _disposed;

    public ReminderEngine(IReminderNotificationService notificationService)
    {
        _notificationService = notificationService;
        _notificationService.ResponseReceived += OnNotificationResponseReceived;
        _scheduler = new ReminderScheduler(OnSchedulerElapsed);
    }

    public event EventHandler? StateChanged;

    public bool NotificationsAvailable => _notificationService.IsAvailable;

    public string NotificationStatus => _notificationService.StatusMessage;

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
            RescheduleLocked(DateTimeOffset.Now);
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
            RescheduleLocked(DateTimeOffset.Now);
        }

        RaiseStateChanged();
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

        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null)
            {
                return false;
            }

            reminderEvent.Name = validatedName;
            EnableAfterEditLocked(reminderEvent);
            RescheduleLocked(DateTimeOffset.Now);
        }

        RaiseStateChanged();
        return true;
    }

    public bool UpdateInterval(Guid eventId, int intervalMinutes)
    {
        if (intervalMinutes is < ReminderDefaults.MinimumIntervalMinutes or > ReminderDefaults.MaximumIntervalMinutes)
        {
            return false;
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null)
            {
                return false;
            }

            reminderEvent.Interval = TimeSpan.FromMinutes(intervalMinutes);
            reminderEvent.FrozenRemaining = reminderEvent.Interval;
            EnableAfterEditLocked(reminderEvent);

            if (reminderEvent.IsEnabled && !reminderEvent.IsPaused)
            {
                reminderEvent.DueAt = DateTimeOffset.Now + reminderEvent.Interval;
            }

            RescheduleLocked(DateTimeOffset.Now);
        }

        RaiseStateChanged();
        return true;
    }

    public void TogglePause(Guid eventId)
    {
        Guid? notificationToRemove = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null || !reminderEvent.IsEnabled)
            {
                return;
            }

            if (reminderEvent.IsPaused)
            {
                ResumeLocked(reminderEvent, DateTimeOffset.Now);
            }
            else
            {
                notificationToRemove = PauseLocked(reminderEvent, DateTimeOffset.Now);
            }

            RescheduleLocked(DateTimeOffset.Now);
        }

        RemoveNotification(notificationToRemove);
        RaiseStateChanged();
    }

    public void Restart(Guid eventId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null ||
                !reminderEvent.IsEnabled ||
                reminderEvent.IsPaused ||
                reminderEvent.ActiveNotificationId is not null)
            {
                return;
            }

            reminderEvent.FrozenRemaining = reminderEvent.Interval;
            reminderEvent.DueAt = DateTimeOffset.Now + reminderEvent.Interval;
            RescheduleLocked(DateTimeOffset.Now);
        }

        RaiseStateChanged();
    }

    public void SetEnabled(Guid eventId, bool isEnabled)
    {
        Guid? notificationToRemove = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            var reminderEvent = FindEventLocked(eventId);
            if (reminderEvent is null || reminderEvent.IsEnabled == isEnabled)
            {
                return;
            }

            if (isEnabled)
            {
                reminderEvent.IsEnabled = true;
                ResumeLocked(reminderEvent, DateTimeOffset.Now);
            }
            else
            {
                notificationToRemove = PauseLocked(reminderEvent, DateTimeOffset.Now);
                reminderEvent.IsEnabled = false;
            }

            RescheduleLocked(DateTimeOffset.Now);
        }

        RemoveNotification(notificationToRemove);
        RaiseStateChanged();
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
            _events.Remove(reminderEvent);
            RescheduleLocked(DateTimeOffset.Now);
        }

        RemoveNotification(notificationToRemove);
        RaiseStateChanged();
    }

    public void PauseAll()
    {
        List<Guid> notificationsToRemove = [];
        lock (_gate)
        {
            ThrowIfDisposed();
            var now = DateTimeOffset.Now;
            foreach (var reminderEvent in _events.Where(item => item.IsEnabled && !item.IsPaused))
            {
                var notificationId = PauseLocked(reminderEvent, now);
                if (notificationId is not null)
                {
                    notificationsToRemove.Add(notificationId.Value);
                }
            }

            RescheduleLocked(now);
        }

        foreach (var notificationId in notificationsToRemove)
        {
            _notificationService.Remove(notificationId);
        }

        RaiseStateChanged();
    }

    public void ResumeAll()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var now = DateTimeOffset.Now;
            foreach (var reminderEvent in _events.Where(item => item.IsEnabled && item.IsPaused))
            {
                ResumeLocked(reminderEvent, now);
            }

            RescheduleLocked(now);
        }

        RaiseStateChanged();
    }

    public void RestartAll()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var now = DateTimeOffset.Now;
            foreach (var reminderEvent in _events.Where(
                         item => item.IsEnabled &&
                                 !item.IsPaused &&
                                 item.ActiveNotificationId is null))
            {
                reminderEvent.FrozenRemaining = reminderEvent.Interval;
                reminderEvent.DueAt = now + reminderEvent.Interval;
            }

            RescheduleLocked(now);
        }

        RaiseStateChanged();
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
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        var reminderEvent = new ReminderEvent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Interval = interval,
            IsEnabled = isEnabled,
            IsPaused = !isEnabled,
            DueAt = isEnabled ? DateTimeOffset.Now + interval : null,
            FrozenRemaining = interval
        };

        _events.Add(reminderEvent);
        return reminderEvent.Id;
    }

    private static ReminderEventSnapshot CreateSnapshot(ReminderEvent reminderEvent, DateTimeOffset now)
    {
        var remaining = reminderEvent.DueAt is not null
            ? reminderEvent.DueAt.Value - now
            : reminderEvent.FrozenRemaining;

        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        return new ReminderEventSnapshot(
            reminderEvent.Id,
            reminderEvent.Name,
            checked((int)reminderEvent.Interval.TotalMinutes),
            reminderEvent.IsEnabled,
            reminderEvent.IsPaused,
            reminderEvent.ActiveNotificationId is not null &&
            !reminderEvent.IsNotificationDeferred,
            reminderEvent.IsEnabled &&
            !reminderEvent.IsPaused &&
            reminderEvent.ActiveNotificationId is null,
            remaining);
    }

    private void OnSchedulerElapsed()
    {
        List<ReminderNotificationRequest> requests = [];
        List<Guid> notificationsToReplace = [];
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var now = DateTimeOffset.Now;
            foreach (var reminderEvent in _events.Where(
                         item => item.IsEnabled &&
                                 !item.IsPaused &&
                                 (item.ActiveNotificationId is null ||
                                  item.IsNotificationDeferred) &&
                                 item.DueAt <= now))
            {
                if (reminderEvent.ActiveNotificationId is not null)
                {
                    notificationsToReplace.Add(reminderEvent.ActiveNotificationId.Value);
                }

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
                        ReminderDefaults.NotificationVisibleDuration));
            }

            RescheduleLocked(now);
        }

        foreach (var notificationId in notificationsToReplace)
        {
            _notificationService.Remove(notificationId);
        }

        if (requests.Count != 0)
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
                        DateTimeOffset.Now));
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

            var notificationShownAt = reminderEvent.NotificationShownAt ?? response.OccurredAt;
            var snoozeDuration = GetSnoozeDuration(reminderEvent);
            var isAutomaticTimeout = response.Action == ReminderNotificationAction.TimedOut;

            if (isAutomaticTimeout)
            {
                if (reminderEvent.IsNotificationDeferred)
                {
                    return;
                }

                reminderEvent.IsNotificationDeferred = true;
            }
            else
            {
                reminderEvent.ActiveNotificationId = null;
                reminderEvent.IsNotificationDeferred = false;
                reminderEvent.NotificationShownAt = null;
                shouldRemoveNotification = response.Action is
                    ReminderNotificationAction.Complete or
                    ReminderNotificationAction.Snooze or
                    ReminderNotificationAction.Skip;
            }

            if (!reminderEvent.IsEnabled || reminderEvent.IsPaused)
            {
                reminderEvent.DueAt = null;
                reminderEvent.FrozenRemaining =
                    response.Action is ReminderNotificationAction.Snooze or
                        ReminderNotificationAction.TimedOut
                        ? snoozeDuration
                        : reminderEvent.Interval;
            }
            else
            {
                var nextDueAt = response.Action switch
                {
                    ReminderNotificationAction.Complete =>
                        response.OccurredAt + reminderEvent.Interval,
                    ReminderNotificationAction.Snooze =>
                        response.OccurredAt + snoozeDuration,
                    ReminderNotificationAction.TimedOut =>
                        response.OccurredAt + snoozeDuration,
                    ReminderNotificationAction.Skip =>
                        notificationShownAt + reminderEvent.Interval,
                    ReminderNotificationAction.UserClosed =>
                        notificationShownAt + reminderEvent.Interval,
                    ReminderNotificationAction.DeliveryFailed =>
                        response.OccurredAt + snoozeDuration,
                    _ => response.OccurredAt + reminderEvent.Interval
                };

                reminderEvent.DueAt = nextDueAt;
                reminderEvent.FrozenRemaining = nextDueAt - response.OccurredAt;
            }

            RescheduleLocked(DateTimeOffset.Now);
        }

        if (shouldRemoveNotification)
        {
            _notificationService.Remove(response.NotificationId);
        }

        RaiseStateChanged();
    }

    private Guid? PauseLocked(ReminderEvent reminderEvent, DateTimeOffset now)
    {
        var notificationId = reminderEvent.ActiveNotificationId;
        if (reminderEvent.DueAt is not null)
        {
            reminderEvent.FrozenRemaining = reminderEvent.DueAt.Value - now;
            if (reminderEvent.FrozenRemaining < TimeSpan.Zero)
            {
                reminderEvent.FrozenRemaining = TimeSpan.Zero;
            }
        }
        else if (notificationId is not null)
        {
            reminderEvent.FrozenRemaining = TimeSpan.Zero;
        }

        reminderEvent.DueAt = null;
        reminderEvent.ActiveNotificationId = null;
        reminderEvent.IsNotificationDeferred = false;
        reminderEvent.NotificationShownAt = null;
        reminderEvent.IsPaused = true;
        return notificationId;
    }

    private static void ResumeLocked(ReminderEvent reminderEvent, DateTimeOffset now)
    {
        reminderEvent.IsPaused = false;
        var remaining = reminderEvent.FrozenRemaining;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        reminderEvent.DueAt = now + remaining;
    }

    private static void EnableAfterEditLocked(ReminderEvent reminderEvent)
    {
        if (!reminderEvent.IsEnabled)
        {
            reminderEvent.IsEnabled = true;
        }
    }

    private ReminderEvent? FindEventLocked(Guid eventId)
    {
        return _events.FirstOrDefault(item => item.Id == eventId);
    }

    private void RescheduleLocked(DateTimeOffset now)
    {
        var nextDueAt = _events
            .Where(item =>
                item.IsEnabled &&
                !item.IsPaused &&
                (item.ActiveNotificationId is null ||
                 item.IsNotificationDeferred) &&
                item.DueAt is not null)
            .Select(item => item.DueAt)
            .Min();

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
        return reminderEvent.Interval < ReminderDefaults.SnoozeDuration
            ? reminderEvent.Interval
            : ReminderDefaults.SnoozeDuration;
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
