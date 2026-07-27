using System.Drawing;
using System.Windows;
using Reminder.App.SystemModule.AppInfo;
using Forms = System.Windows.Forms;

namespace Reminder.App.Windows.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Action _showMainWindow;
    private readonly Action _pauseAll;
    private readonly Action _resumeAll;
    private readonly Action _exit;
    private bool _disposed;

    public TrayIconService(
        Action showMainWindow,
        Action pauseAll,
        Action resumeAll,
        Action exit)
    {
        _showMainWindow = showMainWindow;
        _pauseAll = pauseAll;
        _resumeAll = resumeAll;
        _exit = exit;

        var openItem = new Forms.ToolStripMenuItem("打开 Reminder");
        openItem.Click += (_, _) => ShowMainWindow();

        var versionItem = new Forms.ToolStripMenuItem($"版本 {AppMetadata.Version}")
        {
            Enabled = false
        };

        var pauseAllItem = new Forms.ToolStripMenuItem("快捷全部暂停");
        pauseAllItem.Click += (_, _) => Dispatch(_pauseAll);

        var resumeAllItem = new Forms.ToolStripMenuItem("全部恢复");
        resumeAllItem.Click += (_, _) => Dispatch(_resumeAll);

        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Dispatch(_exit);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(pauseAllItem);
        menu.Items.Add(resumeAllItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(versionItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = $"{AppMetadata.Name} {AppMetadata.Version}",
            Icon = SystemIcons.Information,
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }

    private void ShowMainWindow()
    {
        Dispatch(_showMainWindow);
    }

    private void Dispatch(Action action)
    {
        if (!_disposed)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(action);
        }
    }
}
