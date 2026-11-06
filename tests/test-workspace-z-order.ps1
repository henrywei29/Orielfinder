param([switch] $ReproduceOldZOrder, [switch] $ReproduceOldGroupDetach)
$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.WorkspaceLayout.cs'))
if ($ReproduceOldZOrder) {
    $source = [regex]::Replace($source, 'Canvas\.SetZIndex\(divider, [^)]+\);', 'Canvas.SetZIndex(divider, int.MaxValue);')
    $source = [regex]::Replace($source, '(?s)var ordered = _workspaceViews\.Values.*?Canvas\.SetZIndex\(ordered\[i\], i\);',
        'int top = _workspaceViews.Values.Where(v => _workspaceCanvas.Children.Contains(v)).Select(Canvas.GetZIndex).DefaultIfEmpty().Max(); Canvas.SetZIndex(view, top + 1);')
}
$methods = foreach ($name in @('AddWorkspaceDivider', 'ActivateWorkspaceView')) {
    $match = [regex]::Match($source, '(?sm)^        private void ' + $name + '\(.*?(?=^        (?:private |///|// ={5}|#)|^    \})')
    if (-not $match.Success) { throw "Cannot locate production method $name." }
    $match.Value.TrimEnd()
}
$constants = [regex]::Matches($source, '(?m)^        private const int [^;]+;') | ForEach-Object Value
$syncMatch = [regex]::Match($source, '(?sm)^                foreach \(var cached in _workspaceViews\).*?(?=^                ClassicPaneHost.Visibility)')
if (-not $syncMatch.Success) { throw 'Cannot locate production cached pane synchronization loops.' }
$syncBlock = $syncMatch.Value
if ($ReproduceOldGroupDetach) { $syncBlock = '_workspaceCanvas!.Children.Clear();' + $syncBlock }
$hostSource = @'
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace OrielFolder {
    public enum PaneLayoutKind { Tiled, Floating }
    public enum Visibility { Visible, Collapsed }
    public class Explorer {}
    public class WorkspacePaneState { public Explorer Explorer = new(); }
    public class WorkspaceLayoutState {
        public bool Enabled = true;
        public PaneLayoutKind Layout = PaneLayoutKind.Floating;
        public List<WorkspacePaneState> Panes = new();
        public WorkspacePaneState? ActivePane;
        public double[] ColumnWeights = Array.Empty<double>(), RowWeights = Array.Empty<double>();
    }
    public class Tab { public WorkspaceLayoutState WorkspaceLayout = new(); }
    public class WindowViewModel {
        public Tab ActiveTab = new();
        public bool IsFreeLayoutActive = true;
        public Explorer? LeftExplorer;
        public int CurrentViewMode;
        public void ActivateWorkspacePane(WorkspacePaneState pane) { ActiveTab.WorkspaceLayout.ActivePane = pane; LeftExplorer = pane.Explorer; }
    }
    public class UIElement { public int Z; public double Left, Top; public Visibility Visibility = Visibility.Visible; }
    public class PointerPoint {
        public PointerProperties Properties = new();
        public Point Position = new();
    }
    public class PointerProperties { public bool IsLeftButtonPressed = true; }
    public class Point { public double X, Y; }
    public class PointerArgs : EventArgs {
        public object Pointer = new(); public bool Handled;
        public PointerPoint GetCurrentPoint(object? element) => new();
    }
    public class Border : UIElement {
        public double Width, Height; public object? Background;
        public event EventHandler<PointerArgs>? PointerPressed, PointerMoved, PointerReleased, PointerCaptureLost;
        public bool CapturePointer(object pointer) => true;
        public void ReleasePointerCapture(object pointer) {}
        public void KeepEventsReferenced() { var args = new PointerArgs(); PointerPressed?.Invoke(this, args); PointerMoved?.Invoke(this, args); PointerReleased?.Invoke(this, args); PointerCaptureLost?.Invoke(this, args); }
    }
    public class WorkspaceFolderPane : UIElement { public bool Active; public void SetActive(bool value) => Active = value; }
    public class Canvas {
        public List<UIElement> Children = new();
        public double ActualWidth = 1000, ActualHeight = 800;
        public static int GetZIndex(UIElement element) => element.Z;
        public static void SetZIndex(UIElement element, int value) {
            if (value < -1_000_000 || value > 1_000_000) throw new ArgumentException("Canvas.ZIndex exceeds the native WinUI range.");
            element.Z = value;
        }
        public static void SetLeft(UIElement element, double value) => element.Left = value;
        public static void SetTop(UIElement element, double value) => element.Top = value;
    }
    public class MainWindow {
        private Canvas? _workspaceCanvas = new();
        private readonly Dictionary<WorkspacePaneState, WorkspaceFolderPane> _workspaceViews = new();
        private readonly List<UIElement> _workspaceDividers = new();
        private bool _workspaceDividerDragging;
        private bool _suppressFocusOnViewModeChange;
        private bool _syncingWorkspace = false;
        private WindowViewModel ViewModel = new();
        private object GetThemeBrush(string key) => new();
        private void ArrangeWorkspacePanes() {}
        private void SetViewModeVisibility(int mode) {}
        private void UpdateToolbarButtonStates() {}
        private void UpdateFileSystemWatcherPaths() {}
        private WorkspaceFolderPane GetOrCreateWorkspacePane(WorkspacePaneState pane) {
            if (!_workspaceViews.TryGetValue(pane, out var view)) { view = new(); _workspaceViews.Add(pane, view); }
            return view;
        }
        public MainWindow() {
            for (int i = 0; i < 3; i++) {
                var pane = new WorkspacePaneState(); var view = new WorkspaceFolderPane();
                ViewModel.ActiveTab.WorkspaceLayout.Panes.Add(pane);
                _workspaceViews.Add(pane, view); _workspaceCanvas.Children.Add(view);
            }
        }
        public int AddDivider(bool vertical) {
            AddWorkspaceDivider(ViewModel.ActiveTab.WorkspaceLayout, vertical, 0, 500, 800, 2);
            return Canvas.GetZIndex(_workspaceDividers.Last());
        }
        public bool DividerDragging => _workspaceDividerDragging;
        public WorkspaceLayoutState Group => ViewModel.ActiveTab.WorkspaceLayout;
        public WorkspaceFolderPane ViewFor(WorkspacePaneState pane) => _workspaceViews[pane];
        public bool Attached(WorkspacePaneState pane) => _workspaceCanvas!.Children.Contains(_workspaceViews[pane]);
        public int[] VisibleZ() => ViewModel.ActiveTab.WorkspaceLayout.Panes.Select(p => Canvas.GetZIndex(_workspaceViews[p])).ToArray();
        public void SeedNearLimit() { foreach (var view in _workspaceViews.Values) Canvas.SetZIndex(view, 1_000_000); }
        public WorkspaceFolderPane AddHiddenCachedPane() {
            var pane = new WorkspacePaneState();
            var view = new WorkspaceFolderPane { Visibility = Visibility.Collapsed };
            Canvas.SetZIndex(view, 73);
            _workspaceViews.Add(pane, view); _workspaceCanvas!.Children.Add(view);
            return view;
        }
        public void Activate(int index) => ActivateWorkspaceView(ViewModel.ActiveTab.WorkspaceLayout.Panes[index]);
        public bool ActiveIsTop(int index) {
            var z = VisibleZ(); return z[index] == z.Max() && z.Distinct().Count() == z.Length;
        }
'@
Add-Type -TypeDefinition ($hostSource + "`n" + ($constants -join "`n") + "`n" + ($methods -join "`n") +
    "`npublic void SyncGroup(WorkspaceLayoutState state) { ViewModel.ActiveTab.WorkspaceLayout = state;`n" + $syncBlock + "`n}`n    }`n}")

$failures = @()
foreach ($orientation in @($true, $false)) {
    try {
        $window = [OrielFolder.MainWindow]::new()
        $z = $window.AddDivider($orientation)
        if ($z -lt 3 -or $z -gt 1000000) { throw 'Divider must be above pane layers and within the WinUI range.' }
        Write-Output "PASS: divider creation respects native ZIndex range (vertical=$orientation)"
    } catch { $failures += "Divider creation failed (vertical=$orientation): $($_.Exception.Message)" }
}
try {
    $window = [OrielFolder.MainWindow]::new()
    $window.SeedNearLimit()
    $hidden = $window.AddHiddenCachedPane()
    $window.Activate(1)
    if (-not $window.ActiveIsTop(1)) { throw 'Activating a floating pane must put it above every peer.' }
    foreach ($index in @(2, 0, 1)) { $window.Activate($index) }
    $first = $window.VisibleZ()
    for ($cycle = 0; $cycle -lt 200; $cycle++) {
        foreach ($index in @(2, 0, 1)) {
            $window.Activate($index)
            if (-not $window.ActiveIsTop($index)) { throw 'Repeated activation lost the active pane ordering.' }
        }
    }
    $after = $window.VisibleZ()
    if ([Linq.Enumerable]::Max([int[]]$after) -gt 3) { throw 'Floating Z order must remain bounded by the visible pane count.' }
    if (($first -join ',') -ne ($after -join ',')) { throw 'Repeating the same activation order must not accumulate Z indices.' }
    if ($hidden.Z -ne 73 -or $hidden.Visibility.ToString() -ne 'Collapsed') { throw 'Floating ordering must leave cached inactive panes unchanged.' }
    Write-Output 'PASS: floating activation normalizes existing layers and remains bounded after 600 activations'
} catch { $failures += "Floating activation failed: $($_.Exception.Message)" }
try {
    $window = [OrielFolder.MainWindow]::new()
    $groupA = $window.Group
    $paneA = $groupA.Panes[0]
    $viewA = $window.ViewFor($paneA)
    $groupB = [OrielFolder.WorkspaceLayoutState]::new()
    $paneB = [OrielFolder.WorkspacePaneState]::new()
    $groupB.Panes.Add($paneB)
    $window.SyncGroup($groupB)
    $viewB = $window.ViewFor($paneB)
    if (-not $window.Attached($paneA) -or $viewA.Visibility.ToString() -ne 'Collapsed') { throw 'Switching groups must keep inactive cached views attached and hidden.' }
    if (-not $window.Attached($paneB) -or $viewB.Visibility.ToString() -ne 'Visible') { throw 'The newly active group must be attached and visible.' }
    $window.SyncGroup($groupA)
    if (-not [object]::ReferenceEquals($viewA, $window.ViewFor($paneA)) -or $viewA.Visibility.ToString() -ne 'Visible' -or -not $window.Attached($paneA)) { throw 'Returning to a group must reuse its still-attached view.' }
    if (-not $window.Attached($paneB) -or $viewB.Visibility.ToString() -ne 'Collapsed') { throw 'The group being left must remain attached and become hidden.' }
    Write-Output 'PASS: group A to B to A preserves cached view identity and attachment while switching visibility'
} catch { $failures += "Group synchronization failed: $($_.Exception.Message)" }
if ($failures.Count) { throw ($failures -join "`n") }
