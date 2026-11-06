using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OrielFolder.Helpers;

namespace OrielFolder
{
    public sealed partial class MainWindow
    {
        private IntPtr _outerColumnResizeHwnd;
        private NativeMethods.RECT _outerColumnStartRect;
        private int _outerColumnStartPointerX;
        private int _outerColumnWorkAreaRight;
        private double _outerColumnDpiScale;
        private ScrollViewer? _outerColumnViewport;
        private double _outerColumnStartViewportWidth;
        private Microsoft.UI.Xaml.Shapes.Rectangle? _outerColumnGrip;
        private double _outerColumnGripRightInset;

        private void BeginOuterColumnResize(Microsoft.UI.Xaml.Shapes.Rectangle grip)
        {
            _outerColumnResizeHwnd = IntPtr.Zero;
            _outerColumnViewport = null;
            _outerColumnGrip = null;
            if (_hwnd == IntPtr.Zero || IsZoomed(_hwnd) || IsIconic(_hwnd) || RootGrid.ActualWidth <= 0)
                return;

            // Use the app's client boundary, irrespective of the number or arrangement of panes.
            var right = grip.TransformToVisual(RootGrid)
                .TransformPoint(new Windows.Foundation.Point(grip.ActualWidth, 0)).X;
            if (Math.Abs(RootGrid.ActualWidth - right) > 12)
                return;

            // The grip is outside its column's item list, so its first ScrollViewer
            // ancestor is the owning horizontal Miller viewport, in any pane layout.
            ScrollViewer? viewport = null;
            for (var ancestor = VisualTreeHelper.GetParent(grip); ancestor != null;
                ancestor = VisualTreeHelper.GetParent(ancestor))
            {
                if (ancestor is ScrollViewer scroller)
                {
                    viewport = scroller;
                    break;
                }
            }
            if (viewport == null || viewport.ViewportWidth <= 0)
                return;

            if (!NativeMethods.GetWindowRect(_hwnd, out var rect) ||
                !NativeMethods.GetCursorPos(out var pointer))
                return;

            var monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFO
            {
                cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
            };
            uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
            if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info) || dpi == 0)
                return;

            _outerColumnStartRect = rect;
            _outerColumnStartPointerX = pointer.X;
            _outerColumnWorkAreaRight = info.rcWork.Right;
            _outerColumnDpiScale = dpi / 96.0;
            _outerColumnViewport = viewport;
            _outerColumnStartViewportWidth = viewport.ViewportWidth;
            _outerColumnGrip = grip;
            _outerColumnGripRightInset = RootGrid.ActualWidth - right;
            _outerColumnResizeHwnd = _hwnd;
        }

        private double ResizeWindowForOuterColumn()
        {
            double currentWidth = _resizingColumnGrid?.Width ?? _resizeStartWidth;
            var viewport = _outerColumnViewport;
            if (viewport == null || _outerColumnResizeHwnd != _hwnd || IsZoomed(_hwnd) || IsIconic(_hwnd) ||
                !NativeMethods.GetCursorPos(out var pointer))
                return currentWidth;

            // Screen coordinates remain stable while resizing changes the root's local geometry.
            int physicalDelta = pointer.X - _outerColumnStartPointerX;
            int startWidth = _outerColumnStartRect.Right - _outerColumnStartRect.Left;
            int width = Models.PaneLayoutGeometry.WindowWidthForColumnDrag(startWidth, physicalDelta,
                _outerColumnStartRect.Left, _outerColumnWorkAreaRight, (int)Math.Ceiling(480 * _outerColumnDpiScale));

            const uint noMove = 0x0002;
            // SetWindowPos takes physical pixels; AppWindow resizing applies DPI a second time here.
            if (!NativeMethods.SetWindowPos(_outerColumnResizeHwnd, IntPtr.Zero,
                _outerColumnStartRect.Left, _outerColumnStartRect.Top, width,
                _outerColumnStartRect.Bottom - _outerColumnStartRect.Top,
                noMove | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER))
                return currentWidth;

            // A proportional pane receives only part of a window's growth. Measure
            // its actual viewport allocation before changing the column to avoid clipping.
            RootGrid.UpdateLayout();
            return Math.Clamp(_resizeStartWidth + viewport.ViewportWidth - _outerColumnStartViewportWidth, 150, 600);
        }

        private void CompleteOuterColumnLayout()
        {
            var grip = _outerColumnGrip;
            var viewport = _outerColumnViewport;
            if (!_isResizingColumn || grip == null || viewport == null || _outerColumnResizeHwnd != _hwnd)
                return;

            // Window layout can clamp the old offset before the new column extent exists.
            // Wait until every column (including Ctrl+drag predecessors) has its new width.
            RootGrid.UpdateLayout();
            if (!_isResizingColumn || _resizingColumnGrid == null ||
                _outerColumnResizeHwnd != _hwnd || _outerColumnGrip != grip || _outerColumnViewport != viewport)
                return;

            double right = grip.TransformToVisual(RootGrid)
                .TransformPoint(new Windows.Foundation.Point(grip.ActualWidth, 0)).X;
            double desiredRight = RootGrid.ActualWidth - _outerColumnGripRightInset;
            double delta = right - desiredRight;
            if (Math.Abs(delta) > 0.1)
                viewport.ChangeView(viewport.HorizontalOffset + delta, null, null, true);
        }

        private void OnColumnResizeGripPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Shapes.Rectangle grip)
                CancelColumnResize(grip);
        }

        private void CancelColumnResize(Microsoft.UI.Xaml.Shapes.Rectangle grip)
        {
            if (_isResizingColumn && _millerColumnWidths != null)
                _settings.MillerColumnWidthsJson = System.Text.Json.JsonSerializer.Serialize(_millerColumnWidths);
            grip.PointerCaptureLost -= OnColumnResizeGripPointerCaptureLost;
            _outerColumnResizeHwnd = IntPtr.Zero;
            _outerColumnViewport = null;
            _outerColumnGrip = null;
            _isResizingColumn = false;
            _resizingColumnGrid = null;
            grip.Fill = _transparentBrush;
            SetGripCursor(grip, false);
        }
    }
}
