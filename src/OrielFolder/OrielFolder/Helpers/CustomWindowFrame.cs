using System;
using System.Runtime.InteropServices;

namespace OrielFolder.Helpers;

/// <summary>
/// Our own title-bar frame for a WinUI <c>Window</c> that does NOT use <c>ExtendsContentIntoTitleBar</c>.
/// WinUI's extended title-bar mode caps window dragging at ~120 moves/s (measured, Trellis task
/// 11-06-window-drag-stutter); removing the caption ourselves keeps dragging at normal-window speed.
/// <list type="bullet">
/// <item>WM_NCCALCSIZE: drop the caption, keep the resize borders (maximized: inset by the border).</item>
/// <item>WM_NCHITTEST: answer HTMAXBUTTON over our own maximize button so Windows 11 shows snap layouts.</item>
/// </list>
/// Every other non-client region (caption drag, tabs passthrough, min/max/close clicks) is declared by the window with
/// <c>InputNonClientPointerSource.SetRegionRects</c>. Do not combine with <c>Window.SetTitleBar</c>.
/// </summary>
public sealed class CustomWindowFrame : IDisposable
{
    private const uint WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084, WM_NCDESTROY = 0x0082;
    private const int HTMAXBUTTON = 9;
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;
    private static readonly IntPtr SubclassId = new(0x4F46); // "OF"

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, IntPtr id, IntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, IntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private readonly IntPtr _hwnd;
    private readonly SubclassProc _proc; // keep alive: native code holds the pointer
    private bool _attached;

    /// <summary>Maximize button in client physical pixels; empty = no HTMAXBUTTON answer (e.g. Quick Look).</summary>
    public Windows.Graphics.RectInt32 MaximizeButtonRect { get; set; }

    public CustomWindowFrame(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = Proc;
        _attached = SetWindowSubclass(hwnd, _proc, SubclassId, IntPtr.Zero);
        if (!_attached)
        {
            DebugLogger.Log("[CustomWindowFrame] SetWindowSubclass failed; system caption stays");
            return;
        }
        // Make Windows ask WM_NCCALCSIZE again with our handler in place.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    private IntPtr Proc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data)
    {
        try
        {
            if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
            {
                // lParam -> NCCALCSIZE_PARAMS; rgrc[0] (left, top, right, bottom) is the first member.
                int left = Marshal.ReadInt32(lParam, 0), top = Marshal.ReadInt32(lParam, 4);
                var result = DefSubclassProc(hWnd, msg, wParam, lParam);
                bool hasCaption = (GetWindowLong(hWnd, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
                int? newTop = WindowFrameGeometry.AdjustedClientTop(top, left, Marshal.ReadInt32(lParam, 0), IsZoomed(hWnd), hasCaption);
                if (newTop.HasValue) Marshal.WriteInt32(lParam, 4, newTop.Value);
                return result;
            }
            if (msg == WM_NCHITTEST)
            {
                var max = MaximizeButtonRect;
                if (max.Width > 0 && max.Height > 0)
                {
                    var (sx, sy) = WindowFrameGeometry.PointFromLParam(lParam);
                    var pt = new POINT { X = sx, Y = sy };
                    if (ScreenToClient(hWnd, ref pt) && WindowFrameGeometry.Contains(max.X, max.Y, max.Width, max.Height, pt.X, pt.Y))
                        return new IntPtr(HTMAXBUTTON);
                }
            }
            if (msg == WM_NCDESTROY) Detach();
        }
        catch
        {
            // Never let a managed exception cross the native window procedure.
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void Detach()
    {
        if (!_attached) return;
        _attached = false;
        RemoveWindowSubclass(_hwnd, _proc, SubclassId);
    }

    public void Dispose() => Detach();
}
