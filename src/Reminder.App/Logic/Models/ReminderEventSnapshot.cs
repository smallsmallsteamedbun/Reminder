namespace Reminder.App.Logic.Models;

public sealed record ReminderEventSnapshot(
    Guid Id,
    string Name,
    int IntervalMinutes,
    bool IsEnabled,
    bool IsPaused,
    bool IsAwaitingAction,
    bool CanRestart,
    TimeSpan Remaining);
