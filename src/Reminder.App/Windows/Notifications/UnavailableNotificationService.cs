using Reminder.App.Logic.Services;

namespace Reminder.App.Windows.Notifications;

public sealed class UnavailableNotificationService(string reason) : IReminderNotificationService
{
    public event EventHandler<ReminderNotificationResponse>? ResponseReceived
    {
        add { }
        remove { }
    }

    public bool IsAvailable => false;

    public string StatusMessage => "Windows 通知未就绪";

    public string StatusHelpMessage =>
        $"通知组件初始化失败：{reason}\n" +
        "请先重新启动 Reminder；若仍未恢复，请确认 Windows 通知已开启，" +
        "并尝试重新安装程序。";

    public bool Show(ReminderNotificationRequest request)
    {
        return false;
    }

    public bool ShowMissedEvents(IReadOnlyList<string> eventNames)
    {
        return false;
    }

    public void Remove(Guid notificationId)
    {
    }

    public void Dispose()
    {
    }
}
