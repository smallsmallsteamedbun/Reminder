namespace Reminder.App.Logic.Models;

public sealed record ScheduledTimeSettings(
    ReminderRecurrence Recurrence,
    DateTimeOffset OneTimeAt,
    TimeOnly TimeOfDay,
    DayOfWeek DayOfWeek,
    int DayOfMonth,
    int MonthOfYear)
{
    public static ScheduledTimeSettings CreateDefault(
        ReminderRecurrence recurrence,
        DateTimeOffset now)
    {
        var defaultTime = now.AddHours(1);
        defaultTime = new DateTimeOffset(
            defaultTime.Year,
            defaultTime.Month,
            defaultTime.Day,
            defaultTime.Hour,
            defaultTime.Minute,
            0,
            defaultTime.Offset);

        return new ScheduledTimeSettings(
            recurrence,
            defaultTime,
            TimeOnly.FromDateTime(defaultTime.DateTime),
            defaultTime.DayOfWeek,
            defaultTime.Day,
            defaultTime.Month);
    }
}
