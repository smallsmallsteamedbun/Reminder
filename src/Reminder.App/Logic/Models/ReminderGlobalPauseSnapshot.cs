namespace Reminder.App.Logic.Models;

public sealed record ReminderGlobalPauseSnapshot
{
    public required bool IsPaused { get; init; }

    public required ReminderGlobalPauseDuration Duration { get; init; }

    public required DateTimeOffset? EndsAt { get; init; }

    public required TimeSpan? Remaining { get; init; }
}
