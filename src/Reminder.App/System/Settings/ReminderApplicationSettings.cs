namespace Reminder.App.SystemModule.Settings;

public sealed record ReminderApplicationSettings
{
    public ReminderRenderingMode RenderingMode { get; init; } =
        ReminderRenderingMode.HardwarePreferred;
}
