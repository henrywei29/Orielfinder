$ErrorActionPreference = 'Stop'
$sources = @{
    Routing = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.WorkspaceRouting.cs'))
    Settings = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.SettingsHandler.cs'))
}
$methods = foreach ($entry in @(
    @('Routing', 'QueueWorkspaceColumnLayout'),
    @('Routing', 'OnWorkspaceMillerItemsLoaded'),
    @('Routing', 'RouteWorkspaceAfterReplace'),
    @('Settings', 'ApplyMillerCheckboxMode'),
    @('Settings', 'ApplyCheckboxToItemsControl')
)) {
    $match = [regex]::Match($sources[$entry[0]], '(?sm)^        private (?:void|bool) ' + $entry[1] + '\(.*?(?=^        (?:private |///|// ={5}|#)|^    \})')
    if (-not $match.Success) { throw "Cannot locate production method $($entry[1])." }
    $match.Value.TrimEnd()
}
$hostSource = @'
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using OrielFolder.Models;
using OrielFolder.ViewModels;
using OrielFolder.Views;
namespace Microsoft.UI.Dispatching {
    public enum DispatcherQueuePriority { Low }
    public class DispatcherQueue {
        public Queue<Action> Pending = new();
        public bool TryEnqueue(DispatcherQueuePriority priority, Action action) { Pending.Enqueue(action); return true; }
        public void Flush() { while (Pending.Count > 0) Pending.Dequeue()(); }
    }
}
namespace OrielFolder.ViewModels { public class ExplorerViewModel { public bool IsReplacingLastColumn; } }
namespace OrielFolder.Models { public class WorkspacePaneState { public ExplorerViewModel Explorer = new(); } }
namespace OrielFolder.Views {
    public class WorkspaceFolderPane {
        public OrielFolder.ItemsControl MillerItems = new();
        public OrielFolder.ScrollViewer MillerScroller = new();
    }
}
namespace OrielFolder {
    public enum ListViewSelectionMode { Extended, Multiple }
    public class ListView { public ListViewSelectionMode SelectionMode; }
    public class Border { public double Width; }
    public class RoutedEventArgs : EventArgs {}
    public class ItemsControl {
        public object? ItemsPanelRoot = new();
        public List<ListView> Items = new() { new(), new() };
    }
    public class ScrollViewer {
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue = new();
        public Border Spacer = new();
    }
    public class MainWindow {
        private bool _isClosed = false;
        private ListViewSelectionMode _millerSelectionMode;
        private Dictionary<int, (ItemsControl items, object extra)> _tabMillerPanels = new();
        private Dictionary<WorkspacePaneState, WorkspaceFolderPane> _workspaceViews = new();
        private ItemsControl MillerColumnsControlRight = new();
        private bool TryGetVisibleWorkspacePane(ExplorerViewModel explorer, out WorkspaceFolderPane view) {
            foreach (var entry in _workspaceViews)
                if (entry.Key.Explorer == explorer) { view = entry.Value; return true; }
            view = null!; return false;
        }
        private void AdjustSpacerAndScrollAfterReplace(ExplorerViewModel explorer, ScrollViewer sv, Border spacer, ItemsControl items) {}
        private Border GetMillerSpacer(ScrollViewer sv) => sv.Spacer;
        private void UpdateFileSystemWatcherPaths() {}
        private ListView GetListViewFromItemsControl(ItemsControl items, int index) => items.Items[index];
        public WorkspacePaneState Pane = new();
        public WorkspaceFolderPane View = new();
        public MainWindow() { _workspaceViews.Add(Pane, View); }
        public void SetCheckboxes(bool enabled) => ApplyMillerCheckboxMode(enabled);
        public void ResetRealizedColumns() { View.MillerItems.Items = new() { new(), new() }; }
        public void LoadExistingColumns() => OnWorkspaceMillerItemsLoaded(View.MillerItems, new RoutedEventArgs());
        public void AddColumn() => QueueWorkspaceColumnLayout(Pane, View,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new object()));
        public void ReplaceColumn(bool inserted) => RouteWorkspaceAfterReplace(Pane.Explorer, inserted);
        public void Flush() => View.MillerScroller.DispatcherQueue.Flush();
'@
Add-Type -TypeDefinition ($hostSource + "`n" + ($methods -join "`n") + "`n    }`n}")

$failures = @()
foreach ($case in @('enable', 'disable', 'loaded', 'add', 'replace', 'cancelled replacement')) {
    $window = [OrielFolder.MainWindow]::new()
    $window.SetCheckboxes($true)
    switch ($case) {
        'disable' { $window.SetCheckboxes($false) }
        'loaded' { $window.ResetRealizedColumns(); $window.LoadExistingColumns() }
        'add' { $window.ResetRealizedColumns(); $window.AddColumn(); $window.Flush() }
        'replace' { $window.ResetRealizedColumns(); $window.ReplaceColumn($true); $window.Flush() }
        'cancelled replacement' { $window.ResetRealizedColumns(); $window.ReplaceColumn($false); $window.Flush() }
    }
    $expected = if ($case -in @('disable', 'cancelled replacement')) { 'Extended' } else { 'Multiple' }
    if (@($window.View.MillerItems.Items | Where-Object { $_.SelectionMode.ToString() -ne $expected }).Count) {
        $failures += "Workspace checkbox mode failed after $case (expected $expected)."
    } else { Write-Output "PASS: workspace checkbox mode after $case" }
}
if ($failures.Count) { throw ($failures -join "`n") }
