using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Reminder.App.SystemModule.Runtime;
using Reminder.App.UI.ViewModels;

namespace Reminder.App.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _countdownRefreshTimer;
    private readonly DispatcherTimer _newEventHighlightTimer;
    private EventViewModel? _highlightedEvent;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _countdownRefreshTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => _viewModel.Refresh(),
            Dispatcher);

        _newEventHighlightTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1.8),
            DispatcherPriority.Background,
            (_, _) => ClearNewEventHighlight(),
            Dispatcher);

        _viewModel.EventAdded += OnEventAdded;

        Loaded += (_, _) => StartUiRefresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                StartUiRefresh();
            }
            else
            {
                _countdownRefreshTimer.Stop();
                Dispatcher.BeginInvoke(
                    ProcessMemoryTrimmer.TrimAfterWindowHidden,
                    DispatcherPriority.ApplicationIdle);
            }
        };
    }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        StartUiRefresh();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private void StartUiRefresh()
    {
        _viewModel.Refresh();
        if (!_countdownRefreshTimer.IsEnabled)
        {
            _countdownRefreshTimer.Start();
        }
    }

    private void EditableTextBox_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        CommitTextBox(sender);
    }

    private void EditableTextBox_OnKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        CommitTextBox(sender);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void EventListScrollViewer_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer ||
            scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var targetOffset = Math.Clamp(
            scrollViewer.VerticalOffset - e.Delta,
            0,
            scrollViewer.ScrollableHeight);
        scrollViewer.ScrollToVerticalOffset(targetOffset);
        e.Handled = true;
    }

    private void OnEventAdded(Guid eventId)
    {
        Dispatcher.BeginInvoke(
            () => RevealAndHighlightEvent(eventId),
            DispatcherPriority.Loaded);
    }

    private void RevealAndHighlightEvent(Guid eventId)
    {
        var eventViewModel = _viewModel.Events.FirstOrDefault(item => item.Id == eventId);
        if (eventViewModel is null)
        {
            return;
        }

        ClearNewEventHighlight();
        _highlightedEvent = eventViewModel;
        eventViewModel.SetHighlighted(true);

        UpdateLayout();
        if (EventItemsControl.ItemContainerGenerator.ContainerFromItem(eventViewModel)
            is FrameworkElement container)
        {
            var top = container.TranslatePoint(
                new System.Windows.Point(0, 0),
                EventListScrollViewer).Y;
            var bottom = top + container.ActualHeight;
            if (top < 0 || bottom > EventListScrollViewer.ViewportHeight)
            {
                container.BringIntoView();
            }
        }

        _newEventHighlightTimer.Start();
    }

    private void ClearNewEventHighlight()
    {
        _newEventHighlightTimer.Stop();
        _highlightedEvent?.SetHighlighted(false);
        _highlightedEvent = null;
    }

    private static void CommitTextBox(object sender)
    {
        if (sender is not System.Windows.Controls.TextBox
            {
                DataContext: EventViewModel eventViewModel
            } textBox)
        {
            return;
        }

        if (Equals(textBox.Tag, "Name"))
        {
            eventViewModel.CommitName();
        }
        else if (Equals(textBox.Tag, "Interval"))
        {
            eventViewModel.CommitInterval();
        }
    }
}
