namespace Reminder.App.Logic.Services;

public sealed record ReminderNotificationRequest(
    Guid EventId,
    Guid NotificationId,
    string EventName,
    DateTimeOffset ShownAt,
    TimeSpan VisibleDuration,
    bool RequestDisplayAttention = false);
