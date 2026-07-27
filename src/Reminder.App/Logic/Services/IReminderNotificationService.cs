namespace Reminder.App.Logic.Services;

public interface IReminderNotificationService : IDisposable
{
    event EventHandler<ReminderNotificationResponse>? ResponseReceived;

    bool IsAvailable { get; }

    string StatusMessage { get; }

    string StatusHelpMessage { get; }

    bool Show(ReminderNotificationRequest request);

    bool ShowMissedEvents(IReadOnlyList<string> eventNames);

    void Remove(Guid notificationId);
}
