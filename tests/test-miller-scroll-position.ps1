$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.xaml.cs'))
$methods = @()
foreach ($name in @('OnColumnsChanged', 'OnRightColumnsChanged', 'AdjustSpacerAndScrollAfterReplace', 'SetMillerSpacerWidth', 'SetAutoFitColumnWidth')) {
    $match = [regex]::Match($source, '(?sm)^        private void ' + $name + '\(.*?(?=^        (?:private |///|// ={5}|#))')
    if (-not $match.Success) { throw "Cannot locate production method $name." }
    $methods += $match.Value.TrimEnd()
}

# Compile real event handlers; replace only WinUI layout and dispatcher collaborators.
$hostSource = @'
#nullable enable annotations
#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
namespace Microsoft.UI.Dispatching {
    public enum DispatcherQueuePriority { Low }
    public class DispatcherQueue {
        public bool Defer;
        public Queue<Action> Pending = new();
        public bool TryEnqueue(DispatcherQueuePriority priority, Action action) {
            if (Defer) Pending.Enqueue(action); else action();
            return true;
        }
        public void Flush() { while (Pending.Count > 0) Pending.Dequeue()(); }
    }
}
namespace OrielFolder.Helpers { public static class DebugLogger { public static void Log(string text) {} } }
namespace OrielFolder.ViewModels {
    public class ExplorerViewModel {
        public List<object> Columns = new() { new(), new(), new() };
        public bool IsReplacingLastColumn;
    }
}
namespace OrielFolder {
    public enum ViewMode { MillerColumns }
    public enum ListViewSelectionMode { Extended, Multiple }
    public class Border { public double Width; }
    public class DependencyObject { public DependencyObject? Parent; }
    public class FrameworkElement : DependencyObject { public double ActualWidth; }
    public class Grid : FrameworkElement {
        public Action<double>? WidthChanged;
        public double Width { set { WidthChanged?.Invoke(value); ActualWidth = value; } }
    }
    public static class VisualTreeHelper {
        public static DependencyObject? GetParent(DependencyObject child) => child.Parent;
    }
    public class ItemsControl {
        public object ItemsSource = new();
        public List<object> Items = new() { new(), new(), new() };
        public double[]? Widths;
        public object ContainerFromIndex(int index) => new FrameworkElement { ActualWidth = Widths?[index] ?? 220 };
        public void UpdateLayout() {}
    }
    public class ScrollViewer : DependencyObject {
        public double HorizontalOffset;
        public double ViewportWidth = 300;
        public int ViewChanges;
        public bool DisableAnimation;
        public bool SimulateLayoutClamping;
        public double ContentWidth;
        public Border? Spacer;
        public double MeasuredMaximumOffset;
        public void UpdateLayout() {
            if (SimulateLayoutClamping)
                MeasuredMaximumOffset = Math.Max(0, ContentWidth + (Spacer?.Width ?? 0) - ViewportWidth);
        }
        public bool ChangeView(double? horizontal, double? vertical, float? zoom, bool disableAnimation) {
            if (horizontal.HasValue) HorizontalOffset = SimulateLayoutClamping
                ? Math.Min(horizontal.Value, MeasuredMaximumOffset) : horizontal.Value;
            ViewChanges++; DisableAnimation = disableAnimation; return true;
        }
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue = new();
    }
    public class MainViewModel {
        public ViewMode CurrentViewMode;
        public ViewMode RightViewMode;
        public bool IsSwitchingTab;
        public bool IsFreeLayoutActive;
        public bool IsSplitViewEnabled = true;
        public ViewModels.ExplorerViewModel LeftExplorer = new();
        public ViewModels.ExplorerViewModel RightExplorer = new();
    }
    public class SettingsService { public int MillerFolderOpenAlignment = 2; }
    public class MainWindow {
        public SettingsService _settings = new();
        private bool _isClosed = false;
        public MainViewModel ViewModel = new();
        public ItemsControl MillerColumnsControl = new();
        public ItemsControl MillerColumnsControlRight = new();
        public ScrollViewer MillerScrollViewer = new();
        public ScrollViewer LeftScroller => MillerScrollViewer;
        public ScrollViewer MillerScrollViewerRight = new();
        public bool ActiveRight;
        public Border MillerColumnSpacerLeft = new();
        public Border MillerColumnSpacerRight = new();
        private ListViewSelectionMode _millerSelectionMode = ListViewSelectionMode.Multiple;
        private Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue = new();
        public int ScrollRequests;
        public int CheckboxUpdates;
        public int WatcherUpdates;
        public double ColumnWidth = 220;
        private void UpdateFileSystemWatcherPaths() { WatcherUpdates++; }
        private ScrollViewer GetActiveMillerScrollViewer() => ActiveRight ? MillerScrollViewerRight : LeftScroller;
        private ScrollViewer GetLeftMillerScrollViewer() => LeftScroller;
        private ItemsControl GetActiveMillerColumnsControl() => ActiveRight ? MillerColumnsControlRight : MillerColumnsControl;
        private ItemsControl? GetMillerItems(ScrollViewer scroller) => scroller == MillerScrollViewerRight ? MillerColumnsControlRight : MillerColumnsControl;
        private Border? GetMillerSpacer(ScrollViewer? scrollViewer) => scrollViewer == MillerScrollViewerRight ? MillerColumnSpacerRight : MillerColumnSpacerLeft;
        private void ScrollToLastColumn(ViewModels.ExplorerViewModel explorer, ScrollViewer? scroller, bool disableAnimation = false) { ScrollRequests++; }
        private void ScrollToLastColumnSync(ViewModels.ExplorerViewModel explorer, ScrollViewer? scroller, bool disableAnimation = false) { ScrollRequests++; }
        private void ApplyCheckboxToItemsControl(ItemsControl control, ListViewSelectionMode mode) { CheckboxUpdates++; }
        private void PrepareAndAnimateNewColumn(ItemsControl control) {}
        private double GetTotalColumnsActualWidth(ItemsControl? control, int count) {
            if (control?.Widths is not { } widths) return ColumnWidth * count;
            double total = 0;
            for (int i = 0; i < count; i++) total += widths[i];
            return total;
        }
        public void AddColumn(bool right) {
            var change = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new object());
            if (right) OnRightColumnsChanged(null, change); else OnColumnsChanged(null, change);
        }
        public void FinishReplacement(bool right) => AdjustSpacerAndScrollAfterReplace(
            right ? ViewModel.RightExplorer : ViewModel.LeftExplorer,
            right ? MillerScrollViewerRight : LeftScroller,
            right ? MillerColumnSpacerRight : MillerColumnSpacerLeft,
            right ? MillerColumnsControlRight : MillerColumnsControl);
        public void PrepareRemoval(bool right, int count) => SetMillerSpacerWidth(
            right ? MillerColumnSpacerRight : MillerColumnSpacerLeft,
            right ? MillerColumnsControlRight : MillerColumnsControl, count);
        public void FitColumn(Grid grid, double width) => SetAutoFitColumnWidth(grid, width);
'@
Add-Type -TypeDefinition ($hostSource + "`n" + ($methods -join "`n") + "`n    }`n}")
if ($source -notmatch 'SetAutoFitColumnWidth\(widthGrid, fitted\);') { throw 'Async AutoFit does not use offset-preserving width changes.' }

foreach ($right in @($false, $true)) {
    foreach ($mode in @(0, 1, 2)) {
        foreach ($replace in @($false, $true)) {
            $window = [OrielFolder.MainWindow]::new()
            $window._settings.MillerFolderOpenAlignment = $mode
            $control = if ($right) { $window.MillerColumnsControlRight } else { $window.MillerColumnsControl }
            $scroller = if ($right) { $window.MillerScrollViewerRight } else { $window.LeftScroller }
            $spacer = if ($right) { $window.MillerColumnSpacerRight } else { $window.MillerColumnSpacerLeft }
            $control.Widths = [double[]]@(315, 480, 120)
            $scroller.HorizontalOffset = 125
            $scroller.ViewportWidth = 1000
            if ($replace) { $window.FinishReplacement($right) } else { $window.AddColumn($right) }
            $expected = switch ($mode) { 0 { 125 } 1 { 795 } 2 { 315 } }
            $clamped = [Math]::Min($expected, [Math]::Max(0, 915 + $spacer.Width - 1000))
            if ($scroller.HorizontalOffset -ne $expected -or $clamped -ne $expected) {
                throw "Folder-open mode $mode did not produce offset $expected (right=$right, replace=$replace)."
            }
            if ($mode -eq 0 -and $scroller.ViewChanges -ne 0) { throw 'Keep-position mode issued an automatic scroll.' }
            Write-Output "PASS: folder-open mode=$mode right=$right replace=$replace"
        }
    }
}

foreach ($right in @($false, $true)) {
    foreach ($width in @(220, 950)) {
        $window = [OrielFolder.MainWindow]::new()
        $window.ColumnWidth = $width
        $window.AddColumn($right)
        $scroller = if ($right) { $window.MillerScrollViewerRight } else { $window.LeftScroller }
        if ($window.ScrollRequests -ne 0 -or $scroller.HorizontalOffset -ne $width -or -not $scroller.DisableAnimation) { throw "Opening a folder did not align its containing column instantly (right=$right, width=$width)." }
        if ($window.CheckboxUpdates -ne 1) { throw 'New-column checkbox setup was lost.' }
        if (-not $right -and $window.WatcherUpdates -ne 1) { throw 'File watcher setup was lost.' }
        Write-Output "PASS: opened folder's containing column aligns left (right=$right, width=$width)"

        $scroller = if ($right) { $window.MillerScrollViewerRight } else { $window.LeftScroller }
        $scroller.HorizontalOffset = 125
        $window.FinishReplacement($right)
        if ($window.ScrollRequests -ne 0 -or $scroller.HorizontalOffset -ne $width) {
            throw "Replacement did not align the selected folder's containing column (right=$right, width=$width)."
        }
        Write-Output "PASS: replacement aligns containing column (right=$right, width=$width)"
    }
    $window = [OrielFolder.MainWindow]::new()
    $scroller = if ($right) { $window.MillerScrollViewerRight } else { $window.LeftScroller }
    $explorer = if ($right) { $window.ViewModel.RightExplorer } else { $window.ViewModel.LeftExplorer }
    $spacer = if ($right) { $window.MillerColumnSpacerRight } else { $window.MillerColumnSpacerLeft }
    $explorer.Columns.RemoveRange(1, 2)
    $scroller.HorizontalOffset = 125
    $window.FinishReplacement($right)
    if ($window.ScrollRequests -ne 0 -or $spacer.Width -ne 80 -or $scroller.HorizontalOffset -ne 0) { throw 'A root column failed to align with valid trailing space.' }
    Write-Output "PASS: root alignment retains necessary trailing space (right=$right)"

    $window = [OrielFolder.MainWindow]::new()
    $scroller = if ($right) { $window.MillerScrollViewerRight } else { $window.LeftScroller }
    $explorer = if ($right) { $window.ViewModel.RightExplorer } else { $window.ViewModel.LeftExplorer }
    $spacer = if ($right) { $window.MillerColumnSpacerRight } else { $window.MillerColumnSpacerLeft }
    $explorer.Columns.RemoveAt(2)
    $scroller.ViewportWidth = 1000
    $scroller.HorizontalOffset = 100
    $window.FinishReplacement($right)
    $explorer.Columns.Add([object]::new())
    $window.AddColumn($right)
    $clampedOffset = [Math]::Min(220, [Math]::Max(0, 660 + $spacer.Width - 1000))
    if ($clampedOffset -ne 220 -or $scroller.HorizontalOffset -ne 220) { throw "Adding after replacement cannot align the selected column (right=$right)." }
    Write-Output "PASS: replacement then add can align even in a wide viewport (right=$right)"

    $window = [OrielFolder.MainWindow]::new()
    $control = if ($right) { $window.MillerColumnsControlRight } else { $window.MillerColumnsControl }
    $spacer = if ($right) { $window.MillerColumnSpacerRight } else { $window.MillerColumnSpacerLeft }
    $control.Widths = [double[]]@(950, 950, 220)
    $window.PrepareRemoval($right, 2)
    $clampedOffset = [Math]::Min(900, [Math]::Max(0, 950 + $spacer.Width - 1000))
    if ($spacer.Width -lt 1170 -or $clampedOffset -ne 900) { throw "Mixed-width removal clamps the viewport (right=$right)." }
    Write-Output "PASS: mixed-width removal reserves actual disappearing width (right=$right)"
}

$window = [OrielFolder.MainWindow]::new()
$window.LeftScroller.ViewportWidth = 1000
$window.LeftScroller.HorizontalOffset = 100
$window.MillerColumnSpacerLeft.Width = 660
$window.MillerColumnsControlRight.Widths = [double[]]@(950, 950, 220)
$window.LeftScroller.DispatcherQueue.Defer = $true
$window.AddColumn($false)
$window.ActiveRight = $true
$window.LeftScroller.DispatcherQueue.Flush()
$clampedOffset = [Math]::Min(220, [Math]::Max(0, 660 + $window.MillerColumnSpacerLeft.Width - 1000))
if ($clampedOffset -ne 220 -or $window.MillerScrollViewerRight.ViewChanges -ne 0) { throw 'A pane switch before the layout callback aligned the wrong pane.' }
Write-Output 'PASS: deferred layout uses the original pane and column widths'

foreach ($right in @($false, $true)) {
    $window = [OrielFolder.MainWindow]::new()
    $scroller = if ($right) { $window.MillerScrollViewerRight } else { $window.LeftScroller }
    $scroller.SimulateLayoutClamping = $true
    $scroller.ContentWidth = 660
    $scroller.ViewportWidth = 1000
    $scroller.Spacer = if ($right) { $window.MillerColumnSpacerRight } else { $window.MillerColumnSpacerLeft }
    $window.AddColumn($right)
    if ($scroller.HorizontalOffset -ne 220) { throw "Alignment clamped against the old layout extent (right=$right)." }
    Write-Output "PASS: trailing space is measured before changing offset (right=$right)"
}

foreach ($right in @($false, $true)) {
    $window = [OrielFolder.MainWindow]::new()
    $control = if ($right) { $window.MillerColumnsControlRight } else { $window.MillerColumnsControl }
    $scroller = if ($right) { $window.MillerScrollViewerRight } else { $window.LeftScroller }
    $control.Widths = [double[]]@(315, 480, 120)
    $window.FinishReplacement($right)
    if ($scroller.HorizontalOffset -ne 315) { throw 'Mixed-width alignment did not use the measured prefix.' }
    Write-Output "PASS: measured mixed widths select the containing column (right=$right)"
}

foreach ($right in @($false, $true)) {
    $window = [OrielFolder.MainWindow]::new()
    $scroller = if ($right) { $window.MillerScrollViewerRight } else { $window.LeftScroller }
    $spacer = if ($right) { $window.MillerColumnSpacerRight } else { $window.MillerColumnSpacerLeft }
    $scroller.ViewportWidth = 1000
    $scroller.HorizontalOffset = 125 # User scrolled after the original alignment.
    $spacer.Width = 465
    $grid = [OrielFolder.Grid]::new()
    $grid.ActualWidth = 220
    $grid.Parent = $scroller
    $grid.WidthChanged = [Action[double]] {
        param($width)
        $scroller.HorizontalOffset = [Math]::Min($scroller.HorizontalOffset, [Math]::Max(0, 440 + $width + $spacer.Width - 1000))
    }
    $window.FitColumn($grid, 150)
    if ($scroller.HorizontalOffset -ne 125 -or $scroller.ViewChanges -ne 0) { throw "Async AutoFit shrink clamped or overrode manual scrolling (right=$right)." }
    Write-Output "PASS: async AutoFit preserves the user's current offset (right=$right)"

    $window = [OrielFolder.MainWindow]::new()
    $scroller = if ($right) { $window.MillerScrollViewerRight } else { $window.LeftScroller }
    $scroller.DispatcherQueue.Defer = $true
    $window.AddColumn($right)
    if ($right) { $window.ViewModel.RightExplorer = [OrielFolder.ViewModels.ExplorerViewModel]::new() }
    else { $window.ViewModel.LeftExplorer = [OrielFolder.ViewModels.ExplorerViewModel]::new() }
    $scroller.DispatcherQueue.Flush()
    if ($scroller.ViewChanges -ne 0) { throw 'A stale callback aligned a different tab group.' }
    Write-Output "PASS: changing groups discards the old alignment callback (right=$right)"
}
