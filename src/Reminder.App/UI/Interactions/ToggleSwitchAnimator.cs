using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace Reminder.App.UI.Interactions;

public static class ToggleSwitchAnimator
{
    public static readonly DependencyProperty IsAnimationEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsAnimationEnabled",
            typeof(bool),
            typeof(ToggleSwitchAnimator),
            new PropertyMetadata(false, OnIsAnimationEnabledChanged));

    private static readonly DependencyProperty AnimationGenerationProperty =
        DependencyProperty.RegisterAttached(
            "AnimationGeneration",
            typeof(int),
            typeof(ToggleSwitchAnimator),
            new PropertyMetadata(0));

    public static void SetIsAnimationEnabled(
        DependencyObject element,
        bool value)
    {
        element.SetValue(IsAnimationEnabledProperty, value);
    }

    public static bool GetIsAnimationEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(IsAnimationEnabledProperty);
    }

    private static void OnIsAnimationEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not WpfCheckBox checkBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            checkBox.Loaded += OnLoaded;
            checkBox.Unloaded += OnUnloaded;
            checkBox.Checked += OnCheckedChanged;
            checkBox.Unchecked += OnCheckedChanged;
            return;
        }

        checkBox.Loaded -= OnLoaded;
        checkBox.Unloaded -= OnUnloaded;
        checkBox.Checked -= OnCheckedChanged;
        checkBox.Unchecked -= OnCheckedChanged;
        CompleteImmediately(checkBox);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfCheckBox checkBox)
        {
            SetThumbPosition(checkBox, animate: false);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfCheckBox checkBox)
        {
            CompleteImmediately(checkBox);
        }
    }

    private static void OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is WpfCheckBox checkBox && checkBox.IsLoaded)
        {
            SetThumbPosition(checkBox, animate: true);
        }
    }

    private static void SetThumbPosition(WpfCheckBox checkBox, bool animate)
    {
        if (!TryGetTemplateParts(
                checkBox,
                out var thumb,
                out var thumbTransform))
        {
            return;
        }

        var controlWidth = checkBox.ActualWidth > 0
            ? checkBox.ActualWidth
            : checkBox.Width;
        var thumbWidth = thumb.ActualWidth > 0
            ? thumb.ActualWidth
            : thumb.Width;
        var travel = Math.Max(
            0,
            controlWidth -
            thumbWidth -
            thumb.Margin.Left -
            thumb.Margin.Right);
        var target = checkBox.IsChecked == true ? travel : 0;
        var generation = GetAnimationGeneration(checkBox) + 1;
        SetAnimationGeneration(checkBox, generation);

        var current = thumbTransform.X;
        thumbTransform.BeginAnimation(
            TranslateTransform.XProperty,
            null);
        thumbTransform.X = current;

        if (!animate ||
            !UiMotion.AreAnimationsEnabled ||
            Math.Abs(current - target) < 0.01)
        {
            thumbTransform.X = target;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = current,
            To = target,
            Duration = TimeSpan.FromMilliseconds(165),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            },
            FillBehavior = FillBehavior.HoldEnd
        };
        animation.Completed += (_, _) =>
        {
            if (GetAnimationGeneration(checkBox) != generation)
            {
                return;
            }

            thumbTransform.BeginAnimation(
                TranslateTransform.XProperty,
                null);
            thumbTransform.X = target;
        };
        thumbTransform.BeginAnimation(
            TranslateTransform.XProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void CompleteImmediately(WpfCheckBox checkBox)
    {
        SetAnimationGeneration(
            checkBox,
            GetAnimationGeneration(checkBox) + 1);
        if (!TryGetTemplateParts(
                checkBox,
                out var thumb,
                out var thumbTransform))
        {
            return;
        }

        var controlWidth = checkBox.ActualWidth > 0
            ? checkBox.ActualWidth
            : checkBox.Width;
        var thumbWidth = thumb.ActualWidth > 0
            ? thumb.ActualWidth
            : thumb.Width;
        var travel = Math.Max(
            0,
            controlWidth -
            thumbWidth -
            thumb.Margin.Left -
            thumb.Margin.Right);
        thumbTransform.BeginAnimation(
            TranslateTransform.XProperty,
            null);
        thumbTransform.X =
            checkBox.IsChecked == true
                ? travel
                : 0;
    }

    private static bool TryGetTemplateParts(
        WpfCheckBox checkBox,
        out Ellipse thumb,
        out TranslateTransform thumbTransform)
    {
        checkBox.ApplyTemplate();
        thumb =
            checkBox.Template.FindName("Thumb", checkBox) as Ellipse
            ?? null!;
        thumbTransform =
            checkBox.Template.FindName(
                "ThumbTransform",
                checkBox) as TranslateTransform
            ?? null!;
        return thumb is not null && thumbTransform is not null;
    }

    private static int GetAnimationGeneration(DependencyObject element)
    {
        return (int)element.GetValue(AnimationGenerationProperty);
    }

    private static void SetAnimationGeneration(
        DependencyObject element,
        int value)
    {
        element.SetValue(AnimationGenerationProperty, value);
    }
}
