using System.Collections.Concurrent;
using Microsoft.Toolkit.Uwp.Notifications;
using Reminder.App.Logic.Services;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Reminder.App.Windows.Notifications;

public sealed class WindowsToastNotificationService : IReminderNotificationService
{
    private const string ToastGroup = "Reminder";
    private readonly ConcurrentDictionary<Guid, TrackedNotification> _notifications = new();
    private bool _disposed;
    private bool _isAvailable = true;
    private string _statusMessage = "Windows 通知已就绪";

    public WindowsToastNotificationService()
    {
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }

    public event EventHandler<ReminderNotificationResponse>? ResponseReceived;

    public bool IsAvailable => _isAvailable;

    public string StatusMessage => _statusMessage;

    public bool Show(ReminderNotificationRequest request)
    {
        if (_disposed)
        {
            return false;
        }

        try
        {
            var tag = CreateTag(request.NotificationId);
            var content = new ToastContentBuilder()
                .AddArgument("eventId", request.EventId.ToString("N"))
                .AddArgument("notificationId", request.NotificationId.ToString("N"))
                .AddArgument("action", "reopen")
                .AddText("Reminder")
                .AddText($"该休息一下了：{request.EventName}")
                .AddText("可以直接在这里处理本次提醒。")
                .AddButton(CreateActionButton("完成", "complete", request))
                .AddButton(CreateActionButton("延迟", "snooze", request))
                .AddButton(CreateActionButton("跳过本次", "skip", request))
                .SetToastDuration(ToastDuration.Long)
                .GetToastContent();

            var contentXml = content.GetXml();
            var tracked = new TrackedNotification(request, tag, contentXml.GetXml());
            _notifications[request.NotificationId] = tracked;
            tracked.RestartAutoDismiss(
                request.VisibleDuration,
                () => AutoDismiss(request.NotificationId));

            var toast = CreateToast(tracked, suppressPopup: false);
            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
            _isAvailable = true;
            _statusMessage = "Windows 通知已就绪";
            return true;
        }
        catch (Exception exception)
        {
            if (_notifications.TryRemove(request.NotificationId, out var tracked))
            {
                tracked.Dispose();
            }

            _isAvailable = false;
            _statusMessage = $"Windows 通知暂不可用：{exception.Message}";
            return false;
        }
    }

    public void Remove(Guid notificationId)
    {
        if (!_notifications.TryRemove(notificationId, out var tracked))
        {
            return;
        }

        tracked.Dispose();
        try
        {
            ToastNotificationManagerCompat.History.Remove(tracked.Tag, ToastGroup);
        }
        catch
        {
            // Removing a notification is best-effort. The active business state has
            // already invalidated this notification, so a late callback is ignored.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ToastNotificationManagerCompat.OnActivated -= OnToastActivated;

        foreach (var notificationId in _notifications.Keys)
        {
            Remove(notificationId);
        }
    }

    private static ToastButton CreateActionButton(
        string label,
        string action,
        ReminderNotificationRequest request)
    {
        return new ToastButton()
            .SetContent(label)
            .AddArgument("eventId", request.EventId.ToString("N"))
            .AddArgument("notificationId", request.NotificationId.ToString("N"))
            .AddArgument("action", action)
            .SetBackgroundActivation();
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        var toastArguments = ToastArguments.Parse(args.Argument);
        if (!toastArguments.TryGetValue("eventId", out var eventIdText) ||
            !toastArguments.TryGetValue("notificationId", out var notificationIdText) ||
            !Guid.TryParseExact(eventIdText, "N", out var eventId) ||
            !Guid.TryParseExact(notificationIdText, "N", out var notificationId) ||
            !_notifications.TryGetValue(notificationId, out var tracked) ||
            tracked.Request.EventId != eventId)
        {
            return;
        }

        toastArguments.TryGetValue("action", out var actionText);
        if (string.Equals(actionText, "reopen", StringComparison.Ordinal))
        {
            Reopen(tracked);
            return;
        }

        var action = actionText switch
        {
            "complete" => ReminderNotificationAction.Complete,
            "snooze" => ReminderNotificationAction.Snooze,
            _ => ReminderNotificationAction.Skip
        };

        Complete(tracked.Request, action, DateTimeOffset.Now);
    }

    private void OnToastDismissed(
        ReminderNotificationRequest request,
        ToastDismissalReason reason)
    {
        var action = reason switch
        {
            ToastDismissalReason.UserCanceled => ReminderNotificationAction.UserClosed,
            ToastDismissalReason.TimedOut => ReminderNotificationAction.TimedOut,
            _ => (ReminderNotificationAction?)null
        };

        if (action == ReminderNotificationAction.TimedOut)
        {
            ReportTimedOut(request);
        }
        else if (action is not null)
        {
            Complete(request, action.Value, DateTimeOffset.Now);
        }
    }

    private void Complete(
        ReminderNotificationRequest request,
        ReminderNotificationAction action,
        DateTimeOffset occurredAt)
    {
        if (!_notifications.TryRemove(request.NotificationId, out var tracked))
        {
            return;
        }

        tracked.Dispose();

        ResponseReceived?.Invoke(
            this,
            new ReminderNotificationResponse(
                request.EventId,
                request.NotificationId,
                action,
                occurredAt));
    }

    private static string CreateTag(Guid notificationId)
    {
        return notificationId.ToString("N")[..16];
    }

    private void AutoDismiss(Guid notificationId)
    {
        if (!_notifications.TryGetValue(notificationId, out var tracked))
        {
            return;
        }

        var isFirstTimeout = tracked.TryMarkTimedOut();
        tracked.StopAutoDismiss();
        try
        {
            var notificationCenterToast = CreateToast(tracked, suppressPopup: true);
            ToastNotificationManagerCompat.CreateToastNotifier().Show(notificationCenterToast);
        }
        catch (Exception exception)
        {
            _isAvailable = false;
            _statusMessage = $"通知中心保留失败：{exception.Message}";
        }

        if (!IsCurrent(tracked))
        {
            RemoveFromHistory(tracked);
            return;
        }

        if (isFirstTimeout)
        {
            RaiseTimedOut(tracked.Request);
        }
    }

    private void Reopen(TrackedNotification tracked)
    {
        if (_disposed || !IsCurrent(tracked))
        {
            return;
        }

        tracked.StopAutoDismiss();
        try
        {
            var toast = CreateToast(tracked, suppressPopup: false);
            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
            if (!IsCurrent(tracked))
            {
                RemoveFromHistory(tracked);
                return;
            }

            tracked.RestartAutoDismiss(
                tracked.Request.VisibleDuration,
                () => AutoDismiss(tracked.Request.NotificationId));
            _isAvailable = true;
            _statusMessage = "Windows 通知已就绪";
        }
        catch (Exception exception)
        {
            _isAvailable = false;
            _statusMessage = $"重新显示通知失败：{exception.Message}";
        }
    }

    private ToastNotification CreateToast(
        TrackedNotification tracked,
        bool suppressPopup)
    {
        var document = new XmlDocument();
        document.LoadXml(tracked.ContentXml);
        var toast = new ToastNotification(document)
        {
            Tag = tracked.Tag,
            Group = ToastGroup,
            SuppressPopup = suppressPopup
        };

        toast.Dismissed += (_, args) =>
            OnToastDismissed(tracked.Request, args.Reason);
        toast.Failed += (_, _) => Complete(
            tracked.Request,
            ReminderNotificationAction.DeliveryFailed,
            DateTimeOffset.Now);
        return toast;
    }

    private void ReportTimedOut(ReminderNotificationRequest request)
    {
        if (!_notifications.TryGetValue(request.NotificationId, out var tracked) ||
            !tracked.TryMarkTimedOut())
        {
            return;
        }

        tracked.StopAutoDismiss();
        RaiseTimedOut(request);
    }

    private void RaiseTimedOut(ReminderNotificationRequest request)
    {
        ResponseReceived?.Invoke(
            this,
            new ReminderNotificationResponse(
                request.EventId,
                request.NotificationId,
                ReminderNotificationAction.TimedOut,
                DateTimeOffset.Now));
    }

    private bool IsCurrent(TrackedNotification tracked)
    {
        return _notifications.TryGetValue(
                   tracked.Request.NotificationId,
                   out var current) &&
               ReferenceEquals(current, tracked);
    }

    private static void RemoveFromHistory(TrackedNotification tracked)
    {
        try
        {
            ToastNotificationManagerCompat.History.Remove(tracked.Tag, ToastGroup);
        }
        catch
        {
            // A concurrent business action already invalidated the notification.
        }
    }

    private sealed class TrackedNotification(
        ReminderNotificationRequest request,
        string tag,
        string contentXml) : IDisposable
    {
        private readonly object _timerGate = new();
        private System.Threading.Timer? _autoDismissTimer;
        private bool _disposed;
        private int _timedOut;

        public ReminderNotificationRequest Request { get; } = request;

        public string Tag { get; } = tag;

        public string ContentXml { get; } = contentXml;

        public bool RestartAutoDismiss(TimeSpan delay, Action callback)
        {
            lock (_timerGate)
            {
                if (_disposed)
                {
                    return false;
                }

                _autoDismissTimer?.Dispose();
                _autoDismissTimer = new System.Threading.Timer(
                    _ => callback(),
                    null,
                    delay,
                    Timeout.InfiniteTimeSpan);
                return true;
            }
        }

        public bool TryMarkTimedOut()
        {
            return Interlocked.CompareExchange(ref _timedOut, 1, 0) == 0;
        }

        public void StopAutoDismiss()
        {
            lock (_timerGate)
            {
                _autoDismissTimer?.Dispose();
                _autoDismissTimer = null;
            }
        }

        public void Dispose()
        {
            lock (_timerGate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _autoDismissTimer?.Dispose();
                _autoDismissTimer = null;
            }
        }
    }
}
