using System.Drawing;
using System.Windows;
using Reminder.App.SystemModule.AppInfo;
using Forms = System.Windows.Forms;

namespace Reminder.App.Windows.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Action _showMainWindow;
    private bool _disposed;

    public TrayIconService(Action showMainWindow)
    {
        _showMainWindow = showMainWindow;

        var openItem = new Forms.ToolStripMenuItem("打开 Reminder");
        openItem.Click += (_, _) => ShowMainWindow();

        var versionItem = new Forms.ToolStripMenuItem($"版本 {AppMetadata.Version}")
        {
            Enabled = false
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(versionItem);

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
        if (_disposed)
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.BeginInvoke(_showMainWindow);
    }
}
