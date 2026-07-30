using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Reminder.App.Logic.Services;
using Reminder.App.SystemModule.Settings;
using Reminder.App.SystemModule.Runtime;
using Reminder.App.UI.Interactions;
using Reminder.App.UI.Theming;
using Reminder.App.UI.ViewModels;
using Reminder.App.Windows.Appearance;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using DataFormats = System.Windows.DataFormats;
using TextBox = System.Windows.Controls.TextBox;
using ToolTip = System.Windows.Controls.ToolTip;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Brush = System.Windows.Media.Brush;

namespace Reminder.App.UI.Views;

public partial class MainWindow : Window
{
    private const int WmActivateApp = 0x001C;
    private const int WmMouseHorizontalWheel = 0x020E;
    private readonly MainViewModel _viewModel;
    private readonly ReminderThemeService _themeService;
    private readonly DispatcherTimer _countdownRefreshTimer;
    private readonly DispatcherTimer _newEventHighlightTimer;
    private readonly SmoothScrollController _eventListScroller;
    private readonly SmoothScrollController _settingsScroller;
    private readonly SectionHighlightAnimator _settingsSectionHighlighter;
    private readonly ListReflowAnimator _eventListReflowAnimator;
    private readonly PageNavigationController _pageNavigator;
    private readonly ContentControl[] _homeCardSlots;
    private HwndSource? _windowSource;
    private EventViewModel? _highlightedEvent;
    private bool _allowApplicationExit;
    private bool _isCommittingInterval;
    private Guid? _revealWhenVisibleEventId;

    public MainWindow(
        MainViewModel viewModel,
        ReminderThemeService themeService)
    {
        _viewModel = viewModel;
        _themeService = themeService;
        DataContext = viewModel;
        InitializeComponent();
        _homeCardSlots =
        [
            HomeTopCard,
            HomeMiddleCard,
            HomeBottomCard
        ];
        _pageNavigator = new PageNavigationController(
            HomePage,
            EventPage,
            SettingsPage,
            GlobalActionsPanel,
            EventSummaryPanel,
            AddEventButton,
            HomeNavigationButton,
            EventsNavigationButton,
            SettingsNavigationButton,
            (Brush)FindResource("PrimarySubtleBrush"),
            (Brush)FindResource("PrimaryBrush"),
            (Brush)FindResource("TextSecondaryBrush"));
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
        _settingsScroller = new SmoothScrollController(SettingsScrollViewer);
        _settingsSectionHighlighter = new SectionHighlightAnimator(
            (Brush)FindResource("SurfaceBrush"),
            (Brush)FindResource("PrimarySubtleBrush"));
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
        _viewModel.RenderingModeChangeRequested +=
            OnRenderingModeChangeRequested;
        _viewModel.HomePresentationChanged +=
            OnHomePresentationChanged;
        _themeService.ThemeChanged += OnThemeChanged;

        Loaded += (_, _) => StartUiRefresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                StartUiRefresh();
                if (_revealWhenVisibleEventId is { } eventId)
                {
                    _revealWhenVisibleEventId = null;
                    NavigateTo(ReminderPage.Events, eventId);
                }
            }
            else
            {
                _countdownRefreshTimer.Stop();
                HomeCountdownRings.CompleteImmediately();
                _pageNavigator.CompleteImmediately();
                HomePresentationAnimator.CompleteImmediately(
                    HomeTimerButton,
                    _homeCardSlots,
                    FindVisualDescendants<Button>(HomePage)
                        .Where(item =>
                            item.DataContext is HomeReminderViewModel));
                _eventListScroller.CompleteImmediately();
                _settingsScroller.CompleteImmediately();
                _settingsSectionHighlighter.CompleteImmediately();
                _eventListReflowAnimator.CompleteImmediately();
                Dispatcher.BeginInvoke(
                    ProcessMemoryTrimmer.TrimAfterWindowHidden,
                    DispatcherPriority.ApplicationIdle);
            }
        };
    }

    public event Action? RestartRequested;

    public void ShowHomeAndActivate()
    {
        var wasVisible = IsVisible;
        NavigateTo(ReminderPage.Home);
        if (!wasVisible)
        {
            _pageNavigator.CompleteImmediately();
        }

        ShowAndActivate();
    }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        _themeService.Reapply();
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
        WindowsWindowThemeService.ApplyDarkTitleBar(
            this,
            _themeService.IsDark);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        CloseTransientPopups();
        base.OnDeactivated(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RenderingModeChangeRequested -=
            OnRenderingModeChangeRequested;
        _viewModel.HomePresentationChanged -=
            OnHomePresentationChanged;
        _themeService.ThemeChanged -= OnThemeChanged;
        _eventListScroller.Dispose();
        _settingsScroller.Dispose();
        _settingsSectionHighlighter.Dispose();
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

    private void IntervalTextBox_OnKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        CommitInterval(sender as FrameworkElement);
        e.Handled = true;
    }

    private void IntervalInputGroup_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender is not FrameworkElement group)
        {
            return;
        }

        if (e.NewFocus is DependencyObject newFocus &&
            IsVisualDescendant(group, newFocus))
        {
            return;
        }

        CommitInterval(group);
    }

    private void SnoozeDurationTextBox_OnKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        _viewModel.CommitSnoozeDuration();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void SnoozeDurationInputGroup_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender is not FrameworkElement group)
        {
            return;
        }

        if (e.NewFocus is DependencyObject newFocus &&
            IsVisualDescendant(group, newFocus))
        {
            return;
        }

        _viewModel.CommitSnoozeDuration();
    }

    private void EventSearchTextBox_OnGotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        EventSearchHistoryPopup.IsOpen =
            _viewModel.HasSearchHistory;
    }

    private void EventSearchTextBox_OnKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        _viewModel.CommitSearch();
        EventSearchHistoryPopup.IsOpen = false;
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void ClearEventSearchButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.ClearSearch();
        EventSearchHistoryPopup.IsOpen = false;
        EventSearchTextBox.Focus();
    }

    private void SearchHistoryItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button { Tag: string query })
        {
            _viewModel.SelectSearchHistory(query);
        }

        EventSearchHistoryPopup.IsOpen = false;
    }

    private void DeleteSearchHistoryButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button { Tag: string query })
        {
            _viewModel.RemoveSearchHistory(query);
        }

        EventSearchHistoryPopup.IsOpen =
            _viewModel.HasSearchHistory;
        e.Handled = true;
    }

    private void ClearSearchHistoryButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.ClearSearchHistory();
        EventSearchHistoryPopup.IsOpen = false;
    }

    private void RestoreDefaultSettingsButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new RestoreDefaultSettingsDialog
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.RestoreDefaultSettings();
        }
    }

    private void RenderingSettingsIndexButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        ScrollToSettingsSection(RenderingSettingsSection);
    }

    private void AppearanceSettingsIndexButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        ScrollToSettingsSection(AppearanceSettingsSection);
    }

    private void StartupSettingsIndexButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        ScrollToSettingsSection(StartupSettingsSection);
    }

    private void NotificationSettingsIndexButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        ScrollToSettingsSection(NotificationSettingsSection);
    }

    private void RestoreDefaultsSettingsIndexButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        ScrollToSettingsSection(RestoreDefaultsSettingsSection);
    }

    private void ScrollToSettingsSection(FrameworkElement section)
    {
        _settingsScroller.CompleteImmediately();
        _settingsSectionHighlighter.CompleteImmediately();
        SettingsScrollViewer.UpdateLayout();
        var sectionTop = section.TranslatePoint(
            new System.Windows.Point(0, 0),
            SettingsScrollViewer).Y;
        _settingsScroller.ScrollBy(
            sectionTop - 12,
            () =>
            {
                if (section is Border border)
                {
                    _settingsSectionHighlighter.Flash(border);
                }
            });
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
        var source = e.OriginalSource as DependencyObject;
        if (e.ChangedButton == MouseButton.Left)
        {
            if (source is not null &&
                IsDescendantOf(source, EventSearchTextBox))
            {
                EventSearchHistoryPopup.IsOpen =
                    _viewModel.HasSearchHistory;
            }
            else
            {
                CommitSearchOnExternalMouseClick(source);
            }
        }

        if (e.ChangedButton != MouseButton.Left ||
            FindAncestor<TextBox>(
                source) is not { } textBox ||
            textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        textBox.Focus();
        MoveCaretToEnd(textBox);
        e.Handled = true;
    }

    private void CommitSearchOnExternalMouseClick(
        DependencyObject? source)
    {
        if ((!EventSearchTextBox.IsKeyboardFocusWithin &&
             !EventSearchHistoryPopup.IsOpen) ||
            source is null ||
            IsDescendantOf(source, EventSearchTextBox))
        {
            return;
        }

        _viewModel.CommitSearch();
        EventSearchHistoryPopup.IsOpen = false;
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
        if (message == WmActivateApp &&
            wParam == IntPtr.Zero)
        {
            CloseTransientPopups();
            return IntPtr.Zero;
        }

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

    private void CloseTransientPopups()
    {
        EventSearchHistoryPopup.IsOpen = false;
        foreach (var comboBox in
                 FindVisualDescendants<ComboBox>(this)
                     .Where(item => item.IsDropDownOpen))
        {
            comboBox.IsDropDownOpen = false;
        }
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
        EventSearchHistoryPopup.IsOpen = false;
        Dispatcher.BeginInvoke(
            () => RevealAndHighlightEvent(eventId),
            DispatcherPriority.Loaded);
    }

    private void HomeNavigationButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        NavigateTo(ReminderPage.Home);
    }

    private void EventsNavigationButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        NavigateTo(ReminderPage.Events);
    }

    private void SettingsNavigationButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        NavigateTo(ReminderPage.Settings);
    }

    private void HomeEventCard_OnMouseEnter(
        object sender,
        MouseEventArgs e)
    {
        if (sender is not Button
            {
                DataContext: HomeReminderViewModel homeEvent
            } button)
        {
            return;
        }

        _viewModel.PreviewHomeEvent(homeEvent.Id);
        HomeCountdownRings.CompleteImmediately();
        HomePresentationAnimator.AnimateCard(button, isRaised: true);
        HomePresentationAnimator.PulseTimer(HomeTimerButton);
    }

    private void HomeEventCard_OnMouseLeave(
        object sender,
        MouseEventArgs e)
    {
        if (sender is Button button)
        {
            HomePresentationAnimator.AnimateCard(
                button,
                isRaised: false);
        }

        _viewModel.PreviewHomeEvent(null);
        HomeCountdownRings.CompleteImmediately();
        HomePresentationAnimator.PulseTimer(HomeTimerButton);
    }

    private void HomeEventCard_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button
            {
                DataContext: HomeReminderViewModel homeEvent
            })
        {
            NavigateTo(ReminderPage.Events, homeEvent.Id);
        }
    }

    private void HomeTimerButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        NavigateTo(
            ReminderPage.Events,
            _viewModel.SelectedHomeEvent?.Id);
    }

    private void OnRenderingModeChangeRequested(
        ReminderRenderingMode renderingMode)
    {
        var dialog = new RenderingModeRestartDialog
        {
            Owner = this
        };
        _ = dialog.ShowDialog();
        if (dialog.RestartNow)
        {
            RestartRequested?.Invoke();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _settingsSectionHighlighter.UpdatePalette(
            (Brush)FindResource("SurfaceBrush"),
            (Brush)FindResource("PrimarySubtleBrush"));
        _pageNavigator.UpdatePalette(
            (Brush)FindResource("PrimarySubtleBrush"),
            (Brush)FindResource("PrimaryBrush"),
            (Brush)FindResource("TextSecondaryBrush"));
        WindowsWindowThemeService.ApplyDarkTitleBar(
            this,
            _themeService.IsDark);
    }

    private void OnHomePresentationChanged(
        IReadOnlyCollection<Guid> changedEventIds)
    {
        if (_pageNavigator.CurrentPage == ReminderPage.Home &&
            IsVisible)
        {
            HomeCountdownRings.CompleteImmediately();
            HomePresentationAnimator.PulseCards(
                _homeCardSlots,
                changedEventIds);
            HomePresentationAnimator.PulseTimer(HomeTimerButton);
        }
    }

    private void NavigateTo(
        ReminderPage page,
        Guid? revealEventId = null)
    {
        EventSearchHistoryPopup.IsOpen = false;
        if (page != ReminderPage.Settings)
        {
            _settingsScroller.CompleteImmediately();
            _settingsSectionHighlighter.CompleteImmediately();
        }

        if (page == ReminderPage.Events && revealEventId is not null)
        {
            _viewModel.ClearSearch();
        }

        Action? completed = null;
        if (page == ReminderPage.Events &&
            revealEventId is not null)
        {
            completed = () =>
            {
                if (!IsVisible)
                {
                    _revealWhenVisibleEventId = revealEventId;
                    return;
                }

                _ = Dispatcher.BeginInvoke(
                    () => RevealAndHighlightEvent(
                        revealEventId.Value),
                    DispatcherPriority.Loaded);
            };
        }

        _pageNavigator.Navigate(page, completed);
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
        else if (Equals(textBox.Tag, "SchedulePart"))
        {
            eventViewModel.CommitScheduleParts();
        }
    }

    private void CommitInterval(FrameworkElement? source)
    {
        if (_isCommittingInterval ||
            source?.DataContext is not EventViewModel eventViewModel)
        {
            return;
        }

        _isCommittingInterval = true;
        try
        {
            eventViewModel.CommitInterval();
        }
        finally
        {
            _isCommittingInterval = false;
        }
    }

    private static bool IsVisualDescendant(
        DependencyObject ancestor,
        DependencyObject descendant)
    {
        for (var current = descendant;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
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
