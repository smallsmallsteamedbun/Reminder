namespace Reminder.App.SystemModule.Settings;

public sealed class ReminderApplicationSettingsService
{
    private readonly object _gate = new();
    private ReminderRenderingMode _renderingMode;

    public ReminderApplicationSettingsService(
        ReminderApplicationSettings? initialSettings = null)
    {
        _renderingMode =
            initialSettings?.RenderingMode ??
            ReminderRenderingMode.HardwarePreferred;
    }

    public event EventHandler? SettingsChanged;

    public ReminderRenderingMode RenderingMode
    {
        get
        {
            lock (_gate)
            {
                return _renderingMode;
            }
        }
    }

    public bool SetRenderingMode(ReminderRenderingMode renderingMode)
    {
        if (!Enum.IsDefined(renderingMode))
        {
            return false;
        }

        lock (_gate)
        {
            if (_renderingMode == renderingMode)
            {
                return false;
            }

            _renderingMode = renderingMode;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public ReminderApplicationSettings Export()
    {
        lock (_gate)
        {
            return new ReminderApplicationSettings
            {
                RenderingMode = _renderingMode
            };
        }
    }
}
