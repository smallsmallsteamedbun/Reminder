using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Reminder.App.UI.Interactions;

public sealed class ListReflowAnimator : IDisposable
{
    private static readonly Duration DeleteFadeDuration =
        new(TimeSpan.FromMilliseconds(90));
    private static readonly Duration ReflowDuration =
        new(TimeSpan.FromMilliseconds(190));

    private readonly ItemsControl _itemsControl;
    private readonly ScrollViewer _viewport;
    private readonly List<ActiveTransform> _activeTransforms = [];
    private FrameworkElement? _fadingElement;
    private Action? _pendingDeleteAction;
    private int _animationGeneration;
    private bool _disposed;

    public ListReflowAnimator(
        ItemsControl itemsControl,
        ScrollViewer viewport)
    {
        _itemsControl = itemsControl;
        _viewport = viewport;
    }

    public bool IsAnimating =>
        _pendingDeleteAction is not null ||
        _activeTransforms.Count != 0;

    public void DeleteWithReflow(object item, Action deleteAction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(deleteAction);

        CompleteImmediately();

        var target = _itemsControl.ItemContainerGenerator.ContainerFromItem(item)
            as FrameworkElement;
        if (!UiMotion.AreAnimationsEnabled ||
            !_viewport.IsVisible ||
            target is null)
        {
            deleteAction();
            return;
        }

        var generation = ++_animationGeneration;
        _fadingElement = target;
        _pendingDeleteAction = deleteAction;

        var fade = new DoubleAnimation(
            fromValue: target.Opacity,
            toValue: 0,
            DeleteFadeDuration)
        {
            EasingFunction = new QuadraticEase
            {
                EasingMode = EasingMode.EaseIn
            },
            FillBehavior = FillBehavior.HoldEnd
        };
        UiMotion.LimitFrameRate(fade);
        fade.Completed += (_, _) => CompleteFadeAndStartReflow(
            generation,
            target);
        target.BeginAnimation(
            UIElement.OpacityProperty,
            fade,
            HandoffBehavior.SnapshotAndReplace);
    }

    public void CompleteImmediately()
    {
        if (_disposed)
        {
            return;
        }

        _animationGeneration++;
        ClearFade();
        ClearReflowAnimations();

        var deleteAction = _pendingDeleteAction;
        _pendingDeleteAction = null;
        deleteAction?.Invoke();
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

    private void CompleteFadeAndStartReflow(
        int generation,
        FrameworkElement fadedElement)
    {
        if (_disposed ||
            generation != _animationGeneration ||
            !ReferenceEquals(_fadingElement, fadedElement))
        {
            return;
        }

        var oldPositions = CaptureVisiblePositions();
        ClearFade();

        var deleteAction = _pendingDeleteAction;
        _pendingDeleteAction = null;
        deleteAction?.Invoke();

        _itemsControl.UpdateLayout();
        _viewport.UpdateLayout();
        StartReflowAnimations(generation, oldPositions);
    }

    private Dictionary<object, double> CaptureVisiblePositions()
    {
        _itemsControl.UpdateLayout();
        _viewport.UpdateLayout();

        var positions = new Dictionary<object, double>(
            ReferenceEqualityComparer.Instance);
        foreach (var item in _itemsControl.Items.Cast<object>())
        {
            if (_itemsControl.ItemContainerGenerator.ContainerFromItem(item)
                    is not FrameworkElement container)
            {
                continue;
            }

            var top = container.TranslatePoint(
                new System.Windows.Point(0, 0),
                _viewport).Y;
            if (!IntersectsExtendedViewport(
                    top,
                    container.ActualHeight))
            {
                continue;
            }

            positions[item] = top;
        }

        return positions;
    }

    private void StartReflowAnimations(
        int generation,
        IReadOnlyDictionary<object, double> oldPositions)
    {
        foreach (var item in _itemsControl.Items.Cast<object>())
        {
            if (!oldPositions.TryGetValue(item, out var oldTop) ||
                _itemsControl.ItemContainerGenerator.ContainerFromItem(item)
                    is not FrameworkElement container)
            {
                continue;
            }

            var newTop = container.TranslatePoint(
                new System.Windows.Point(0, 0),
                _viewport).Y;
            var offset = oldTop - newTop;
            if (Math.Abs(offset) < 0.5 ||
                !IntersectsExtendedViewport(newTop, container.ActualHeight))
            {
                continue;
            }

            var transform = new TranslateTransform(0, offset);
            var activeTransform = new ActiveTransform(container, transform);
            _activeTransforms.Add(activeTransform);
            container.RenderTransform = transform;

            var reflow = new DoubleAnimation(
                fromValue: offset,
                toValue: 0,
                ReflowDuration)
            {
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                },
                FillBehavior = FillBehavior.Stop
            };
            UiMotion.LimitFrameRate(reflow);
            reflow.Completed += (_, _) => CompleteReflowAnimation(
                generation,
                activeTransform);
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                reflow,
                HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void CompleteReflowAnimation(
        int generation,
        ActiveTransform activeTransform)
    {
        if (_disposed || generation != _animationGeneration)
        {
            return;
        }

        ClearTransform(activeTransform);
        _activeTransforms.Remove(activeTransform);
    }

    private bool IntersectsExtendedViewport(
        double top,
        double elementHeight)
    {
        var padding = Math.Max(elementHeight, 24);
        return top + elementHeight >= -padding &&
               top <= _viewport.ViewportHeight + padding;
    }

    private void ClearFade()
    {
        if (_fadingElement is null)
        {
            return;
        }

        _fadingElement.BeginAnimation(
            UIElement.OpacityProperty,
            null);
        _fadingElement = null;
    }

    private void ClearReflowAnimations()
    {
        foreach (var activeTransform in _activeTransforms.ToArray())
        {
            ClearTransform(activeTransform);
        }

        _activeTransforms.Clear();
    }

    private static void ClearTransform(ActiveTransform activeTransform)
    {
        activeTransform.Transform.BeginAnimation(
            TranslateTransform.YProperty,
            null);
        activeTransform.Transform.Y = 0;

        if (ReferenceEquals(
                activeTransform.Element.RenderTransform,
                activeTransform.Transform))
        {
            activeTransform.Element.ClearValue(
                UIElement.RenderTransformProperty);
        }
    }

    private sealed record ActiveTransform(
        FrameworkElement Element,
        TranslateTransform Transform);
}
