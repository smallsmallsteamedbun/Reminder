using Reminder.App.Logic.Services;
using Reminder.App.UI.ViewModels;
using Reminder.App.UI.Views;
using Reminder.App.Windows.Notifications;
using Reminder.App.Windows.Tray;

namespace Reminder.App;

public partial class App : System.Windows.Application
{
    private ReminderEngine? _engine;
    private MainViewModel? _mainViewModel;
    private MainWindow? _mainWindow;
    private TrayIconService? _trayIconService;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("REMINDER_SOFTWARE_RENDERING"),
                "1",
                StringComparison.Ordinal))
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        base.OnStartup(e);

        IReminderNotificationService notificationService;
        try
        {
            notificationService = new WindowsToastNotificationService();
        }
        catch (Exception exception)
        {
            notificationService = new UnavailableNotificationService(exception.Message);
        }

        _engine = new ReminderEngine(notificationService);
        _engine.InitializeDefaultEvents();

        _mainViewModel = new MainViewModel(_engine, Dispatcher);
        _mainWindow = new MainWindow(_mainViewModel);
        MainWindow = _mainWindow;

        _trayIconService = new TrayIconService(_mainWindow.ShowAndActivate);
        _mainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        _mainViewModel?.Dispose();
        _engine?.Dispose();
        base.OnExit(e);
    }
}
