namespace Reminder.App.Logic.Services;

public sealed record ReminderNotificationRequest(
    Guid EventId,
    Guid NotificationId,
    string EventName,
    DateTimeOffset ShownAt,
    ReminderNotificationDisplayDuration DisplayDuration,
    bool RequestDisplayAttention = false);
