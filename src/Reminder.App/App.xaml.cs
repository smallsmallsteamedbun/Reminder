using Reminder.App.Logic.Models;
using Reminder.App.Logic.Services;
using Reminder.App.UI.ViewModels;
using Reminder.App.UI.Views;
using Reminder.App.Windows.Activity;
using Reminder.App.Windows.Notifications;
using Reminder.App.Windows.Tray;

namespace Reminder.App;

public partial class App : System.Windows.Application
{
    private ReminderEngine? _engine;
    private MainViewModel? _mainViewModel;
    private MainWindow? _mainWindow;
    private TrayIconService? _trayIconService;
    private IWindowsActivityMonitor? _windowsActivityMonitor;

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

        try
        {
            _windowsActivityMonitor = new WindowsActivityMonitor();
            _windowsActivityMonitor.StateChanged +=
                OnWindowsActivityStateChanged;
            var initialActivity = _windowsActivityMonitor.Current;
            _engine.UpdateSystemState(
                new ReminderSystemState(
                    initialActivity.IsSessionLocked,
                    initialActivity.IsDisplayOff,
                    initialActivity.IsSleeping));
        }
        catch
        {
            _windowsActivityMonitor?.Dispose();
            _windowsActivityMonitor = null;
        }

        _mainViewModel = new MainViewModel(_engine, Dispatcher);
        _mainWindow = new MainWindow(_mainViewModel);
        MainWindow = _mainWindow;

        _trayIconService = new TrayIconService(
            _mainWindow.ShowAndActivate,
            () => _engine.PauseAll(
                ReminderGlobalPauseDuration.UntilManualResume),
            _engine.ResumeAll);
        _mainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        if (_windowsActivityMonitor is not null)
        {
            _windowsActivityMonitor.StateChanged -=
                OnWindowsActivityStateChanged;
            _windowsActivityMonitor.Dispose();
        }

        _mainViewModel?.Dispose();
        _engine?.Dispose();
        base.OnExit(e);
    }

    private void OnWindowsActivityStateChanged(
        object? sender,
        WindowsActivityChangedEventArgs e)
    {
        _engine?.UpdateSystemState(
            new ReminderSystemState(
                e.Snapshot.IsSessionLocked,
                e.Snapshot.IsDisplayOff,
                e.Snapshot.IsSleeping),
            e.OccurredAt);
    }
}
