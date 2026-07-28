namespace Reminder.App.Logic.Models;

public sealed record ReminderEventSnapshot
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required ReminderEventType EventType { get; init; }

    public required int IntervalMinutes { get; init; }

    public required ScheduledTimeSettings ScheduledTime { get; init; }

    public required int? RemainingOccurrences { get; init; }

    public required bool IsEnabled { get; init; }

    public required bool IsPaused { get; init; }

    public required FixedUnavailablePolicy FixedUnavailablePolicy { get; init; }

    public required UnavailableNotificationPolicy
        FixedUnavailableNotificationPolicy { get; init; }

    public required UnavailableNotificationPolicy
        ScheduledUnavailableNotificationPolicy { get; init; }

    public required bool IsBlockedByGlobalPause { get; init; }

    public required bool IsBlockedBySystemState { get; init; }

    public required bool IsEffectivelyRunning { get; init; }

    public required bool IsAwaitingAction { get; init; }

    public required bool HasPendingMissedOccurrence { get; init; }

    public required bool CanRestart { get; init; }

    public required bool IsExpired { get; init; }

    public required bool ShowExpiredEasterEgg { get; init; }

    public required TimeSpan? Remaining { get; init; }
}
