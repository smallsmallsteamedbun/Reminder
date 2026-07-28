namespace Reminder.App.Logic.Models;

internal sealed class ReminderEvent
{
    public required Guid Id { get; init; }

    public required string Name { get; set; }

    public required ReminderSchedule Schedule { get; set; }

    public required ReminderTermination Termination { get; init; }

    public bool IsEnabled { get; set; }

    public bool IsPaused { get; set; }

    public bool IsBlockedByGlobalPause { get; set; }

    public FixedUnavailablePolicy FixedUnavailablePolicy { get; set; } =
        FixedUnavailablePolicy.PauseTiming;

    public UnavailableNotificationPolicy FixedUnavailableNotificationPolicy
    {
        get;
        set;
    } = UnavailableNotificationPolicy.Suppress;

    public UnavailableNotificationPolicy ScheduledUnavailableNotificationPolicy
    {
        get;
        set;
    } = UnavailableNotificationPolicy.Suppress;

    public FixedClockBlockReason FixedClockBlockReasons { get; set; }

    public bool SystemBlockInterruptedActiveNotification { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    public TimeSpan FrozenRemaining { get; set; }

    public Guid? ActiveNotificationId { get; set; }

    public bool IsNotificationDeferred { get; set; }

    public DateTimeOffset? NotificationShownAt { get; set; }

    public DateTimeOffset? ActiveOccurrenceAt { get; set; }

    public bool IsExpired { get; set; }

    public bool ShowExpiredEasterEgg { get; set; }
}
