using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Reminder.App.UI.ViewModels;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace Reminder.App.UI.Interactions;

internal static class HomePresentationAnimator
{
    public static void AnimateCard(Button button, bool isRaised)
    {
        if (button.RenderTransform.IsFrozen)
        {
            button.RenderTransform =
                button.RenderTransform.CloneCurrentValue();
        }

        if (button.RenderTransform is not TransformGroup group ||
            group.Children.Count < 2 ||
            group.Children[0] is not ScaleTransform scale ||
            group.Children[1] is not TranslateTransform translate)
        {
            return;
        }

        var duration = TimeSpan.FromMilliseconds(180);
        if (!UiMotion.AreAnimationsEnabled)
        {
            scale.ScaleX = scale.ScaleY = isRaised ? 1.025 : 1;
            translate.Y = isRaised ? -7 : 0;
            return;
        }

        AnimateDouble(
            scale,
            ScaleTransform.ScaleXProperty,
            scale.ScaleX,
            isRaised ? 1.025 : 1,
            duration);
        AnimateDouble(
            scale,
            ScaleTransform.ScaleYProperty,
            scale.ScaleY,
            isRaised ? 1.025 : 1,
            duration);
        AnimateDouble(
            translate,
            TranslateTransform.YProperty,
            translate.Y,
            isRaised ? -7 : 0,
            duration);
    }

    public static void PulseTimer(Button timerButton)
    {
        if (timerButton.RenderTransform.IsFrozen)
        {
            timerButton.RenderTransform =
                timerButton.RenderTransform.CloneCurrentValue();
        }

        if (!UiMotion.AreAnimationsEnabled ||
            timerButton.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        var animation = CreatePulseAnimation();
        animation.Completed += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = 1;
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    public static void PulseCards(
        IEnumerable<ContentControl> slots,
        IReadOnlyCollection<Guid> changedEventIds)
    {
        if (!UiMotion.AreAnimationsEnabled ||
            changedEventIds.Count == 0)
        {
            return;
        }

        foreach (var slot in slots)
        {
            if (slot.Content is not HomeReminderViewModel homeEvent ||
                !changedEventIds.Contains(homeEvent.Id))
            {
                continue;
            }

            var scale = slot.RenderTransform as ScaleTransform;
            if (scale is null || scale.IsFrozen)
            {
                scale = new ScaleTransform(1, 1);
                slot.RenderTransform = scale;
                slot.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var animation = CreatePulseAnimation();
            animation.Completed += (_, _) =>
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = scale.ScaleY = 1;
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
    }

    public static void CompleteImmediately(
        Button timerButton,
        IEnumerable<ContentControl> slots,
        IEnumerable<Button> cards)
    {
        foreach (var button in cards)
        {
            button.BeginAnimation(UIElement.OpacityProperty, null);
            if (button.RenderTransform.IsFrozen ||
                button.RenderTransform is not TransformGroup group)
            {
                continue;
            }

            foreach (var transform in group.Children)
            {
                if (transform is ScaleTransform scale)
                {
                    scale.BeginAnimation(
                        ScaleTransform.ScaleXProperty,
                        null);
                    scale.BeginAnimation(
                        ScaleTransform.ScaleYProperty,
                        null);
                    scale.ScaleX = scale.ScaleY = 1;
                }
                else if (transform is TranslateTransform translate)
                {
                    translate.BeginAnimation(
                        TranslateTransform.YProperty,
                        null);
                    translate.Y = 0;
                }
            }
        }

        ResetScale(timerButton.RenderTransform);
        foreach (var slot in slots)
        {
            ResetScale(slot.RenderTransform);
        }
    }

    private static DoubleAnimationUsingKeyFrames CreatePulseAnimation()
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0.985,
                KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1.02,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(105)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                1,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
        return animation;
    }

    private static void ResetScale(Transform transform)
    {
        if (transform.IsFrozen ||
            transform is not ScaleTransform scale)
        {
            return;
        }

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = scale.ScaleY = 1;
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
}
