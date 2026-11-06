$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path $repo 'src\OrielFolder\OrielFolder'
$adapterPath = Join-Path $root 'ViewModels\MainViewModel.MultiPane.cs'
if (-not (Test-Path -LiteralPath $adapterPath)) { throw 'Workspace explorer bridge is not implemented.' }

# Reuse the split-tab harness infrastructure, then execute the real adapter and lifecycle methods.
$splitTest = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'test-split-tab-groups.ps1'))
$harness = $splitTest.Substring(0, $splitTest.IndexOf('Add-Type -TypeDefinition'))
$harness = $harness.Replace('$PSScriptRoot', "'$($PSScriptRoot.Replace("'", "''"))'")
Invoke-Expression $harness
$extraMethods = foreach ($name in @('UpdateActiveTabHeader', 'CloseOtherTabs', 'CloseTabsToRight')) {
    $match = [regex]::Match($tabSource, '(?sm)^        public (?:void|List<string>) ' + $name + '\(.*?^        }')
    if (-not $match.Success) { throw "Cannot locate production method $name." }
    $match.Value
}
$viewModeSource = [IO.File]::ReadAllText((Join-Path $root 'ViewModels\MainViewModel.ViewMode.cs'))
$switchMode = [regex]::Match($viewModeSource, '(?sm)^        public void SwitchViewMode\(.*?^        }').Value
$resolveMode = [regex]::Match($tabSource, '(?sm)^        public ViewMode ResolveViewModeFromHome\(.*?^        }').Value
$cleanupLoop = [regex]::Match($vmSource, '(?sm)^                foreach \(var (?:tab|explorer) in (?:Tabs|EnumerateOwnedExplorers\(\))\).*?^                }').Value
if (-not $switchMode -or -not $resolveMode -or -not $cleanupLoop) { throw 'Cannot locate actual view-mode or explorer cleanup behavior.' }
$hostPrefix = $hostPrefix.Replace('public ViewMode LeftViewMode => _leftViewMode;', 'public ViewMode LeftViewMode { get => _leftViewMode; set => _leftViewMode = value; }')
$hostPrefix = $hostPrefix.Replace('public ViewMode IconSize;', 'public ViewMode IconSize; public ViewMode? PreferredViewMode;')
$hostPrefix = $hostPrefix.Replace('public int Tab2StartupBehavior => 0;', 'public int Tab2StartupBehavior => 0; public int DefaultViewMode => 0; public int LastUsedFolderViewMode { get; set; }')
$hostPrefix = $hostPrefix.Replace('public static class Services {', 'public static class Services { public static object? GetService(this object services, Type type) => new OrielFolder.Services.SettingsService();')
$hostPrefix = $hostPrefix.Replace('public static class ViewModeExtensions {', 'public static class ViewModeExtensions { public static string GetDisplayName(ViewMode mode) => mode.ToString();')
$hostPrefix += @'
        private ViewMode? _viewModeBeforeHome;
        private ViewMode? _lastClosedViewMode;
        private void SaveViewModePreference() {}
        private void OpenOrSwitchToSettingsTab() {}
        private Task RefreshRecycleBinInfoAsync() => Task.CompletedTask;
'@
$hostPrefix = $hostPrefix.Replace('public ExplorerViewModel LeftExplorer { get => _leftExplorer; set => _leftExplorer = value; }', 'public ExplorerViewModel LeftExplorer { get => _leftExplorer; set { _leftExplorer = value; OnPropertyChanged("LeftExplorer"); } }')
$hostPrefix = $hostPrefix.Replace('public List<string> Notifications = new();', 'public List<string> Notifications = new(); public Action<string>? Observe;')
$hostPrefix = $hostPrefix.Replace('Notifications.Add(name);', 'Notifications.Add(name); Observe?.Invoke(name);')
$model = [IO.File]::ReadAllText((Join-Path $root 'Models\WorkspacePaneState.cs'))
$geometry = [IO.File]::ReadAllText((Join-Path $root 'Models\PaneLayoutGeometry.cs')).Replace('namespace OrielFolder.Models;', 'namespace OrielFolder.Models {') + "`n}"
$geometry = [regex]::Replace($geometry, '(?m)^using .*;\r?\n', '')
$tabModel = [IO.File]::ReadAllText((Join-Path $root 'Models\TabItem.MultiPane.cs'))
$adapter = [regex]::Replace([IO.File]::ReadAllText($adapterPath), '(?m)^using .*;\r?\n', '')
Add-Type -TypeDefinition ($hostPrefix + $rightProperty.Value + ($methods -join "`n") + ($extraMethods -join "`n") + $switchMode + $resolveMode + "`n public void RunExplorerCleanup() {`n" + $cleanupLoop + "`n}`n} }`n" + $geometry + $model + $tabModel + $adapter)

function New-Group {
    $vm = [OrielFolder.ViewModels.MainViewModel]::new()
    $vm.AddNewTab()
    $vm.CurrentViewMode = [OrielFolder.Models.ViewMode]::IconLarge
    $vm.CurrentIconSize = [OrielFolder.Models.ViewMode]::IconLarge
    $vm.LeftExplorer.CurrentPath = 'classic-left'
    $vm.RightExplorer.CurrentPath = 'classic-right'
    $vm.RightViewMode = [OrielFolder.Models.ViewMode]::List
    $vm.IsSplitViewEnabled = $true
    $vm.ActivePane = [OrielFolder.Models.ActivePane]::Right
    $vm.SaveActiveTabState()
    $vm.ActiveTab.WorkspaceLayout = [OrielFolder.Models.WorkspaceLayoutState]::new()
    $vm.ActiveTab.WorkspaceLayout.Enabled = $true
    $vm.ActiveTab.WorkspaceLayout.ClassicSplitEnabled = $true
    return $vm
}

$vm = New-Group
$tab = $vm.ActiveTab
$classic = $tab.Explorer
$right = $tab.SplitRightExplorer
$pane = $tab.WorkspaceLayout.Add($vm.CreateWorkspaceExplorer('D:\pane'), [OrielFolder.Models.ViewMode]::Details)
$vm.Observe = { param($name) if ($name -eq 'LeftExplorer') { $vm.SaveActiveTabState(); $vm.UpdateActiveTabHeader() } }
$vm.ActivateWorkspacePane($pane)
$vm.SaveActiveTabState()
$vm.UpdateActiveTabHeader()
if (-not $vm.IsFreeLayoutActive -or $vm.LeftExplorer -ne $pane.Explorer -or $vm.ActiveExplorer -ne $pane.Explorer -or $vm.IsSplitViewEnabled -or $vm.ActivePane -ne [OrielFolder.Models.ActivePane]::Left) { throw 'Activating a pane did not route explorer commands to the selected folder.' }
if ($tab.Explorer -ne $classic -or $tab.Path -ne 'classic-left' -or $tab.ViewMode -ne [OrielFolder.Models.ViewMode]::IconLarge -or $tab.IconSize -ne [OrielFolder.Models.ViewMode]::IconLarge -or -not $tab.IsSplitEnabled -or $tab.SplitRightExplorer -ne $right -or $tab.SplitRightViewMode -ne [OrielFolder.Models.ViewMode]::List -or $tab.SplitActivePane -ne [OrielFolder.Models.ActivePane]::Right) { throw 'Bridge activation or reentrant save overwrote the classic group state.' }
Write-Output 'PASS: pane activation routes commands and preserves classic metadata during reentrant notifications'

$vm.Observe = $null
$vm.CurrentViewMode = [OrielFolder.Models.ViewMode]::IconSmall
$second = $tab.WorkspaceLayout.Add($vm.CreateWorkspaceExplorer('PC'), [OrielFolder.Models.ViewMode]::Home)
$vm.ActivateWorkspacePane($second)
if ($pane.Mode -ne [OrielFolder.Models.ViewMode]::IconSmall -or $vm.CurrentViewMode -ne [OrielFolder.Models.ViewMode]::Home -or $second.Mode -ne [OrielFolder.Models.ViewMode]::Home) { throw 'Switching panes lost the old mode or failed to restore the pane Home view.' }
$vm.ActivateWorkspacePane($pane)
if ($vm.CurrentIconSize -ne [OrielFolder.Models.ViewMode]::IconSmall -or $vm.CurrentViewMode -ne [OrielFolder.Models.ViewMode]::IconSmall) { throw 'Pane activation did not restore its icon size.' }
$foreign = [OrielFolder.Models.WorkspaceLayoutState]::new().Add($vm.CreateWorkspaceExplorer(''), [OrielFolder.Models.ViewMode]::List)
$vm.ActivateWorkspacePane($foreign)
if ($vm.LeftExplorer -ne $pane.Explorer -or $tab.WorkspaceLayout.ActivePane -ne $pane) { throw 'A foreign pane changed the active tab bridge.' }
$vm.LeaveWorkspacePaneBridge()
if ($vm.IsFreeLayoutActive -or $vm.LeftExplorer -ne $classic -or $vm.CurrentViewMode -ne [OrielFolder.Models.ViewMode]::IconLarge -or $vm.CurrentIconSize -ne [OrielFolder.Models.ViewMode]::IconLarge -or -not $vm.IsSplitViewEnabled -or $vm.RightViewMode -ne [OrielFolder.Models.ViewMode]::List -or $vm.ActivePane -ne [OrielFolder.Models.ActivePane]::Right) { throw 'Leaving workspace failed to restore the classic folder layout.' }
if ($pane.Explorer.CleanupCount -ne 0 -or $second.Explorer.CleanupCount -ne 0 -or $classic.CleanupCount -ne 0) { throw 'Leaving the bridge disposed retained explorers.' }
Write-Output 'PASS: pane modes and icon sizes survive activation while foreign panes are ignored and classic layout restores'

$vm.ActivateWorkspacePane($pane)
$vm.AddNewTab()
if ($vm.IsFreeLayoutActive -or $vm.ActiveTab -eq $tab -or $vm.ActiveTab.ViewMode -ne [OrielFolder.Models.ViewMode]::Home -or $vm.LeftExplorer -eq $pane.Explorer) { throw 'Switching to a new tab retained the old workspace bridge.' }
if ($vm.CurrentIconSize -ne [OrielFolder.Models.ViewMode]::IconMedium) { throw 'A new classic tab inherited the workspace pane icon size.' }
$vm.SwitchToTab(0)
if ($vm.IsFreeLayoutActive -or $vm.LeftExplorer -ne $classic -or $vm.ActiveTab.ViewMode -ne [OrielFolder.Models.ViewMode]::IconLarge) { throw 'Normal tab restore did not restore classic state before the workspace UI hook.' }
$vm.ActivateWorkspacePane($pane)
$owned = @($vm.EnumerateOwnedExplorers())
if ($owned.Count -ne 6 -or $owned -notcontains $classic -or $owned -notcontains $right -or $owned -notcontains $pane.Explorer -or $owned -notcontains $second.Explorer) { throw 'Window cleanup enumeration omitted classic or workspace explorers.' }
$tab.WorkspaceLayout.Add($classic, [OrielFolder.Models.ViewMode]::List) | Out-Null
if (@($vm.EnumerateOwnedExplorers()).Count -ne 6) { throw 'Shared classic/workspace explorers are enumerated twice.' }
$vm.CloseTab(0)
if ($classic.CleanupCount -ne 1 -or $right.CleanupCount -ne 1 -or $pane.Explorer.CleanupCount -ne 1 -or $second.Explorer.CleanupCount -ne 1 -or $vm.LeftExplorer.CleanupCount -ne 0 -or $vm.IsFreeLayoutActive) { throw 'Closing the active workspace group did not dispose only its owned explorers once.' }
Write-Output 'PASS: tab switching resets bridge and owned explorers are deduplicated for group close'

foreach ($action in @('others', 'right', 'last')) {
    $vm = New-Group
    $original = $vm.ActiveTab
    $ownedPane = $original.WorkspaceLayout.Add($vm.CreateWorkspaceExplorer(''), [OrielFolder.Models.ViewMode]::Details)
    if ($action -ne 'last') {
        $vm.AddNewTab()
        $keep = $vm.ActiveTab
        if ($action -eq 'others') { $vm.CloseOtherTabs($keep) | Out-Null }
        else { $vm.Tabs.Reverse(); $vm.ActiveTabIndex = 0; $vm.CloseTabsToRight($keep) | Out-Null }
    }
    else { $vm.CloseTab(0) }
    if ($ownedPane.Explorer.CleanupCount -ne 1 -or $original.Explorer.CleanupCount -ne 1 -or $original.SplitRightExplorer.CleanupCount -ne 1) { throw "The $action close path leaked or double disposed a workspace explorer." }
}
Write-Output 'PASS: other-tab, tabs-to-right, and last-tab close release every workspace explorer'

$vm = New-Group
$closing = $vm.ActiveTab
$shared = $closing.Explorer
$vm.AddNewTab()
$retained = $vm.ActiveTab
$retained.WorkspaceLayout = [OrielFolder.Models.WorkspaceLayoutState]::new()
$retained.WorkspaceLayout.Add($shared, [OrielFolder.Models.ViewMode]::Details) | Out-Null
$vm.CloseTab(0)
if ($shared.CleanupCount -ne 0 -or @($vm.EnumerateOwnedExplorers()) -notcontains $shared) { throw 'Closing a group disposed an explorer still referenced by a retained group.' }
$vm.CloseTab(0)
if ($shared.CleanupCount -ne 1) { throw 'The final owner did not release its shared explorer once.' }
Write-Output 'PASS: shared explorers remain alive until their final owning group closes'

$vm = [OrielFolder.ViewModels.MainViewModel]::new()
foreach ($path in @('', 'PC')) {
    $explorer = $vm.CreateWorkspaceExplorer($path)
    if ($explorer.CurrentPath -ne '') { throw 'A virtual root was passed to real folder navigation.' }
}
$explorer = $vm.CreateWorkspaceExplorer('D:\folder')
if ($explorer.CurrentPath -ne 'D:\folder' -or -not $explorer.EnableAutoNavigation) { throw 'New workspace explorers do not initialize and navigate independently.' }
Write-Output 'PASS: workspace explorer creation navigates real paths and leaves virtual roots alone'

$vm = New-Group
$tab = $vm.ActiveTab
$tab.PreferredViewMode = [OrielFolder.Models.ViewMode]::List
$pane = $tab.WorkspaceLayout.Add($vm.CreateWorkspaceExplorer(''), [OrielFolder.Models.ViewMode]::Details)
$vm.ActivateWorkspacePane($pane)
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::IconSmall)
$vm.SaveActiveTabState()
if ($tab.ViewMode -ne [OrielFolder.Models.ViewMode]::IconLarge -or $tab.IconSize -ne [OrielFolder.Models.ViewMode]::IconLarge -or $pane.Mode -ne [OrielFolder.Models.ViewMode]::IconSmall) { throw 'SwitchViewMode overwrote classic metadata or failed to persist the free pane mode.' }
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::Home)
$vm.SaveActiveTabState()
if ($pane.Mode -ne [OrielFolder.Models.ViewMode]::Home -or $vm.ResolveViewModeFromHome() -ne [OrielFolder.Models.ViewMode]::IconSmall -or $tab.PreferredViewMode -ne [OrielFolder.Models.ViewMode]::List) { throw 'Free-pane Home navigation consumed classic mode preferences or forgot the pane folder mode.' }
$other = $tab.WorkspaceLayout.Add($vm.CreateWorkspaceExplorer(''), [OrielFolder.Models.ViewMode]::Details)
$vm.ActivateWorkspacePane($other)
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::Home)
if ($vm.ResolveViewModeFromHome() -ne [OrielFolder.Models.ViewMode]::Details) { throw 'Home-to-folder mode restoration leaked between free panes.' }
$vm.ActivateWorkspacePane($pane)
if ($vm.CurrentViewMode -ne [OrielFolder.Models.ViewMode]::Home -or $vm.ResolveViewModeFromHome() -ne [OrielFolder.Models.ViewMode]::IconSmall) { throw 'Reactivating a Home pane lost its independent folder preference.' }
$vm.SwitchViewMode($vm.ResolveViewModeFromHome())
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::RecycleBin)
if ($tab.ViewMode -ne [OrielFolder.Models.ViewMode]::IconLarge -or $tab.IconSize -ne [OrielFolder.Models.ViewMode]::IconLarge) { throw 'The RecycleBin mode branch overwrote classic free-layout metadata.' }
if ($vm.Tabs.Count -ne 2 -or $vm.ActiveTab -eq $tab -or $vm.IsFreeLayoutActive -or $vm.CurrentViewMode -ne [OrielFolder.Models.ViewMode]::RecycleBin -or $vm.ActiveTab.ViewMode -ne [OrielFolder.Models.ViewMode]::RecycleBin -or $vm.LeftExplorer -eq $pane.Explorer) { throw 'RecycleBin from a free pane did not open exactly one independent classic tab with a cleared bridge.' }
if ($tab.WorkspaceLayout.ActivePane -ne $pane -or $pane.Mode -ne [OrielFolder.Models.ViewMode]::IconSmall -or -not $tab.WorkspaceLayout.Enabled -or $tab.WorkspaceLayout.Panes.Count -ne 2) { throw 'Opening RecycleBin lost the original workspace selection, folder mode, or panes.' }
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::RecycleBin)
if ($vm.Tabs.Count -ne 2) { throw 'Repeating the RecycleBin command created another tab or retained a recursive bridge.' }
Write-Output 'PASS: real mode switching protects classic metadata and Home returns each pane to its own folder mode'

$vm = New-Group
$tab = $vm.ActiveTab
$classic = $tab.Explorer
$right = $tab.SplitRightExplorer
$pane = $tab.WorkspaceLayout.Add($vm.CreateWorkspaceExplorer(''), [OrielFolder.Models.ViewMode]::Details)
$tab.WorkspaceLayout.Add($classic, [OrielFolder.Models.ViewMode]::List) | Out-Null
$vm.AddNewTab()
$vm.ActiveTab.WorkspaceLayout = [OrielFolder.Models.WorkspaceLayoutState]::new()
$vm.ActiveTab.WorkspaceLayout.Add($pane.Explorer, [OrielFolder.Models.ViewMode]::List) | Out-Null
$vm.RunExplorerCleanup()
if ($classic.CleanupCount -ne 1 -or $right.CleanupCount -ne 1 -or $pane.Explorer.CleanupCount -ne 1 -or $vm.ActiveTab.Explorer.CleanupCount -ne 1 -or $vm.ActiveTab.SplitRightExplorer.CleanupCount -ne 1) { throw 'Window cleanup did not dispose every owned explorer exactly once across aliases and tab groups.' }
Write-Output 'PASS: actual window cleanup includes all workspace explorers and deduplicates shared ownership'
