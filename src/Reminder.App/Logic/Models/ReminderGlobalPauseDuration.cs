namespace Reminder.App.Logic.Models;

public enum ReminderGlobalPauseDuration
{
    UntilManualResume,
    OneMinute,
    FiveMinutes,
    TenMinutes,
    FifteenMinutes,
    ThirtyMinutes,
    OneHour,
    TwoHours,
    FiveHours
}

public static class ReminderGlobalPauseDurationExtensions
{
    public static TimeSpan? ToTimeSpan(
        this ReminderGlobalPauseDuration duration)
    {
        return duration switch
        {
            ReminderGlobalPauseDuration.UntilManualResume => null,
            ReminderGlobalPauseDuration.OneMinute =>
                TimeSpan.FromMinutes(1),
            ReminderGlobalPauseDuration.FiveMinutes =>
                TimeSpan.FromMinutes(5),
            ReminderGlobalPauseDuration.TenMinutes =>
                TimeSpan.FromMinutes(10),
            ReminderGlobalPauseDuration.FifteenMinutes =>
                TimeSpan.FromMinutes(15),
            ReminderGlobalPauseDuration.ThirtyMinutes =>
                TimeSpan.FromMinutes(30),
            ReminderGlobalPauseDuration.OneHour =>
                TimeSpan.FromHours(1),
            ReminderGlobalPauseDuration.TwoHours =>
                TimeSpan.FromHours(2),
            ReminderGlobalPauseDuration.FiveHours =>
                TimeSpan.FromHours(5),
            _ => throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "不支持的全部暂停时长")
        };
    }
}
