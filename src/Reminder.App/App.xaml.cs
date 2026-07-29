using Reminder.App.Logic.Models;
using Reminder.App.Logic.Services;
using Reminder.App.SystemModule.Persistence;
using Reminder.App.SystemModule.Runtime;
using Reminder.App.SystemModule.Settings;
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
    private ReminderApplicationSettingsService? _settingsService;
    private bool _finalStateSaved;
    private bool _exitInProgress;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (!ReminderProcessRestarter.WaitForPreviousProcessIfRequested(
                e.Args))
        {
            Shutdown(-1);
            return;
        }

        var stateStore = new ProtectedReminderStateStore();
        var loadResult = stateStore.Load();
        _settingsService = new ReminderApplicationSettingsService(
            loadResult.State?.Settings);
        ApplyRenderingMode(_settingsService.RenderingMode);

        base.OnStartup(e);

        IReminderNotificationService notificationService;
        try
        {
            notificationService = new WindowsToastNotificationService(
                _settingsService);
        }
        catch (Exception exception)
        {
            notificationService = new UnavailableNotificationService(exception.Message);
        }

        _engine = new ReminderEngine(
            notificationService,
            runtimeSettings: _settingsService);

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

        var stateRestored = TryRestoreState(
            stateStore,
            loadResult,
            out var recoveryFailed,
            out var restoredState);
        if (restoredState is not null)
        {
            var previousRenderingMode =
                _settingsService.RenderingMode;
            _settingsService.Apply(restoredState.Settings);
            if (previousRenderingMode !=
                _settingsService.RenderingMode)
            {
                ApplyRenderingMode(_settingsService.RenderingMode);
            }
        }

        if (!stateRestored)
        {
            _engine.InitializeDefaultEvents();
        }
        else
        {
            _engine.ActivateRecoveredState();
        }

        _mainViewModel = new MainViewModel(
            _engine,
            Dispatcher,
            _settingsService);
        _mainWindow = new MainWindow(_mainViewModel);
        _mainWindow.RestartRequested += RequestRestart;
        MainWindow = _mainWindow;

        _trayIconService = new TrayIconService(
            _mainWindow.ShowAndActivate,
            () => _engine.PauseAll(
                ReminderGlobalPauseDuration.UntilManualResume),
            _engine.ResumeAll,
            RequestExit);
        _persistenceCoordinator =
            new ReminderPersistenceCoordinator(
                _engine,
                stateStore,
                _settingsService);
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
        out bool recoveryFailed,
        out ReminderPersistedState? restoredState)
    {
        restoredState = null;
        recoveryFailed =
            loadResult.Status == ReminderStateLoadStatus.RecoveryFailed;
        if (!loadResult.HasState || loadResult.State is null)
        {
            return false;
        }

        if (_engine!.TryImportState(
                loadResult.State.EngineState,
                out _))
        {
            restoredState = loadResult.State;
            return true;
        }

        if (loadResult.Status == ReminderStateLoadStatus.LoadedPrimary)
        {
            var backupResult = stateStore.LoadBackup();
            if (backupResult.State is not null &&
                _engine.TryImportState(
                    backupResult.State.EngineState,
                    out _))
            {
                restoredState = backupResult.State;
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

    private void RequestRestart()
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
                "渲染设置已经更改，但当前状态保存失败，Reminder 没有重启。请检查 Data 目录是否可写后重试。",
                "无法重启 Reminder",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (!ReminderProcessRestarter.TryStartReplacementProcess())
        {
            _exitInProgress = false;
            System.Windows.MessageBox.Show(
                _mainWindow,
                "无法启动新的 Reminder 进程。渲染设置已经保存，将在下次自行启动软件时生效。",
                "无法立即重启",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        _finalStateSaved = true;
        _mainWindow.AllowApplicationExit();
        Shutdown();
    }

    private static void ApplyRenderingMode(ReminderRenderingMode mode)
    {
        var forceSoftware = string.Equals(
            Environment.GetEnvironmentVariable("REMINDER_SOFTWARE_RENDERING"),
            "1",
            StringComparison.Ordinal);
        System.Windows.Media.RenderOptions.ProcessRenderMode =
            forceSoftware ||
            mode == ReminderRenderingMode.SoftwareCompatibility
                ? System.Windows.Interop.RenderMode.SoftwareOnly
                : System.Windows.Interop.RenderMode.Default;
    }

}
