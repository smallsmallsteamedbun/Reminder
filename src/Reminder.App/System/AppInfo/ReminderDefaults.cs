namespace Reminder.App.SystemModule.AppInfo;

public static class ReminderDefaults
{
    public const int NewEventIntervalMinutes = 45;
    public const int MinimumIntervalMinutes = 1;
    public const int MaximumIntervalMinutes = 525_600;
    public const int MaximumEventNameLength = 50;
    public const int MaximumTerminationOccurrences = 100;

    public static readonly TimeSpan SnoozeDuration = TimeSpan.FromMinutes(5);
}
