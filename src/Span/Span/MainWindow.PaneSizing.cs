using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Span.Models;

namespace Span
{
    public sealed partial class MainWindow
    {
        private TabItem? _paneDividerDragTab;
        private uint? _paneDividerPointerId;
        private double _paneDividerStartPointerX;
        private double _paneDividerStartLeftWidth;
        private double _paneDividerStartTotalWidth;

        private bool IsClassicSplitVisible => ViewModel != null && ViewModel.IsSplitViewEnabled
            && ViewModel.CurrentViewMode != ViewMode.Settings
            && ViewModel.CurrentViewMode != ViewMode.ActionLog
            && ViewModel.CurrentViewMode != ViewMode.RecycleBin;

        private void ApplyClassicPaneSizing()
        {
            if (LeftPaneCol == null || RightPaneCol == null || SplitterCol == null) return;
            if (!IsClassicSplitVisible || ViewModel.ActiveTab == null)
            {
                SplitterCol.Width = new GridLength(0);
                LeftPaneCol.Width = new GridLength(1, GridUnitType.Star);
                RightPaneCol.Width = new GridLength(0);
                return;
            }

            SplitterCol.Width = new GridLength(1);
            double ratio = PaneLayoutGeometry.ClampSplitRatio(
                Math.Max(0, ClassicPaneHost.ActualWidth - 1), ViewModel.ActiveTab.SplitRatio);
            LeftPaneCol.Width = new GridLength(ratio, GridUnitType.Star);
            RightPaneCol.Width = new GridLength(1 - ratio, GridUnitType.Star);
        }

        private void OnClassicPaneHostSizeChanged(object sender, SizeChangedEventArgs e)
            => ApplyClassicPaneSizing();

        private void OnPaneDividerLoaded(object sender, RoutedEventArgs e)
            => Helpers.CursorHelper.SetCursor(PaneDivider, Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);

        private void OnPaneDividerDragStarted(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(ClassicPaneHost);
            if (!point.Properties.IsLeftButtonPressed || !IsClassicSplitVisible
                || ViewModel.ActiveTab == null || _paneDividerPointerId != null
                || !PaneDivider.CapturePointer(e.Pointer)) return;

            _paneDividerDragTab = ViewModel.ActiveTab;
            _paneDividerPointerId = e.Pointer.PointerId;
            _paneDividerStartPointerX = point.Position.X;
            _paneDividerStartLeftWidth = LeftPaneCol.ActualWidth;
            _paneDividerStartTotalWidth = LeftPaneCol.ActualWidth + RightPaneCol.ActualWidth;
            e.Handled = true;
        }

        private void OnPaneDividerDrag(object sender, PointerRoutedEventArgs e)
        {
            if (_paneDividerPointerId != e.Pointer.PointerId || _paneDividerDragTab == null
                || _paneDividerDragTab != ViewModel.ActiveTab
                || !IsClassicSplitVisible || _paneDividerStartTotalWidth <= 0) return;

            double delta = e.GetCurrentPoint(ClassicPaneHost).Position.X - _paneDividerStartPointerX;
            _paneDividerDragTab.SplitRatio = PaneLayoutGeometry.ClampSplitRatio(
                _paneDividerStartTotalWidth,
                (_paneDividerStartLeftWidth + delta) / _paneDividerStartTotalWidth);
            ApplyClassicPaneSizing();
            e.Handled = true;
        }

        private void OnPaneDividerDragCompleted(object sender, PointerRoutedEventArgs e)
        {
            if (_paneDividerPointerId != e.Pointer.PointerId) return;
            OnPaneDividerDrag(sender, e);
            PaneDivider.ReleasePointerCapture(e.Pointer);
            OnPaneDividerCaptureLost(sender, e);
        }

        private void OnPaneDividerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_paneDividerPointerId != e.Pointer.PointerId) return;
            _paneDividerPointerId = null;
            _paneDividerDragTab = null;
            _paneDividerStartTotalWidth = 0;
            e.Handled = true;
        }
    }
}
