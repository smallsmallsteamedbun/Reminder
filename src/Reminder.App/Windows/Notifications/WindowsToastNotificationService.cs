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
    private string _statusHelpMessage = "Windows 通知功能正常。";

    public WindowsToastNotificationService()
    {
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        RefreshAvailability();
    }

    public event EventHandler<ReminderNotificationResponse>? ResponseReceived;

    public bool IsAvailable => _isAvailable;

    public string StatusMessage => _statusMessage;

    public string StatusHelpMessage => _statusHelpMessage;

    private void RefreshAvailability()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _ = GetReadyNotifier();
        }
        catch (Exception exception)
        {
            MarkUnavailable(
                $"启动时读取 Windows 通知设置失败：{exception.Message}",
                "请确认 Windows“设置 > 系统 > 通知”中的系统通知和 " +
                "Reminder 通知均已开启；若仍失败，请重新启动或重新安装 Reminder。");
        }
    }

    public bool Show(ReminderNotificationRequest request)
    {
        if (_disposed)
        {
            return false;
        }

        try
        {
            var notifier = GetReadyNotifier();
            if (notifier is null)
            {
                return false;
            }

            if (request.RequestDisplayAttention)
            {
                _ = DisplayAttentionService.TryRequestDisplayOn();
            }

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
            notifier.Show(toast);
            RefreshAvailabilityAfterShow(notifier);
            return _isAvailable;
        }
        catch (Exception exception)
        {
            if (_notifications.TryRemove(request.NotificationId, out var tracked))
            {
                tracked.Dispose();
            }

            MarkUnavailable(
                $"发送通知时发生错误：{exception.Message}",
                "请确认 Windows“设置 > 系统 > 通知”中的系统通知和 " +
                "Reminder 通知均已开启；若仍失败，请重新启动或重新安装 Reminder。");
            return false;
        }
    }

    public bool ShowMissedEvents(IReadOnlyList<string> eventNames)
    {
        if (_disposed || eventNames.Count == 0)
        {
            return false;
        }

        try
        {
            var notifier = GetReadyNotifier();
            if (notifier is null)
            {
                return false;
            }

            var content = new ToastContentBuilder()
                .AddText("Reminder · 已跳过")
                .AddText(string.Join("、", eventNames))
                .AddText("这些事件的计划时间已经过去。")
                .SetToastDuration(ToastDuration.Long)
                .GetToastContent();

            var toast = new ToastNotification(content.GetXml())
            {
                Group = ToastGroup
            };
            notifier.Show(toast);
            RefreshAvailabilityAfterShow(notifier);
            return _isAvailable;
        }
        catch (Exception exception)
        {
            MarkUnavailable(
                $"发送“已跳过”通知时发生错误：{exception.Message}",
                "请确认 Windows“设置 > 系统 > 通知”中的系统通知和 " +
                "Reminder 通知均已开启；若仍失败，请重新启动 Reminder。");
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
            var notifier = GetReadyNotifier();
            if (notifier is not null)
            {
                var notificationCenterToast = CreateToast(
                    tracked,
                    suppressPopup: true);
                notifier.Show(notificationCenterToast);
                RefreshAvailabilityAfterShow(notifier);
            }
        }
        catch (Exception exception)
        {
            MarkUnavailable(
                $"通知中心保留失败：{exception.Message}",
                "请确认 Reminder 的 Windows 通知权限已开启；" +
                "若权限正常，请重新启动 Reminder。");
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
            var notifier = GetReadyNotifier();
            if (notifier is null)
            {
                return;
            }

            var toast = CreateToast(tracked, suppressPopup: false);
            notifier.Show(toast);
            RefreshAvailabilityAfterShow(notifier);
            if (!IsCurrent(tracked))
            {
                RemoveFromHistory(tracked);
                return;
            }

            tracked.RestartAutoDismiss(
                tracked.Request.VisibleDuration,
                () => AutoDismiss(tracked.Request.NotificationId));
        }
        catch (Exception exception)
        {
            MarkUnavailable(
                $"重新显示通知失败：{exception.Message}",
                "请确认 Reminder 的 Windows 通知权限已开启；" +
                "若权限正常，请重新启动 Reminder。");
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
        toast.Failed += (_, _) =>
        {
            MarkUnavailable(
                "Windows 报告通知投递失败。",
                "请确认 Windows“设置 > 系统 > 通知”中的系统通知和 " +
                "Reminder 通知均已开启；若仍失败，请重新启动 Reminder。");
            Complete(
                tracked.Request,
                ReminderNotificationAction.DeliveryFailed,
                DateTimeOffset.Now);
        };
        return toast;
    }

    private ToastNotifierCompat? GetReadyNotifier()
    {
        ToastNotifierCompat notifier;
        try
        {
            notifier =
                ToastNotificationManagerCompat.CreateToastNotifier();
        }
        catch (Exception exception)
        {
            MarkUnavailable(
                $"无法创建 Windows 通知发送器：{exception.Message}",
                "请重新启动 Reminder；若仍未恢复，请确认 Windows 通知已开启，" +
                "并尝试重新安装程序。");
            return null;
        }

        return IsNotifierEnabled(notifier)
            ? notifier
            : null;
    }

    private void RefreshAvailabilityAfterShow(
        ToastNotifierCompat notifier)
    {
        _ = IsNotifierEnabled(notifier);
    }

    private bool IsNotifierEnabled(ToastNotifierCompat notifier)
    {
        try
        {
            var setting = notifier.Setting;
            if (setting == NotificationSetting.Enabled)
            {
                MarkAvailable();
                return true;
            }

            MarkUnavailable(setting);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Some unpackaged desktop processes can send compatible toasts but
            // Windows does not permit them to query ToastNotifier.Setting.
            // Keep delivery enabled and rely on Show/Failed to report a real
            // delivery error instead of disabling notifications pre-emptively.
            MarkAvailable();
            return true;
        }
        catch (Exception exception)
        {
            MarkUnavailable(
                $"读取 Windows 通知设置失败：{exception.Message}",
                "请确认 Windows“设置 > 系统 > 通知”中的系统通知和 " +
                "Reminder 通知均已开启；若仍失败，请重新启动 Reminder。");
            return false;
        }
    }

    private void MarkAvailable()
    {
        _isAvailable = true;
        _statusMessage = "Windows 通知已就绪";
        _statusHelpMessage = "Windows 通知功能正常。";
    }

    private void MarkUnavailable(NotificationSetting setting)
    {
        var (reason, solution) = setting switch
        {
            NotificationSetting.DisabledForApplication => (
                "Windows 已关闭 Reminder 的通知权限。",
                "请打开“设置 > 系统 > 通知”，找到 Reminder 并开启通知。"),
            NotificationSetting.DisabledForUser => (
                "当前 Windows 用户的全部通知已被关闭。",
                "请打开“设置 > 系统 > 通知”，开启系统通知。"),
            NotificationSetting.DisabledByGroupPolicy => (
                "通知已被 Windows 组策略关闭。",
                "此设置通常由组织管理员控制，请联系系统管理员开启通知。"),
            NotificationSetting.DisabledByManifest => (
                "当前安装未正确声明 Windows 通知能力。",
                "请重新安装正式发布的 Reminder 安装包。"),
            _ => (
                $"Windows 返回了未知通知状态：{setting}。",
                "请检查 Windows 通知设置，然后重新启动 Reminder。")
        };

        MarkUnavailable(reason, solution);
    }

    private void MarkUnavailable(string reason, string solution)
    {
        _isAvailable = false;
        _statusMessage = "Windows 通知未就绪";
        _statusHelpMessage = $"{reason}\n{solution}";
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
