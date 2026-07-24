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

    public string StatusMessage => $"Windows 通知暂不可用：{reason}";

    public bool Show(ReminderNotificationRequest request)
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
