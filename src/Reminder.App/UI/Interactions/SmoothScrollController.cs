using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Reminder.App.UI.Interactions;

public sealed class SmoothScrollController : IDisposable
{
    private const double NaturalFrequency = 18;
    private const double MaximumVelocity = 4_500;
    private readonly ScrollViewer _scrollViewer;
    private readonly bool _animationsEnabled;
    private bool _isAnimating;
    private bool _disposed;
    private long _lastFrameTimestamp;
    private double _targetOffset;
    private double _velocity;
    private Action? _completed;

    public SmoothScrollController(ScrollViewer scrollViewer)
    {
        _scrollViewer = scrollViewer;
        _animationsEnabled = UiMotion.AreAnimationsEnabled;
        _targetOffset = scrollViewer.VerticalOffset;
    }

    public void ScrollBy(double pixels)
    {
        ThrowIfDisposed();
        var origin = _isAnimating
            ? _targetOffset
            : _scrollViewer.VerticalOffset;
        AnimateTo(origin + pixels);
    }

    public void ScrollToReveal(
        FrameworkElement element,
        double viewportPadding,
        Action? completed = null)
    {
        ThrowIfDisposed();
        _scrollViewer.UpdateLayout();

        var top = element.TranslatePoint(
            new System.Windows.Point(0, 0),
            _scrollViewer).Y;
        var bottom = top + element.ActualHeight;
        var viewportBottom = _scrollViewer.ViewportHeight - viewportPadding;
        var target = _scrollViewer.VerticalOffset;

        if (top < viewportPadding)
        {
            target += top - viewportPadding;
        }
        else if (bottom > viewportBottom)
        {
            target += bottom - viewportBottom;
        }

        AnimateTo(target, completed);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAnimation(invokeCompletion: false);
    }

    public void CompleteImmediately(bool invokeCompletion = false)
    {
        ThrowIfDisposed();
        if (_isAnimating)
        {
            _scrollViewer.ScrollToVerticalOffset(
                ClampOffset(_targetOffset));
        }

        StopAnimation(invokeCompletion);
    }

    private void AnimateTo(double targetOffset, Action? completed = null)
    {
        _targetOffset = ClampOffset(targetOffset);
        _completed = completed;

        if (!_animationsEnabled ||
            Math.Abs(_targetOffset - _scrollViewer.VerticalOffset) < 0.5)
        {
            _scrollViewer.ScrollToVerticalOffset(_targetOffset);
            var callback = _completed;
            _completed = null;
            callback?.Invoke();
            return;
        }

        if (_isAnimating)
        {
            return;
        }

        _isAnimating = true;
        _velocity = 0;
        _lastFrameTimestamp = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            StopAnimation(invokeCompletion: false);
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastFrameTimestamp, now);
        _lastFrameTimestamp = now;
        var deltaSeconds = Math.Min(elapsed.TotalSeconds, 1.0 / 30);
        if (deltaSeconds <= 0)
        {
            return;
        }

        _targetOffset = ClampOffset(_targetOffset);
        var current = _scrollViewer.VerticalOffset;
        var displacement = _targetOffset - current;
        var acceleration =
            NaturalFrequency * NaturalFrequency * displacement -
            2 * NaturalFrequency * _velocity;

        _velocity = Math.Clamp(
            _velocity + acceleration * deltaSeconds,
            -MaximumVelocity,
            MaximumVelocity);

        var next = ClampOffset(current + _velocity * deltaSeconds);
        if ((displacement > 0 && next > _targetOffset) ||
            (displacement < 0 && next < _targetOffset))
        {
            next = _targetOffset;
            _velocity = 0;
        }

        _scrollViewer.ScrollToVerticalOffset(next);

        if (Math.Abs(_targetOffset - next) < 0.35 &&
            Math.Abs(_velocity) < 5)
        {
            _scrollViewer.ScrollToVerticalOffset(_targetOffset);
            StopAnimation(invokeCompletion: true);
        }
    }

    private void StopAnimation(bool invokeCompletion)
    {
        if (_isAnimating)
        {
            CompositionTarget.Rendering -= OnRendering;
            _isAnimating = false;
        }

        _velocity = 0;
        var callback = _completed;
        _completed = null;
        if (invokeCompletion)
        {
            callback?.Invoke();
        }
    }

    private double ClampOffset(double offset)
    {
        return Math.Clamp(offset, 0, _scrollViewer.ScrollableHeight);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
