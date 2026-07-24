namespace Reminder.App.Logic.Models;

internal abstract record ReminderSchedule(ReminderEventType Type);

internal sealed record FixedIntervalSchedule(TimeSpan Interval)
    : ReminderSchedule(ReminderEventType.FixedInterval);

internal sealed record ScheduledTimeSchedule(ScheduledTimeSettings Settings)
    : ReminderSchedule(ReminderEventType.ScheduledTime);
