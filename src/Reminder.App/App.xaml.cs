using Reminder.App.Logic.Models;
using Reminder.App.Logic.Services;
using Reminder.App.SystemModule.Persistence;
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
    private ReminderPersistenceCoordinator? _persistenceCoordinator;
    private bool _finalStateSaved;
    private bool _exitInProgress;

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

        var stateStore = new ProtectedReminderStateStore();
        var loadResult = stateStore.Load();
        var stateRestored = TryRestoreState(
            stateStore,
            loadResult,
            out var recoveryFailed);
        if (!stateRestored)
        {
            _engine.InitializeDefaultEvents();
        }
        else
        {
            _engine.ActivateRecoveredState();
        }

        _mainViewModel = new MainViewModel(_engine, Dispatcher);
        _mainWindow = new MainWindow(_mainViewModel);
        MainWindow = _mainWindow;

        _trayIconService = new TrayIconService(
            _mainWindow.ShowAndActivate,
            () => _engine.PauseAll(
                ReminderGlobalPauseDuration.UntilManualResume),
            _engine.ResumeAll,
            RequestExit);
        _persistenceCoordinator =
            new ReminderPersistenceCoordinator(_engine, stateStore);
        _persistenceCoordinator.Start();
        _mainWindow.Show();

        if (recoveryFailed)
        {
            var dialog = new StateRecoveryFailedDialog
            {
                Owner = _mainWindow
            };
            _ = dialog.ShowDialog();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (!_finalStateSaved)
        {
            _finalStateSaved =
                _persistenceCoordinator?.SaveFinal().IsSuccess == true;
        }

        _persistenceCoordinator?.Dispose();
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

    protected override void OnSessionEnding(
        System.Windows.SessionEndingCancelEventArgs e)
    {
        if (!_finalStateSaved)
        {
            _finalStateSaved =
                _persistenceCoordinator?.SaveFinal().IsSuccess == true;
        }

        _mainWindow?.AllowApplicationExit();
        base.OnSessionEnding(e);
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

    private bool TryRestoreState(
        ProtectedReminderStateStore stateStore,
        ReminderStateLoadResult loadResult,
        out bool recoveryFailed)
    {
        recoveryFailed =
            loadResult.Status == ReminderStateLoadStatus.RecoveryFailed;
        if (!loadResult.HasState || loadResult.State is null)
        {
            return false;
        }

        if (_engine!.TryImportState(
                loadResult.State,
                out _))
        {
            return true;
        }

        if (loadResult.Status == ReminderStateLoadStatus.LoadedPrimary)
        {
            var backupResult = stateStore.LoadBackup();
            if (backupResult.State is not null &&
                _engine.TryImportState(
                    backupResult.State,
                    out _))
            {
                return true;
            }
        }

        recoveryFailed = true;
        return false;
    }

    private void RequestExit()
    {
        if (_exitInProgress ||
            _persistenceCoordinator is null ||
            _mainWindow is null)
        {
            return;
        }

        _exitInProgress = true;
        var saveResult = _persistenceCoordinator.SaveFinal();
        if (!saveResult.IsSuccess)
        {
            _exitInProgress = false;
            System.Windows.MessageBox.Show(
                _mainWindow,
                "当前状态保存失败，Reminder 没有退出。请检查 Data 目录是否可写后重试。",
                "无法退出 Reminder",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        _finalStateSaved = true;
        _mainWindow.AllowApplicationExit();
        Shutdown();
    }
}
