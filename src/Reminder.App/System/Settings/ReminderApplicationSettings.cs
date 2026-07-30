using Reminder.App.Logic.Services;
using Reminder.App.SystemModule.AppInfo;

namespace Reminder.App.SystemModule.Settings;

public sealed record ReminderApplicationSettings
{
    public ReminderThemeMode ThemeMode { get; init; } =
        ReminderThemeMode.FollowSystem;

    public ReminderRenderingMode RenderingMode { get; init; } =
        ReminderRenderingMode.HardwarePreferred;

    public bool StartWithWindows { get; init; }

    public bool SilentStart { get; init; }

    public int SnoozeDurationMinutes { get; init; } =
        (int)ReminderDefaults.SnoozeDuration.TotalMinutes;

    public ReminderSnoozeOverflowPolicy SnoozeOverflowPolicy { get; init; } =
        ReminderSnoozeOverflowPolicy.ShortenToFixedInterval;

    public ReminderNotificationDisplayDuration NotificationDisplayDuration
        { get; init; } = ReminderNotificationDisplayDuration.Short;

    public IReadOnlyList<string> SearchHistory { get; init; } = [];
}
