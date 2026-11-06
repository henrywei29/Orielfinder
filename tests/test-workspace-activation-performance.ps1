$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.WorkspaceLayout.cs'))
$method = [regex]::Match($source, '(?sm)^        private void ActivateWorkspaceView\(.*?^        }').Value
if (-not $method) { throw 'Cannot locate production workspace activation.' }
$hostSource = @'
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
namespace OrielFolder {
    public enum Visibility { Visible, Collapsed }
    public enum PaneLayoutKind { Tiled, Floating }
    public class WorkspacePaneState { public object Explorer = new(); }
    public class WorkspaceFolderPane {
        public Visibility Visibility;
        public int Highlights, Z;
        public void SetActive(bool active) { Highlights++; }
    }
    public class Canvas {
        public List<WorkspaceFolderPane> Children = new();
        public static int GetZIndex(WorkspaceFolderPane view) => view.Z;
        public static void SetZIndex(WorkspaceFolderPane view, int index) { view.Z = index; }
    }
    public class LayoutState {
        public bool Enabled = true;
        public List<WorkspacePaneState> Panes = new();
        public WorkspacePaneState? ActivePane;
        public PaneLayoutKind Layout;
    }
    public class Tab { public LayoutState WorkspaceLayout = new(); }
    public class Vm {
        public Tab ActiveTab = new();
        public bool IsFreeLayoutActive = true;
        public object? LeftExplorer;
        public int CurrentViewMode, Activations;
        public Action? OnActivate;
        public void ActivateWorkspacePane(WorkspacePaneState pane) {
            Activations++; ActiveTab.WorkspaceLayout.ActivePane = pane;
            LeftExplorer = pane.Explorer; IsFreeLayoutActive = true; OnActivate?.Invoke();
        }
    }
    public partial class MainWindow {
        public Vm ViewModel = new();
        public bool _syncingWorkspace, _suppressFocusOnViewModeChange;
        public Dictionary<WorkspacePaneState, WorkspaceFolderPane> _workspaceViews = new();
        public Canvas _workspaceCanvas = new();
        public int Toolbars, Watchers, ModeUpdates;
        public void Activate(WorkspacePaneState pane) => ActivateWorkspaceView(pane);
        private void SetViewModeVisibility(int mode) { ModeUpdates++; }
        private void UpdateToolbarButtonStates() { Toolbars++; }
        private void UpdateFileSystemWatcherPaths() { Watchers++; }
'@
Add-Type -TypeDefinition ($hostSource + $method + "`n} }")
$window = [OrielFolder.MainWindow]::new()
$pane = [OrielFolder.WorkspacePaneState]::new()
$other = [OrielFolder.WorkspacePaneState]::new()
$group = $window.ViewModel.ActiveTab.WorkspaceLayout
foreach($p in @($pane,$other)) {
    $group.Panes.Add($p)
    $view = [OrielFolder.WorkspaceFolderPane]::new()
    $window._workspaceViews.Add($p,$view)
    $window._workspaceCanvas.Children.Add($view)
}
$window.Activate($pane)
if ($window.ViewModel.Activations -ne 1 -or $window.Watchers -ne 1 -or $window.Toolbars -ne 1) { throw 'First pane activation did not refresh routing/commands/watchers.' }
'PASS: first pane activation updates explorer and command routing'
for($i=0;$i -lt 100;$i++) { $window.Activate($pane) }
"MEASURE: 100 repeated focus/pointer activations, extra watcher updates=$($window.Watchers-1), extra toolbar updates=$($window.Toolbars-1)"
if ($window.Watchers -ne 1 -or $window.Toolbars -ne 1 -or $window._workspaceViews[$pane].Highlights -ne 1) { throw 'Click/focus within the already-active pane repeats layout, toolbar and watcher work.' }
'PASS: already-active pane avoids redundant highlighting and watcher/toolbar work'
$window.Activate($other)
if ($window.ViewModel.LeftExplorer -ne $other.Explorer -or $window.Watchers -ne 2 -or $window.Toolbars -ne 2) { throw 'Switching panes lost required updates.' }
'PASS: switching to another pane still refreshes commands and watched folders'
$window._syncingWorkspace = $true
$window.Activate($other)
if ($window.Watchers -ne 3 -or $window._workspaceViews[$other].Highlights -ne 3) { throw 'Group synchronization skipped required visual/watcher refresh.' }
$window._syncingWorkspace = $false
'PASS: group synchronization still restores active highlight and watchers'
$group.Layout = [OrielFolder.PaneLayoutKind]::Floating
$window._workspaceViews[$pane].Z = 10
$window.Activate($other)
if ($window._workspaceViews[$other].Z -ne 1 -or $window._workspaceViews[$pane].Z -ne 0 -or $window.Watchers -ne 3) { throw 'Reactivating floating pane failed to raise it without repeating watcher work.' }
'PASS: floating pane still moves to front on repeated activation'
$window.Activate([OrielFolder.WorkspacePaneState]::new())
if ($window.Watchers -ne 3) { throw 'A foreign pane triggered updates.' }
'PASS: foreign panes remain ignored'
