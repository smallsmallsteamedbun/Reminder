namespace Reminder.App.Logic.Models;

internal sealed class ReminderEvent
{
    public required Guid Id { get; init; }

    public required string Name { get; set; }

    public required TimeSpan Interval { get; set; }

    public bool IsEnabled { get; set; }

    public bool IsPaused { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    public TimeSpan FrozenRemaining { get; set; }

    public Guid? ActiveNotificationId { get; set; }

    public bool IsNotificationDeferred { get; set; }

    public DateTimeOffset? NotificationShownAt { get; set; }
}
