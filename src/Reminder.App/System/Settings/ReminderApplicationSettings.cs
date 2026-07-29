using Reminder.App.Logic.Services;
using Reminder.App.SystemModule.AppInfo;

namespace Reminder.App.SystemModule.Settings;

public sealed record ReminderApplicationSettings
{
    public ReminderRenderingMode RenderingMode { get; init; } =
        ReminderRenderingMode.HardwarePreferred;

    public int SnoozeDurationMinutes { get; init; } =
        (int)ReminderDefaults.SnoozeDuration.TotalMinutes;

    public ReminderSnoozeOverflowPolicy SnoozeOverflowPolicy { get; init; } =
        ReminderSnoozeOverflowPolicy.ShortenToFixedInterval;

    public ReminderNotificationDisplayDuration NotificationDisplayDuration
        { get; init; } = ReminderNotificationDisplayDuration.Short;

    public IReadOnlyList<string> SearchHistory { get; init; } = [];
}
