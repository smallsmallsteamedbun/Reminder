using System.Windows;
using System.Windows.Input;

namespace Reminder.App.UI.Views;

public partial class RenderingModeRestartDialog : Window
{
    public RenderingModeRestartDialog()
    {
        InitializeComponent();
    }

    public bool RestartNow { get; private set; }

    private void RestartNowButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        RestartNow = true;
        DialogResult = true;
    }

    private void RestartLaterButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        RestartNow = false;
        DialogResult = false;
    }

    private void Window_OnKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        RestartNow = false;
        DialogResult = false;
        e.Handled = true;
    }
}
