namespace Reminder.App.Windows.Startup;

public sealed class UnavailableWindowsStartupRegistrationService :
    IWindowsStartupRegistrationService
{
    private readonly string _errorMessage;

    public UnavailableWindowsStartupRegistrationService(
        string errorMessage)
    {
        _errorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? "Windows 启动项服务不可用。"
            : errorMessage;
    }

    public bool IsRegistered => false;

    public bool TrySetEnabled(bool enabled, out string errorMessage)
    {
        errorMessage = _errorMessage;
        return false;
    }
}
