using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace Reminder.App.UI.Interactions;

public sealed class SectionHighlightAnimator : IDisposable
{
    private static readonly Duration FlashDuration =
        new(TimeSpan.FromMilliseconds(760));

    private Color _normalColor;
    private Color _highlightColor;
    private Border? _activeSection;
    private SolidColorBrush? _animatedBrush;
    private ColorAnimationUsingKeyFrames? _activeAnimation;
    private EventHandler? _completedHandler;
    private bool _disposed;

    public SectionHighlightAnimator(
        Brush normalBackground,
        Brush highlightBackground)
    {
        _normalColor = GetColor(normalBackground);
        _highlightColor = GetColor(highlightBackground);
    }

    public void Flash(Border section)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CompleteImmediately();
        if (!UiMotion.AreAnimationsEnabled)
        {
            return;
        }

        _activeSection = section;
        _animatedBrush = new SolidColorBrush(_normalColor);
        section.Background = _animatedBrush;

        var animation = new ColorAnimationUsingKeyFrames
        {
            Duration = FlashDuration,
            FillBehavior = FillBehavior.Stop
        };
        animation.KeyFrames.Add(
            new EasingColorKeyFrame(
                _highlightColor,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150)),
                new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                }));
        animation.KeyFrames.Add(
            new DiscreteColorKeyFrame(
                _highlightColor,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(310))));
        animation.KeyFrames.Add(
            new EasingColorKeyFrame(
                _normalColor,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(760)),
                new QuadraticEase
                {
                    EasingMode = EasingMode.EaseInOut
                }));

        EventHandler? completedHandler = null;
        completedHandler = (_, _) =>
        {
            animation.Completed -= completedHandler;
            if (ReferenceEquals(_activeAnimation, animation))
            {
                CompleteImmediately();
            }
        };
        animation.Completed += completedHandler;
        _activeAnimation = animation;
        _completedHandler = completedHandler;
        _animatedBrush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    public void UpdatePalette(
        Brush normalBackground,
        Brush highlightBackground)
    {
        ArgumentNullException.ThrowIfNull(normalBackground);
        ArgumentNullException.ThrowIfNull(highlightBackground);
        CompleteImmediately();
        _normalColor = GetColor(normalBackground);
        _highlightColor = GetColor(highlightBackground);
    }

    public void CompleteImmediately()
    {
        if (_activeAnimation is not null &&
            _completedHandler is not null)
        {
            _activeAnimation.Completed -= _completedHandler;
        }

        _animatedBrush?.BeginAnimation(
            SolidColorBrush.ColorProperty,
            null);
        if (_activeSection is not null)
        {
            _activeSection.SetResourceReference(
                Border.BackgroundProperty,
                "SurfaceBrush");
        }

        _activeAnimation = null;
        _completedHandler = null;
        _animatedBrush = null;
        _activeSection = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CompleteImmediately();
        _disposed = true;
    }

    private static Color GetColor(Brush brush)
    {
        return brush is SolidColorBrush solidColorBrush
            ? solidColorBrush.Color
            : throw new ArgumentException(
                "区域闪动仅支持纯色画刷。",
                nameof(brush));
    }
}
