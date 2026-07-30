namespace Reminder.App.Windows.Startup;

public interface IWindowsStartupRegistrationService
{
    bool IsRegistered { get; }

    bool TrySetEnabled(bool enabled, out string errorMessage);
}
