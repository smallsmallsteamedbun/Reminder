using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Reminder.App.Logic.Services;
using Reminder.App.SystemModule.Runtime;
using Reminder.App.UI.Interactions;
using Reminder.App.UI.ViewModels;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using DataFormats = System.Windows.DataFormats;
using TextBox = System.Windows.Controls.TextBox;
using ToolTip = System.Windows.Controls.ToolTip;

namespace Reminder.App.UI.Views;

public partial class MainWindow : Window
{
    private const int WmMouseHorizontalWheel = 0x020E;
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _countdownRefreshTimer;
    private readonly DispatcherTimer _newEventHighlightTimer;
    private readonly SmoothScrollController _eventListScroller;
    private readonly ListReflowAnimator _eventListReflowAnimator;
    private HwndSource? _windowSource;
    private EventViewModel? _highlightedEvent;
    private bool _allowApplicationExit;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        AddHandler(
            Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(
                EditableTextBox_OnGotKeyboardFocus),
            handledEventsToo: true);
        AddHandler(
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(
                EditableTextBox_OnPreviewMouseDown),
            handledEventsToo: true);
        AddHandler(
            Mouse.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(
                ComboBox_OnPreviewMouseWheel),
            handledEventsToo: true);

        _eventListScroller = new SmoothScrollController(EventListScrollViewer);
        _eventListReflowAnimator = new ListReflowAnimator(
            EventItemsControl,
            EventListScrollViewer);

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
        _viewModel.DeleteRequested += OnDeleteRequested;

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
                _eventListScroller.CompleteImmediately();
                _eventListReflowAnimator.CompleteImmediately();
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

    public void AllowApplicationExit()
    {
        _allowApplicationExit = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowApplicationExit)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(WindowMessageHook);
    }

    protected override void OnClosed(EventArgs e)
    {
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
        base.OnClosed(e);
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

    private void EditableTextBox_OnGotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is TextBox textBox)
        {
            MoveCaretToEnd(textBox);
        }
    }

    private void EditableTextBox_OnPreviewMouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            FindAncestor<TextBox>(
                e.OriginalSource as DependencyObject) is not { } textBox ||
            textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        textBox.Focus();
        MoveCaretToEnd(textBox);
        e.Handled = true;
    }

    private void EventNameTextBox_OnPreviewTextInput(
        object sender,
        TextCompositionEventArgs e)
    {
        if (sender is TextBox textBox &&
            !ReminderInputValidator.IsNameWithinMaximumLength(
                CreateTextAfterInput(textBox, e.Text)))
        {
            e.Handled = true;
        }
    }

    private void EventNameTextBox_OnPasting(
        object sender,
        DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox ||
            !e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            return;
        }

        var pastedText =
            e.SourceDataObject.GetData(DataFormats.UnicodeText) as string
            ?? string.Empty;
        if (!ReminderInputValidator.IsNameWithinMaximumLength(
                CreateTextAfterInput(textBox, pastedText)))
        {
            e.CancelCommand();
        }
    }

    private void ComboBox_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var sourceComboBox = FindAncestor<ComboBox>(source);
        var openComboBox = FindOpenComboBox();
        if (openComboBox is not null)
        {
            if (IsSourceInsidePopup(source, openComboBox) ||
                IsPointerOverPopup(openComboBox))
            {
                ScrollOpenComboBoxPopup(openComboBox, e.Delta);
                e.Handled = true;
                return;
            }

            openComboBox.IsDropDownOpen = false;
        }

        if (sourceComboBox is null)
        {
            return;
        }

        if (IsDescendantOf(sourceComboBox, EventListScrollViewer) &&
            EventListScrollViewer.ScrollableHeight > 0)
        {
            _eventListScroller.ScrollBy(-e.Delta);
        }

        e.Handled = true;
    }

    private static bool IsDescendantOf(
        DependencyObject child,
        DependencyObject ancestor)
    {
        for (var current = child;
             current is not null;
             current = GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void GlobalActionButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button { ToolTip: ToolTip toolTip })
        {
            toolTip.IsOpen = false;
        }
    }

    private ComboBox? FindOpenComboBox()
    {
        return FindVisualDescendants<ComboBox>(this)
            .FirstOrDefault(comboBox => comboBox.IsDropDownOpen);
    }

    private static bool IsPointerOverPopup(ComboBox comboBox)
    {
        return GetComboBoxPopup(comboBox)?.Child is UIElement
        {
            IsMouseOver: true
        };
    }

    private static bool IsSourceInsidePopup(
        DependencyObject? source,
        ComboBox comboBox)
    {
        if (GetComboBoxPopup(comboBox)?.Child is not DependencyObject
            popupRoot)
        {
            return false;
        }

        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, popupRoot))
            {
                return true;
            }

            current = GetParent(current);
        }

        return false;
    }

    private static void ScrollOpenComboBoxPopup(
        ComboBox comboBox,
        int wheelDelta)
    {
        var popup = GetComboBoxPopup(comboBox);
        if (popup?.Child is not DependencyObject popupRoot)
        {
            return;
        }

        var scrollViewer = FindVisualDescendants<ScrollViewer>(popupRoot)
            .FirstOrDefault(item => item.ScrollableHeight > 0);
        if (scrollViewer is null)
        {
            return;
        }

        var notchCount = Math.Max(1, Math.Abs(wheelDelta) / 120);
        var wheelLines = SystemParameters.WheelScrollLines;
        for (var notch = 0; notch < notchCount; notch++)
        {
            if (wheelLines < 0)
            {
                if (wheelDelta > 0)
                {
                    scrollViewer.PageUp();
                }
                else
                {
                    scrollViewer.PageDown();
                }

                continue;
            }

            for (var line = 0; line < Math.Max(1, wheelLines); line++)
            {
                if (wheelDelta > 0)
                {
                    scrollViewer.LineUp();
                }
                else
                {
                    scrollViewer.LineDown();
                }
            }
        }
    }

    private static Popup? GetComboBoxPopup(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        return comboBox.Template.FindName(
            "PART_Popup",
            comboBox) as Popup;
    }

    private void EventListScrollViewer_OnPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        var originalSource = e.OriginalSource as DependencyObject;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 &&
            TryScrollHorizontal(originalSource, -e.Delta))
        {
            e.Handled = true;
            return;
        }

        if (sender is not ScrollViewer scrollViewer ||
            scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        _eventListScroller.ScrollBy(-e.Delta);
        e.Handled = true;
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmMouseHorizontalWheel || !IsVisible)
        {
            return IntPtr.Zero;
        }

        var packedPoint = lParam.ToInt64();
        var screenPoint = new System.Windows.Point(
            unchecked((short)(packedPoint & 0xFFFF)),
            unchecked((short)((packedPoint >> 16) & 0xFFFF)));
        var hit = InputHitTest(PointFromScreen(screenPoint)) as DependencyObject;
        var packedWheel = wParam.ToInt64();
        var delta = unchecked((short)((packedWheel >> 16) & 0xFFFF));
        if (!TryScrollHorizontal(hit, delta))
        {
            return IntPtr.Zero;
        }

        handled = true;
        return IntPtr.Zero;
    }

    private static bool TryScrollHorizontal(
        DependencyObject? source,
        double delta)
    {
        var scroller = FindScrollableAncestor(source, horizontal: true);
        if (scroller is null)
        {
            return false;
        }

        var targetOffset = Math.Clamp(
            scroller.HorizontalOffset + delta,
            0,
            scroller.ScrollableWidth);
        if (Math.Abs(targetOffset - scroller.HorizontalOffset) < 0.01)
        {
            return false;
        }

        scroller.ScrollToHorizontalOffset(targetOffset);
        return true;
    }

    private static ScrollViewer? FindScrollableAncestor(
        DependencyObject? source,
        bool horizontal)
    {
        var current = source;
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer &&
                (horizontal
                    ? scrollViewer.ScrollableWidth > 0
                    : scrollViewer.ScrollableHeight > 0))
            {
                return scrollViewer;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in
                     FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        return current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);
    }

    private void OnEventAdded(Guid eventId)
    {
        Dispatcher.BeginInvoke(
            () => RevealAndHighlightEvent(eventId),
            DispatcherPriority.Loaded);
    }

    private void OnDeleteRequested(EventViewModel eventViewModel)
    {
        var eventName = string.IsNullOrWhiteSpace(eventViewModel.NameInput)
            ? "未命名事件"
            : eventViewModel.NameInput.Trim();
        var dialog = new DeleteEventDialog(eventName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (ReferenceEquals(_highlightedEvent, eventViewModel))
        {
            ClearNewEventHighlight();
        }

        _eventListReflowAnimator.DeleteWithReflow(
            eventViewModel,
            () => _viewModel.ConfirmDelete(eventViewModel.Id));
    }

    private void RevealAndHighlightEvent(Guid eventId)
    {
        var eventViewModel = _viewModel.Events.FirstOrDefault(item => item.Id == eventId);
        if (eventViewModel is null)
        {
            return;
        }

        UpdateLayout();
        if (EventItemsControl.ItemContainerGenerator.ContainerFromItem(eventViewModel)
            is FrameworkElement container)
        {
            ClearNewEventHighlight();
            _eventListScroller.ScrollToReveal(
                container,
                viewportPadding: 12,
                () => StartNewEventHighlight(eventViewModel));
            return;
        }

        StartNewEventHighlight(eventViewModel);
    }

    private void StartNewEventHighlight(EventViewModel eventViewModel)
    {
        ClearNewEventHighlight();
        _highlightedEvent = eventViewModel;
        eventViewModel.SetHighlighted(true);
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
        else if (Equals(textBox.Tag, "SchedulePart"))
        {
            eventViewModel.CommitScheduleParts();
        }
    }

    private static void MoveCaretToEnd(TextBox textBox)
    {
        textBox.CaretIndex = textBox.Text?.Length ?? 0;
        textBox.SelectionLength = 0;
    }

    private static string CreateTextAfterInput(
        TextBox textBox,
        string input)
    {
        var currentText = textBox.Text ?? string.Empty;
        var selectionStart = Math.Clamp(
            textBox.SelectionStart,
            0,
            currentText.Length);
        var selectionLength = Math.Clamp(
            textBox.SelectionLength,
            0,
            currentText.Length - selectionStart);
        return currentText
            .Remove(selectionStart, selectionLength)
            .Insert(selectionStart, input);
    }
}
