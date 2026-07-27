using System.Windows;

namespace Reminder.App.UI.Views;

public partial class StateRecoveryFailedDialog : Window
{
    public StateRecoveryFailedDialog()
    {
        InitializeComponent();
    }

    private void ConfirmButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
