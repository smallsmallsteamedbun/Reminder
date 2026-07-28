using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Reminder.App.UI.Interactions;

internal sealed class PageNavigationController
{
    private readonly FrameworkElement _homePage;
    private readonly FrameworkElement _eventPage;
    private readonly FrameworkElement _settingsPage;
    private readonly FrameworkElement _globalActions;
    private readonly FrameworkElement _eventSummary;
    private readonly FrameworkElement _addEventButton;
    private readonly Button _homeNavigationButton;
    private readonly Button _eventsNavigationButton;
    private readonly Button _settingsNavigationButton;
    private readonly Brush _selectedBackground;
    private readonly Brush _selectedForeground;
    private readonly Brush _normalForeground;
    private bool _transitionInProgress;
    private ReminderPage? _transitionTarget;
    private Action? _transitionCompleted;
    private ReminderPage? _pendingTarget;
    private Action? _pendingCompleted;

    public PageNavigationController(
        FrameworkElement homePage,
        FrameworkElement eventPage,
        FrameworkElement settingsPage,
        FrameworkElement globalActions,
        FrameworkElement eventSummary,
        FrameworkElement addEventButton,
        Button homeNavigationButton,
        Button eventsNavigationButton,
        Button settingsNavigationButton,
        Brush selectedBackground,
        Brush selectedForeground,
        Brush normalForeground)
    {
        _homePage = homePage;
        _eventPage = eventPage;
        _settingsPage = settingsPage;
        _globalActions = globalActions;
        _eventSummary = eventSummary;
        _addEventButton = addEventButton;
        _homeNavigationButton = homeNavigationButton;
        _eventsNavigationButton = eventsNavigationButton;
        _settingsNavigationButton = settingsNavigationButton;
        _selectedBackground = selectedBackground;
        _selectedForeground = selectedForeground;
        _normalForeground = normalForeground;

        Initialize();
    }

    public ReminderPage CurrentPage { get; private set; } =
        ReminderPage.Home;

    public void Navigate(
        ReminderPage targetPage,
        Action? completed = null)
    {
        if (_transitionInProgress)
        {
            _pendingTarget = targetPage;
            _pendingCompleted = completed;
            return;
        }

        if (targetPage == CurrentPage)
        {
            completed?.Invoke();
            return;
        }

        _transitionInProgress = true;
        _transitionTarget = targetPage;
        _transitionCompleted = completed;
        var outgoing = GetPage(CurrentPage);
        var incoming = GetPage(targetPage);
        if (UiMotion.AreAnimationsEnabled)
        {
            AnimateElement(
                outgoing,
                0,
                0,
                CurrentPage == ReminderPage.Events ? 28 : 0,
                TimeSpan.FromMilliseconds(115),
                () => CompletePageSwitch(
                    outgoing,
                    incoming,
                    targetPage));
            return;
        }

        CompletePageSwitch(outgoing, incoming, targetPage);
    }

    public void CompleteImmediately()
    {
        var target =
            _pendingTarget ??
            _transitionTarget ??
            CurrentPage;
        var completed =
            _pendingTarget is not null
                ? _pendingCompleted
                : _transitionCompleted;
        _pendingTarget = null;
        _pendingCompleted = null;
        _transitionTarget = null;
        _transitionCompleted = null;
        _transitionInProgress = false;
        CurrentPage = target;

        foreach (var page in new[]
                 {
                     _homePage,
                     _eventPage,
                     _settingsPage
                 })
        {
            page.BeginAnimation(UIElement.OpacityProperty, null);
            page.Opacity = 1;
            ResetTranslate(page);
            page.Visibility =
                ReferenceEquals(page, GetPage(target))
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        SetActionElementImmediately(
            _globalActions,
            target != ReminderPage.Settings);
        SetActionElementImmediately(
            _eventSummary,
            target == ReminderPage.Events);
        SetActionElementImmediately(
            _addEventButton,
            target == ReminderPage.Events);
        SetSelectedNavigationButton(GetNavigationButton(target));
        completed?.Invoke();
    }

    private void Initialize()
    {
        _homePage.Visibility = Visibility.Visible;
        _eventPage.Visibility = Visibility.Collapsed;
        _settingsPage.Visibility = Visibility.Collapsed;
        _eventSummary.Visibility = Visibility.Collapsed;
        _addEventButton.Visibility = Visibility.Collapsed;
        _globalActions.Visibility = Visibility.Visible;
        SetSelectedNavigationButton(_homeNavigationButton);

        foreach (var element in new[]
                 {
                     _homePage,
                     _eventPage,
                     _settingsPage,
                     _globalActions,
                     _eventSummary,
                     _addEventButton
                 })
        {
            EnsureTranslateTransform(element);
        }
    }

    private void CompletePageSwitch(
        FrameworkElement outgoing,
        FrameworkElement incoming,
        ReminderPage targetPage)
    {
        outgoing.Visibility = Visibility.Collapsed;
        outgoing.Opacity = 1;
        ResetTranslate(outgoing);
        ApplyActionBarState(targetPage);

        incoming.Visibility = Visibility.Visible;
        incoming.Opacity = UiMotion.AreAnimationsEnabled ? 0 : 1;
        var incomingTransform = EnsureTranslateTransform(incoming);
        incomingTransform.Y =
            UiMotion.AreAnimationsEnabled &&
            targetPage == ReminderPage.Events
                ? 28
                : 0;
        CurrentPage = targetPage;
        SetSelectedNavigationButton(GetNavigationButton(targetPage));

        void Finish()
        {
            _transitionInProgress = false;
            _transitionTarget = null;
            var completed = _transitionCompleted;
            _transitionCompleted = null;

            if (_pendingTarget is { } pendingTarget)
            {
                var pendingCompleted = _pendingCompleted;
                _pendingTarget = null;
                _pendingCompleted = null;
                Navigate(pendingTarget, pendingCompleted);
                return;
            }

            completed?.Invoke();
        }

        if (UiMotion.AreAnimationsEnabled)
        {
            AnimateElement(
                incoming,
                1,
                0,
                0,
                TimeSpan.FromMilliseconds(190),
                Finish);
            return;
        }

        Finish();
    }

    private void ApplyActionBarState(ReminderPage page)
    {
        SetActionElementState(
            _globalActions,
            page != ReminderPage.Settings);
        SetActionElementState(
            _eventSummary,
            page == ReminderPage.Events);
        SetActionElementState(
            _addEventButton,
            page == ReminderPage.Events);
    }

    private static void SetActionElementState(
        FrameworkElement element,
        bool isVisible)
    {
        var wasVisible =
            element.Visibility == Visibility.Visible;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        ResetTranslate(element);
        if (!isVisible &&
            wasVisible &&
            UiMotion.AreAnimationsEnabled)
        {
            AnimateElement(
                element,
                0,
                20,
                0,
                TimeSpan.FromMilliseconds(140),
                () =>
                {
                    element.Visibility = Visibility.Collapsed;
                    element.Opacity = 1;
                    ResetTranslate(element);
                });
            return;
        }

        element.Visibility =
            isVisible ? Visibility.Visible : Visibility.Collapsed;
        element.Opacity = 1;
        if (!isVisible || !UiMotion.AreAnimationsEnabled)
        {
            return;
        }

        if (wasVisible)
        {
            return;
        }

        var transform = EnsureTranslateTransform(element);
        transform.X = 20;
        var opacityAnimation = new DoubleAnimation(
            0,
            1,
            TimeSpan.FromMilliseconds(170));
        opacityAnimation.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        };
        element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        AnimateDouble(
            transform,
            TranslateTransform.XProperty,
            20,
            0,
            TimeSpan.FromMilliseconds(170));
    }

    private static void SetActionElementImmediately(
        FrameworkElement element,
        bool isVisible)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        ResetTranslate(element);
        element.Visibility =
            isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void AnimateElement(
        FrameworkElement element,
        double targetOpacity,
        double targetX,
        double targetY,
        TimeSpan duration,
        Action? completed = null)
    {
        var transform = EnsureTranslateTransform(element);
        var opacity = new DoubleAnimation(
            element.Opacity,
            targetOpacity,
            duration)
        {
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            }
        };
        opacity.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = targetOpacity;
            completed?.Invoke();
        };
        element.BeginAnimation(UIElement.OpacityProperty, opacity);
        AnimateDouble(
            transform,
            TranslateTransform.XProperty,
            transform.X,
            targetX,
            duration);
        AnimateDouble(
            transform,
            TranslateTransform.YProperty,
            transform.Y,
            targetY,
            duration);
    }

    private static void AnimateDouble(
        Animatable target,
        DependencyProperty property,
        double from,
        double to,
        TimeSpan duration)
    {
        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
        };
        target.BeginAnimation(property, animation);
    }

    private FrameworkElement GetPage(ReminderPage page)
    {
        return page switch
        {
            ReminderPage.Home => _homePage,
            ReminderPage.Events => _eventPage,
            _ => _settingsPage
        };
    }

    private Button GetNavigationButton(ReminderPage page)
    {
        return page switch
        {
            ReminderPage.Home => _homeNavigationButton,
            ReminderPage.Events => _eventsNavigationButton,
            _ => _settingsNavigationButton
        };
    }

    private void SetSelectedNavigationButton(Button selected)
    {
        foreach (var button in new[]
                 {
                     _homeNavigationButton,
                     _eventsNavigationButton,
                     _settingsNavigationButton
                 })
        {
            button.Background =
                ReferenceEquals(button, selected)
                    ? _selectedBackground
                    : Brushes.Transparent;
            button.Foreground =
                ReferenceEquals(button, selected)
                    ? _selectedForeground
                    : _normalForeground;
        }
    }

    private static TranslateTransform EnsureTranslateTransform(
        FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform transform)
        {
            return transform;
        }

        transform = new TranslateTransform();
        element.RenderTransform = transform;
        return transform;
    }

    private static void ResetTranslate(FrameworkElement element)
    {
        var transform = EnsureTranslateTransform(element);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.X = 0;
        transform.Y = 0;
    }
}
