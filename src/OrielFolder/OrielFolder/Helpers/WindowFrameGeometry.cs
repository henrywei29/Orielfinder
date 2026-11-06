using System;

namespace OrielFolder.Helpers;

/// <summary>
/// Pure math for <see cref="CustomWindowFrame"/>: our own title-bar frame instead of WinUI's extended title-bar mode,
/// which caps window dragging at ~120 moves/s (Trellis task 11-06-window-drag-stutter). No WinRT/Win32 calls here,
/// so it is unit-tested directly.
/// </summary>
public static class WindowFrameGeometry
{
    /// <summary>
    /// New client-area top for WM_NCCALCSIZE after the default handler ran: the caption is removed, side and bottom
    /// borders stay. A maximized window hangs its frame off-screen, so its client top is inset by the border width
    /// (same as the side border the default handler produced). Returns null when the window has no caption style
    /// (e.g. full screen) and the default result must be kept.
    /// </summary>
    public static int? AdjustedClientTop(int originalTop, int originalLeft, int defaultLeft, bool isMaximized, bool hasCaptionStyle)
    {
        if (!hasCaptionStyle) return null;
        if (!isMaximized) return originalTop;
        return originalTop + Math.Max(0, defaultLeft - originalLeft);
    }

    /// <summary>True when (px, py) lies in the rect; left/top edges inclusive, right/bottom exclusive.</summary>
    public static bool Contains(int x, int y, int width, int height, int px, int py) =>
        width > 0 && height > 0 && px >= x && px < x + width && py >= y && py < y + height;

    /// <summary>Signed screen point from a WM_NCHITTEST lParam (multi-monitor coordinates can be negative).</summary>
    public static (int X, int Y) PointFromLParam(IntPtr lParam)
    {
        long v = lParam.ToInt64();
        return (unchecked((short)(v & 0xFFFF)), unchecked((short)((v >> 16) & 0xFFFF)));
    }
}
