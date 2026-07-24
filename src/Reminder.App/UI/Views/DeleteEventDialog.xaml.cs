using System.Windows;
using System.Windows.Input;

namespace Reminder.App.UI.Views;

public partial class DeleteEventDialog : Window
{
    public DeleteEventDialog(string eventName)
    {
        InitializeComponent();
        EventNameText.Text = $"将从事件列表中删除“{eventName}”。";
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
