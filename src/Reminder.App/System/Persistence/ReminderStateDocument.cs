using Reminder.App.Logic.Models;
using Reminder.App.SystemModule.Settings;

namespace Reminder.App.SystemModule.Persistence;

internal sealed record ReminderStateDocument
{
    public const int CurrentVersion = 3;

    public required int Version { get; init; }

    public required DateTimeOffset SavedAt { get; init; }

    public required List<ReminderEventDocument> Events { get; init; }

    public required ReminderGlobalPauseDocument GlobalPause { get; init; }

    public required List<Guid> PendingMissedEventIds { get; init; }

    public ReminderRenderingMode RenderingMode { get; init; } =
        ReminderRenderingMode.HardwarePreferred;
}

internal sealed record ReminderEventDocument
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required ReminderEventType EventType { get; init; }

    public required long FixedIntervalTicks { get; init; }

    public required ReminderScheduledTimeDocument ScheduledTime { get; init; }

    public required int? RemainingOccurrences { get; init; }

    public required bool IsEnabled { get; init; }

    public required bool IsPaused { get; init; }

    public bool IsBlockedByGlobalPause { get; init; }

    public required FixedUnavailablePolicy FixedUnavailablePolicy { get; init; }

    public required UnavailableNotificationPolicy
        FixedUnavailableNotificationPolicy { get; init; }

    public required UnavailableNotificationPolicy
        ScheduledUnavailableNotificationPolicy { get; init; }

    public required long? FrozenRemainingTicks { get; init; }

    public required DateTimeOffset? NextScheduledOccurrenceAt { get; init; }

    public required DateTimeOffset? DeferredOccurrenceAt { get; init; }

    public required bool IsExpired { get; init; }
}

internal sealed record ReminderScheduledTimeDocument
{
    public required ReminderRecurrence Recurrence { get; init; }

    public required DateTimeOffset OneTimeAt { get; init; }

    public required int TimeOfDayMinutes { get; init; }

    public required DayOfWeek DayOfWeek { get; init; }

    public required int DayOfMonth { get; init; }

    public required int MonthOfYear { get; init; }
}

internal sealed record ReminderGlobalPauseDocument
{
    public required bool IsPaused { get; init; }

    public required ReminderGlobalPauseDuration Duration { get; init; }

    public required long? RemainingTicks { get; init; }
}
