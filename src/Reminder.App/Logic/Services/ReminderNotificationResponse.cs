namespace Reminder.App.Logic.Services;

public sealed record ReminderNotificationResponse(
    Guid EventId,
    Guid NotificationId,
    ReminderNotificationAction Action,
    DateTimeOffset OccurredAt);
