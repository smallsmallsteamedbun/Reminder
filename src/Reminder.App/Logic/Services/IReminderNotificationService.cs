namespace Reminder.App.Logic.Services;

public interface IReminderNotificationService : IDisposable
{
    event EventHandler<ReminderNotificationResponse>? ResponseReceived;

    bool IsAvailable { get; }

    string StatusMessage { get; }

    bool Show(ReminderNotificationRequest request);

    void Remove(Guid notificationId);
}
