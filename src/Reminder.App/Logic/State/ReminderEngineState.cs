using Reminder.App.Logic.Models;

namespace Reminder.App.Logic.State;

public sealed record ReminderEngineState
{
    public required DateTimeOffset SavedAt { get; init; }

    public required IReadOnlyList<ReminderEngineEventState> Events { get; init; }

    public required ReminderEngineGlobalPauseState GlobalPause { get; init; }

    public required IReadOnlyList<Guid> PendingMissedEventIds { get; init; }
}

public sealed record ReminderEngineEventState
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required ReminderEventType EventType { get; init; }

    public required TimeSpan FixedInterval { get; init; }

    public required ScheduledTimeSettings ScheduledTime { get; init; }

    public required int? RemainingOccurrences { get; init; }

    public required bool IsEnabled { get; init; }

    public required bool IsPaused { get; init; }

    public required bool IsBlockedByGlobalPause { get; init; }

    public required FixedUnavailablePolicy FixedUnavailablePolicy { get; init; }

    public required UnavailableNotificationPolicy
        FixedUnavailableNotificationPolicy { get; init; }

    public required UnavailableNotificationPolicy
        ScheduledUnavailableNotificationPolicy { get; init; }

    public required TimeSpan? FrozenRemaining { get; init; }

    public required DateTimeOffset? NextScheduledOccurrenceAt { get; init; }

    public required DateTimeOffset? DeferredOccurrenceAt { get; init; }

    public required bool IsExpired { get; init; }
}

public sealed record ReminderEngineGlobalPauseState
{
    public required bool IsPaused { get; init; }

    public required ReminderGlobalPauseDuration Duration { get; init; }

    public required TimeSpan? Remaining { get; init; }
}
