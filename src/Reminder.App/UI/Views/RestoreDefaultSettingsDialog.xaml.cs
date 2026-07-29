using System.Windows;
using System.Windows.Input;

namespace Reminder.App.UI.Views;

public partial class RestoreDefaultSettingsDialog : Window
{
    public RestoreDefaultSettingsDialog()
    {
        InitializeComponent();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Window_OnKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        DialogResult = false;
        e.Handled = true;
    }
}
