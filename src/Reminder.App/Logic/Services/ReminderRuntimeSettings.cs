using Reminder.App.SystemModule.AppInfo;

namespace Reminder.App.Logic.Services;

public enum ReminderSnoozeOverflowPolicy
{
    ShortenToFixedInterval,
    UseUnifiedDuration
}

public enum ReminderNotificationDisplayDuration
{
    Short,
    Long
}

public interface IReminderRuntimeSettings
{
    TimeSpan SnoozeDuration { get; }

    ReminderSnoozeOverflowPolicy SnoozeOverflowPolicy { get; }

    ReminderNotificationDisplayDuration NotificationDisplayDuration { get; }
}

internal sealed class DefaultReminderRuntimeSettings :
    IReminderRuntimeSettings
{
    public static DefaultReminderRuntimeSettings Instance { get; } = new();

    public TimeSpan SnoozeDuration => ReminderDefaults.SnoozeDuration;

    public ReminderSnoozeOverflowPolicy SnoozeOverflowPolicy =>
        ReminderSnoozeOverflowPolicy.ShortenToFixedInterval;

    public ReminderNotificationDisplayDuration NotificationDisplayDuration =>
        ReminderNotificationDisplayDuration.Short;
}
