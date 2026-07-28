using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Reminder.App.UI.Interactions;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Reminder.App.UI.Controls;

public sealed class CountdownRingsControl : FrameworkElement
{
    private static readonly Pen SecondsPen = CreatePen("#FF67D2C4");
    private static readonly Pen MinutesPen = CreatePen("#FF61A6E8");
    private static readonly Pen HoursPen = CreatePen("#FF7B82E6");
    private static readonly Pen DaysPen = CreatePen("#FFA581D8");
    private static readonly Pen ExtraYearPen = CreatePen("#FFD8B56A");
    private const double NormalAnimationDurationSeconds = 0.22;
    private const double RolloverAnimationDurationSeconds = 0.95;
    private long _animationStartedAt;
    private RingFractions _animationFrom;
    private RingFractions _animationTarget;
    private RingFractions _displayedFractions;
    private RingFractions _lastTargetFractions;
    private bool _isAnimating;
    private bool _hasValue;
    private bool _showExtraYear;
    private double _animationDurationSeconds =
        NormalAnimationDurationSeconds;

    public static readonly DependencyProperty RemainingSecondsProperty =
        DependencyProperty.Register(
            nameof(RemainingSeconds),
            typeof(double),
            typeof(CountdownRingsControl),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnRemainingSecondsChanged));

    public CountdownRingsControl()
    {
        IsHitTestVisible = false;
        Unloaded += (_, _) => StopAnimation();
    }

    public double RemainingSeconds
    {
        get => (double)GetValue(RemainingSecondsProperty);
        set => SetValue(RemainingSecondsProperty, value);
    }

    public void CompleteImmediately()
    {
        var target = Math.Max(0, RemainingSeconds);
        _hasValue = true;
        _displayedFractions = RingFractions.FromSeconds(target);
        _lastTargetFractions = _displayedFractions;
        _showExtraYear = HasExtraYear(target);
        StopAnimation();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var baseRadius = Math.Max(
            0,
            Math.Min(ActualWidth, ActualHeight) / 2 - 34);
        if (baseRadius <= 1)
        {
            return;
        }

        DrawArc(
            drawingContext,
            center,
            baseRadius - 36,
            _displayedFractions.Seconds,
            SecondsPen);
        DrawArc(
            drawingContext,
            center,
            baseRadius - 24,
            _displayedFractions.Minutes,
            MinutesPen);
        DrawArc(
            drawingContext,
            center,
            baseRadius - 12,
            _displayedFractions.Hours,
            HoursPen);
        DrawArc(
            drawingContext,
            center,
            baseRadius,
            _displayedFractions.Days,
            DaysPen);
        if (_showExtraYear)
        {
            drawingContext.DrawEllipse(
                null,
                ExtraYearPen,
                center,
                baseRadius + 12,
                baseRadius + 12);
        }
    }

    private static void OnRemainingSecondsChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var control = (CountdownRingsControl)dependencyObject;
        var target = Math.Max(0, (double)e.NewValue);
        var targetFractions = RingFractions.FromSeconds(target);
        if (!control._hasValue ||
            !control.IsLoaded ||
            !control.IsVisible ||
            !UiMotion.AreAnimationsEnabled)
        {
            control._hasValue = true;
            control._displayedFractions = targetFractions;
            control._lastTargetFractions = targetFractions;
            control._showExtraYear = HasExtraYear(target);
            control.StopAnimation();
            control.InvalidateVisual();
            return;
        }

        control._animationFrom = control._displayedFractions;
        control._animationTarget = targetFractions;
        control._animationDurationSeconds =
            IsComponentRollover(
                control._lastTargetFractions,
                targetFractions)
                ? RolloverAnimationDurationSeconds
                : NormalAnimationDurationSeconds;
        control._lastTargetFractions = targetFractions;
        control._showExtraYear = HasExtraYear(target);
        control._animationStartedAt = Stopwatch.GetTimestamp();
        if (!control._isAnimating)
        {
            CompositionTarget.Rendering += control.OnRendering;
            control._isAnimating = true;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            CompleteImmediately();
            return;
        }

        var elapsed =
            Stopwatch.GetElapsedTime(_animationStartedAt).TotalSeconds;
        var progress = Math.Clamp(
            elapsed / _animationDurationSeconds,
            0,
            1);
        var eased = progress < 0.5
            ? 4 * progress * progress * progress
            : 1 - Math.Pow(-2 * progress + 2, 3) / 2;
        _displayedFractions = RingFractions.Lerp(
            _animationFrom,
            _animationTarget,
            eased);
        InvalidateVisual();

        if (progress >= 1)
        {
            _displayedFractions = _animationTarget;
            StopAnimation();
        }
    }

    private void StopAnimation()
    {
        if (!_isAnimating)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _isAnimating = false;
    }

    private static bool IsComponentRollover(
        RingFractions from,
        RingFractions target)
    {
        return target.Seconds > from.Seconds ||
               target.Minutes > from.Minutes ||
               target.Hours > from.Hours ||
               target.Days > from.Days;
    }

    private static bool HasExtraYear(double totalSeconds)
    {
        return Math.Floor(Math.Max(0, totalSeconds) / 86_400) >
               365;
    }

    private static void DrawArc(
        DrawingContext drawingContext,
        Point center,
        double radius,
        double fraction,
        Pen pen)
    {
        if (radius <= 0 || fraction <= 0.0001)
        {
            return;
        }

        if (fraction >= 0.9999)
        {
            drawingContext.DrawEllipse(
                null,
                pen,
                center,
                radius,
                radius);
            return;
        }

        var start = new Point(center.X, center.Y - radius);
        var angle = fraction * Math.PI * 2 - Math.PI / 2;
        var end = new Point(
            center.X + Math.Cos(angle) * radius,
            center.Y + Math.Sin(angle) * radius);
        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(
            new ArcSegment(
                end,
                new Size(radius, radius),
                0,
                fraction > 0.5,
                SweepDirection.Clockwise,
                true));
        var geometry = new PathGeometry([figure]);
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Pen CreatePen(string color)
    {
        var brush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        var pen = new Pen(brush, 7)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return pen;
    }

    private readonly record struct RingFractions(
        double Seconds,
        double Minutes,
        double Hours,
        double Days)
    {
        public static RingFractions FromSeconds(
            double totalSeconds)
        {
            totalSeconds = Math.Max(0, totalSeconds);
            var days = Math.Floor(totalSeconds / 86_400);
            return new RingFractions(
                totalSeconds % 60 / 60,
                Math.Floor(totalSeconds / 60) % 60 / 60,
                Math.Floor(totalSeconds / 3_600) % 24 / 24,
                days == 365 ? 1 : days % 365 / 365);
        }

        public static RingFractions Lerp(
            RingFractions from,
            RingFractions target,
            double progress)
        {
            return new RingFractions(
                Interpolate(from.Seconds, target.Seconds, progress),
                Interpolate(from.Minutes, target.Minutes, progress),
                Interpolate(from.Hours, target.Hours, progress),
                Interpolate(from.Days, target.Days, progress));
        }

        private static double Interpolate(
            double from,
            double target,
            double progress)
        {
            return from + (target - from) * progress;
        }
    }
}
