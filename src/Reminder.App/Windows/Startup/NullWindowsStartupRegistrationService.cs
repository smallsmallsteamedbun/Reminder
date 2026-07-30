namespace Reminder.App.Windows.Startup;

public sealed class NullWindowsStartupRegistrationService :
    IWindowsStartupRegistrationService
{
    private bool _isRegistered;

    public bool IsRegistered => _isRegistered;

    public bool TrySetEnabled(bool enabled, out string errorMessage)
    {
        _isRegistered = enabled;
        errorMessage = string.Empty;
        return true;
    }
}
