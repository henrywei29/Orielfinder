using System;
using System.Collections.Generic;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OrielFolder.Views.Controls;

/// <summary>Colors for <see cref="CaptionButtons"/> (same fields the system caption buttons used via AppWindowTitleBar).</summary>
public readonly record struct CaptionPalette(
    Color Foreground, Color HoverForeground, Color HoverBackground,
    Color PressedForeground, Color PressedBackground, Color InactiveForeground);

/// <summary>
/// Minimize / maximize-restore / close drawn by the app (Trellis task 11-06-window-drag-stutter). The owning window
/// registers <see cref="GetRegionRects"/> as InputNonClientPointerSource regions, so the system performs the actions
/// (and snap layouts via <see cref="Helpers.CustomWindowFrame"/>). Hover/pressed visuals come from the non-client
/// pointer events because XAML does not receive pointer input over non-client regions.
/// </summary>
public sealed partial class CaptionButtons : UserControl
{
    private static readonly Color CloseHoverBackground = Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C);
    private static readonly Color ClosePressedBackground = Color.FromArgb(0xE6, 0xC4, 0x2B, 0x1C);
    private static readonly Color White = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color TransparentColor = Color.FromArgb(0, 0, 0, 0);

    private CaptionPalette _palette = new(
        Color.FromArgb(0xFF, 0, 0, 0), Color.FromArgb(0xFF, 0, 0, 0), Color.FromArgb(0x0F, 0, 0, 0),
        Color.FromArgb(0x9E, 0, 0, 0), Color.FromArgb(0x0A, 0, 0, 0), Color.FromArgb(0x5C, 0, 0, 0));
    private InputNonClientPointerSource? _source;
    private NonClientRegionKind? _hover, _pressed;
    private bool _active = true;

    public CaptionButtons()
    {
        InitializeComponent();
        Render();
    }

    /// <summary>Quick Look shows close only.</summary>
    public bool ShowMinimize
    {
        get => MinimizeButton.Visibility == Visibility.Visible;
        set => MinimizeButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool ShowMaximize
    {
        get => MaximizeButton.Visibility == Visibility.Visible;
        set => MaximizeButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Maximize or restore glyph.</summary>
    public void SetMaximized(bool maximized) => MaximizeGlyph.Glyph = maximized ? "" : "";

    public void SetPalette(CaptionPalette palette)
    {
        _palette = palette;
        Render();
    }

    /// <summary>Inactive windows dim the glyphs (system behaviour).</summary>
    public void SetActive(bool active)
    {
        _active = active;
        Render();
    }

    /// <summary>Subscribe to the window's non-client pointer events for hover/pressed visuals.</summary>
    public void Attach(InputNonClientPointerSource source)
    {
        Detach();
        _source = source;
        source.PointerEntered += OnEntered;
        source.PointerExited += OnExited;
        source.PointerPressed += OnPressed;
        source.PointerReleased += OnReleased;
    }

    public void Detach()
    {
        if (_source == null) return;
        _source.PointerEntered -= OnEntered;
        _source.PointerExited -= OnExited;
        _source.PointerPressed -= OnPressed;
        _source.PointerReleased -= OnReleased;
        _source = null;
        _hover = _pressed = null;
    }

    /// <summary>Visible buttons as (region kind, client rect in physical pixels).</summary>
    public IReadOnlyList<(NonClientRegionKind Kind, Windows.Graphics.RectInt32 Rect)> GetRegionRects(double scale)
    {
        var list = new List<(NonClientRegionKind, Windows.Graphics.RectInt32)>(3);
        void Add(Border b, NonClientRegionKind kind)
        {
            if (b.Visibility != Visibility.Visible || b.ActualWidth <= 0 || b.ActualHeight <= 0) return;
            var r = b.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, b.ActualWidth, b.ActualHeight));
            list.Add((kind, new Windows.Graphics.RectInt32(
                (int)Math.Round(r.X * scale), (int)Math.Round(r.Y * scale),
                (int)Math.Round(r.Width * scale), (int)Math.Round(r.Height * scale))));
        }
        Add(MinimizeButton, NonClientRegionKind.Minimize);
        Add(MaximizeButton, NonClientRegionKind.Maximize);
        Add(CloseButton, NonClientRegionKind.Close);
        return list;
    }

    private void OnEntered(InputNonClientPointerSource s, NonClientPointerEventArgs e) => Update(() => _hover = e.RegionKind);
    private void OnExited(InputNonClientPointerSource s, NonClientPointerEventArgs e) => Update(() => { if (_hover == e.RegionKind) _hover = null; _pressed = null; });
    private void OnPressed(InputNonClientPointerSource s, NonClientPointerEventArgs e) => Update(() => _pressed = e.RegionKind);
    private void OnReleased(InputNonClientPointerSource s, NonClientPointerEventArgs e) => Update(() => _pressed = null);

    private void Update(Action change)
    {
        if (DispatcherQueue == null) return;
        if (DispatcherQueue.HasThreadAccess) { change(); Render(); }
        else DispatcherQueue.TryEnqueue(() => { change(); Render(); });
    }

    private void Render()
    {
        Paint(MinimizeButton, MinimizeGlyph, NonClientRegionKind.Minimize, isClose: false);
        Paint(MaximizeButton, MaximizeGlyph, NonClientRegionKind.Maximize, isClose: false);
        Paint(CloseButton, CloseGlyph, NonClientRegionKind.Close, isClose: true);
    }

    private void Paint(Border button, FontIcon glyph, NonClientRegionKind kind, bool isClose)
    {
        bool pressed = _pressed == kind, hover = _hover == kind;
        Color bg = TransparentColor, fg = _active ? _palette.Foreground : _palette.InactiveForeground;
        if (pressed) { bg = isClose ? ClosePressedBackground : _palette.PressedBackground; fg = isClose ? White : _palette.PressedForeground; }
        else if (hover) { bg = isClose ? CloseHoverBackground : _palette.HoverBackground; fg = isClose ? White : _palette.HoverForeground; }
        button.Background = new SolidColorBrush(bg);
        glyph.Foreground = new SolidColorBrush(fg);
    }
}
