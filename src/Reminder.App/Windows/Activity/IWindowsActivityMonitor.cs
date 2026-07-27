namespace Reminder.App.Windows.Activity;

public interface IWindowsActivityMonitor : IDisposable
{
    event EventHandler<WindowsActivityChangedEventArgs>? StateChanged;

    WindowsActivitySnapshot Current { get; }
}
