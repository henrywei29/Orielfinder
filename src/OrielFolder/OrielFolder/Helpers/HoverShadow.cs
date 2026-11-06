using System;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace OrielFolder.Helpers;

/// <summary>
/// Soft rounded drop shadow that fades in while the pointer is over a card (Home page cards).
/// Usage: put an empty element BEHIND the card in a wrapper Grid, same size as the card, and set
/// <c>helpers:HoverShadow.CornerRadius="10"</c> on it. The pointer is tracked on the wrapper (the element's parent).
/// The shadow is a Composition DropShadow masked by a rounded rectangle, offset 0, so it is equally wide on all four
/// sides; the hovered item container is raised (Canvas.ZIndex) so neighbouring cards do not cover it.
/// Clearing does not rely on PointerExited alone (a fast exit can report the last point inside the card, which left
/// the shadow stuck): only one card is hovered at a time, and a PointerMoved watcher on the window content clears
/// the hovered card as soon as the pointer is outside it.
/// </summary>
public static class HoverShadow
{
    private const float BlurRadius = 18f;
    private const float LightOpacity = 0.28f, DarkOpacity = 0.75f;
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(150);

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(double), typeof(HoverShadow), new PropertyMetadata(-1.0, OnCornerRadiusChanged));

    public static double GetCornerRadius(DependencyObject element) => (double)element.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject element, double value) => element.SetValue(CornerRadiusProperty, value);

    private static State? _current;
    private static UIElement? _watchRoot;
    private static readonly PointerEventHandler WatchMoved = OnWatchPointerMoved;
    private static readonly PointerEventHandler WatchExited = OnWatchPointerExited;

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "HoverShadowState", typeof(object), typeof(HoverShadow), new PropertyMetadata(null));

    private sealed class State
    {
        public required FrameworkElement Host;
        public required UIElement Trigger;
        public required SpriteVisual Sprite;
        public required DropShadow Shadow;
        public required ShapeVisual Shape;
        public required CompositionRoundedRectangleGeometry Geometry;
        public required CompositionVisualSurface Surface;
        public bool Hover, Pressed;
    }

    private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host || (double)e.NewValue < 0) return;
        if (host.GetValue(StateProperty) is State existing)
        {
            existing.Geometry.CornerRadius = new Vector2((float)(double)e.NewValue);
            return;
        }
        host.Loaded -= OnHostLoaded;
        host.Loaded += OnHostLoaded;
        host.Unloaded -= OnHostUnloaded;
        host.Unloaded += OnHostUnloaded;
        if (host.IsLoaded) Setup(host);
    }

    private static void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement host) Setup(host);
    }

    private static void OnHostUnloaded(object sender, RoutedEventArgs e)
    {
        // Navigating away from Home never raises PointerExited: do not keep a stale shadow for the next visit.
        if (sender is FrameworkElement host && host.GetValue(StateProperty) is State state)
        {
            ClearHover(state);
            state.Sprite.StopAnimation("Opacity");
            state.Sprite.Opacity = 0f;
        }
    }

    private static void Setup(FrameworkElement host)
    {
        if (host.GetValue(StateProperty) is State) { return; }
        if (VisualTreeHelper.GetParent(host) is not UIElement trigger) return;
        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
            var geometry = compositor.CreateRoundedRectangleGeometry();
            float radius = (float)GetCornerRadius(host);
            geometry.CornerRadius = new Vector2(radius, radius);
            var shape = compositor.CreateShapeVisual();
            var sprite = compositor.CreateSpriteShape(geometry);
            sprite.FillBrush = compositor.CreateColorBrush(Colors.Black);
            shape.Shapes.Add(sprite);
            var surface = compositor.CreateVisualSurface();
            surface.SourceVisual = shape;

            var shadow = compositor.CreateDropShadow();
            shadow.BlurRadius = BlurRadius;
            shadow.Offset = Vector3.Zero;
            shadow.Color = Colors.Black;
            shadow.Mask = compositor.CreateSurfaceBrush(surface);

            // No Brush: only the shadow is drawn (the card itself covers this area anyway).
            var visual = compositor.CreateSpriteVisual();
            visual.Shadow = shadow;
            visual.Opacity = 0f;
            ElementCompositionPreview.SetElementChildVisual(host, visual);

            var state = new State
            {
                Host = host, Trigger = trigger, Sprite = visual, Shadow = shadow,
                Shape = shape, Geometry = geometry, Surface = surface,
            };
            host.SetValue(StateProperty, state);
            UpdateSize(state);
            UpdateTheme(state);

            host.SizeChanged += (_, _) => UpdateSize(state);
            host.ActualThemeChanged += (_, _) => UpdateTheme(state);
            trigger.PointerEntered += (_, _) => SetHover(state);
            trigger.PointerExited += (_, args) =>
            {
                // Exits bubble up from the card's children; the watcher catches an exit misjudged here.
                if (!IsInside(state, args)) ClearHover(state);
            };
            trigger.PointerCanceled += (_, _) => ClearHover(state);
            trigger.PointerCaptureLost += (_, args) =>
            {
                state.Pressed = false;
                if (IsInside(state, args)) Animate(state); else ClearHover(state);
            };
            trigger.AddHandler(UIElement.PointerPressedEvent,
                new PointerEventHandler((_, _) => { state.Pressed = true; Animate(state); }), true);
            trigger.AddHandler(UIElement.PointerReleasedEvent,
                new PointerEventHandler((_, _) => { state.Pressed = false; Animate(state); }), true);
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[HoverShadow] setup failed: {ex.Message}");
        }
    }

    private static void SetHover(State state)
    {
        if (_current != null && !ReferenceEquals(_current, state)) ClearHover(_current);
        _current = state;
        EnsureWatcher(state);
        if (state.Hover) return;
        state.Hover = true;
        Animate(state);
    }

    private static void ClearHover(State state)
    {
        if (ReferenceEquals(_current, state)) _current = null;
        if (!state.Hover && !state.Pressed) return;
        state.Hover = state.Pressed = false;
        Animate(state);
    }

    /// <summary>Window-level safety net: handledEventsToo, so moves over any element (and leaving the window) count.</summary>
    private static void EnsureWatcher(State state)
    {
        if (state.Host.XamlRoot?.Content is not UIElement root || ReferenceEquals(root, _watchRoot)) return;
        if (_watchRoot != null)
        {
            _watchRoot.RemoveHandler(UIElement.PointerMovedEvent, WatchMoved);
            _watchRoot.RemoveHandler(UIElement.PointerExitedEvent, WatchExited);
        }
        _watchRoot = root;
        root.AddHandler(UIElement.PointerMovedEvent, WatchMoved, true);
        root.AddHandler(UIElement.PointerExitedEvent, WatchExited, true);
    }

    private static void OnWatchPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_current is { } state && !IsInside(state, args)) ClearHover(state);
    }

    private static void OnWatchPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (_current is { } state && (ReferenceEquals(sender, args.OriginalSource) || !IsInside(state, args))) ClearHover(state);
    }

    private static bool IsInside(State state, PointerRoutedEventArgs args)
    {
        if (state.Host.XamlRoot == null || state.Host.ActualWidth <= 0 || state.Host.ActualHeight <= 0) return false;
        var p = args.GetCurrentPoint(state.Host).Position;
        return p.X >= 0 && p.Y >= 0 && p.X < state.Host.ActualWidth && p.Y < state.Host.ActualHeight;
    }

    private static void UpdateSize(State state)
    {
        var size = new Vector2((float)state.Host.ActualWidth, (float)state.Host.ActualHeight);
        state.Geometry.Size = size;
        state.Shape.Size = size;
        state.Surface.SourceSize = size;
        state.Sprite.Size = size;
    }

    private static void UpdateTheme(State state) =>
        state.Shadow.Opacity = state.Host.ActualTheme == ElementTheme.Dark ? DarkOpacity : LightOpacity;

    private static void Animate(State state)
    {
        float target = state.Hover ? (state.Pressed ? 0.55f : 1f) : 0f;
        SetRaised(state, state.Hover);
        var animation = state.Sprite.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1f, target);
        animation.Duration = FadeDuration;
        state.Sprite.StartAnimation("Opacity", animation);
    }

    /// <summary>Raise the hovered item container so the following cards do not paint over its shadow.</summary>
    private static void SetRaised(State state, bool raised)
    {
        DependencyObject? current = state.Trigger;
        while (current != null && current is not SelectorItem) current = VisualTreeHelper.GetParent(current);
        if (current is UIElement container) Canvas.SetZIndex(container, raised ? 1 : 0);
    }
}
