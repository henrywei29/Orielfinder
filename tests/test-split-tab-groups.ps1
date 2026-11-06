$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
$tabSource = [IO.File]::ReadAllText((Join-Path $root 'ViewModels\MainViewModel.TabManagement.cs'))
$vmSource = [IO.File]::ReadAllText((Join-Path $root 'ViewModels\MainViewModel.cs'))
$windowSource = [IO.File]::ReadAllText((Join-Path $root 'MainWindow.xaml.cs'))
$methods = foreach ($name in @('AddNewTab', 'SwitchToTab', 'SaveActiveTabState', 'CloseTab', 'NotifySplitViewChanged', 'NotifyViewModeChanged')) {
    $match = [regex]::Match($tabSource, '(?sm)^        public void ' + $name + '\(.*?^        }')
    if (-not $match.Success) { throw "Cannot locate production method $name." }
    $match.Value
}
$rightProperty = [regex]::Match($vmSource, '(?sm)^        public ExplorerViewModel RightExplorer\r?\n.*?^        }')
$layout = [regex]::Match($windowSource, '(?sm)^        private void UpdateViewModeVisibility\(\).*?^        }')
$rightRebind = [regex]::Match($windowSource, '(?sm)^        private void ResubscribeRightExplorer\(\).*?^        }')
if (-not $rightProperty.Success -or -not $layout.Success) { throw 'Cannot locate production group collaborators.' }

# Execute production switching, saving, closing and layout methods; stub WinUI/services only.
$hostPrefix = @'
#nullable enable annotations
#nullable disable warnings
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using OrielFolder.Models;
using OrielFolder.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
namespace Microsoft.UI.Xaml { public enum Visibility { Visible, Collapsed } }
namespace Microsoft.Extensions.DependencyInjection {
    public static class Services { public static T GetService<T>(this object services) where T:new() => new(); }
}
namespace OrielFolder.Services {
    public class LocalizationService { public string Get(string key) => key; }
    public class SettingsService {
        public static int RightStartupMode;
        public int Tab2StartupViewMode => RightStartupMode;
        public int Tab2StartupBehavior => 0;
    }
}
namespace OrielFolder.Helpers {
    public static class DebugLogger { public static void Log(string text) {} }
    public static class ViewModeExtensions { public static bool IsIconMode(ViewMode mode) => mode == ViewMode.IconMedium || mode == ViewMode.IconSmall || mode == ViewMode.IconLarge; }
}
namespace OrielFolder {
    public class App { public static App Current = new(); public object Services = new(); }
}
namespace OrielFolder.Models {
    public enum ViewMode { Home, MillerColumns, Details, List, IconMedium, Settings, ActionLog, RecycleBin, IconSmall, IconLarge, IconExtraLarge }
    public enum ActivePane { Left, Right }
    public class FolderItem { public string Name = ""; public string Path = ""; }
    public partial class TabItem {
        public string Id = Guid.NewGuid().ToString();
        public string Header = "";
        public string Path = "";
        public ViewMode ViewMode;
        public ViewMode IconSize;
        public bool IsActive;
        public bool IsSplitEnabled;
        public ViewMode SplitRightViewMode;
        public OrielFolder.ViewModels.ExplorerViewModel? Explorer;
        public OrielFolder.ViewModels.ExplorerViewModel? SplitRightExplorer;
        public ActivePane SplitActivePane;
        public ViewMode? SplitRightPreferredViewMode;
    }
}
namespace OrielFolder.ViewModels {
    public class FolderViewModel { public bool IsActive; }
    public class ExplorerViewModel {
        public ExplorerViewModel() {}
        public ExplorerViewModel(FolderItem root, object service) {}
        public bool EnableAutoNavigation;
        public string CurrentPath = "";
        public string CurrentFolderName => CurrentPath;
        public System.Collections.ObjectModel.ObservableCollection<FolderViewModel> Columns = new();
        public PropertyChangedEventHandler? PropertyChanged;
        public Action? NavigationError;
        public Action? PathHighlightsUpdated;
        public int CleanupCount;
        public long TabSwitchSuppressionTicks;
        public void Cleanup() { CleanupCount++; }
        public Task NavigateToPath(string path) { CurrentPath = path; return Task.CompletedTask; }
        public void SetActiveColumn(FolderViewModel folder) { folder.IsActive = true; }
        public void PublishPathChange() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CurrentPath"));
    }
    public partial class MainViewModel {
        public List<TabItem> Tabs = new();
        private int _activeTabIndex = -1;
        public int ActiveTabIndex { get => _activeTabIndex; set => _activeTabIndex = value; }
        public TabItem? ActiveTab => _activeTabIndex >= 0 && _activeTabIndex < Tabs.Count ? Tabs[_activeTabIndex] : null;
        public bool IsSwitchingTab;
        public bool IsRightPreviewEnabled;
        private ExplorerViewModel _leftExplorer = new();
        private ExplorerViewModel _rightExplorer = new();
        public ExplorerViewModel LeftExplorer { get => _leftExplorer; set => _leftExplorer = value; }
        public ExplorerViewModel Explorer => _leftExplorer;
        public ExplorerViewModel ActiveExplorer => ActivePane == ActivePane.Left ? LeftExplorer : RightExplorer;
        private bool _isSplitViewEnabled;
        public bool IsSplitViewEnabled { get => _isSplitViewEnabled; set => _isSplitViewEnabled = value; }
        private ViewMode _currentViewMode;
        public ViewMode CurrentViewMode { get => _currentViewMode; set => _currentViewMode = value; }
        public ViewMode _leftViewMode;
        public ViewMode LeftViewMode => _leftViewMode;
        private ViewMode _rightViewMode;
        public ViewMode RightViewMode { get => _rightViewMode; set => _rightViewMode = value; }
        private ViewMode _currentIconSize;
        public ViewMode CurrentIconSize { get => _currentIconSize; set => _currentIconSize = value; }
        private ActivePane _activePane;
        public ActivePane ActivePane { get => _activePane; set => _activePane = value; }
        public ViewMode? _rightPreferredViewMode;
        public PropertyChangedEventHandler? _rightExplorerPropertyChangedHandler;
        public event EventHandler? LastTabClosed;
        private object _fileService = new();
        private string HomeLabel => "Home";
        public List<string> Notifications = new();
        public int RecentEvents;
        public void EnableRecentProbe() {
            _rightExplorerPropertyChangedHandler = (sender, args) => RecentEvents++;
            RightExplorer.PropertyChanged += _rightExplorerPropertyChangedHandler;
        }
        private void OnPropertyChanged(string name) { Notifications.Add(name); }
        private bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = "") {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value; OnPropertyChanged(name); return true;
        }
        private bool ShouldAutoNavigate(ViewMode mode) => mode == ViewMode.MillerColumns;
        private void OnLeftExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e) {}
        private void UpdateStatusBar() {}
        private void SyncNavigationHistoryState() {}
        private Task InitializeTabExplorerAsync(TabItem tab) => Task.CompletedTask;
        private Task LoadDeferredTabPathAsync(TabItem tab) => Task.CompletedTask;
'@
$hostWindow = @'
    }
}
namespace OrielFolder {
    public class FilterBar { public Microsoft.UI.Xaml.Visibility Visibility; }
    public class FolderView {
        public ViewModels.ExplorerViewModel? ViewModel;
        public ViewMode LastIconMode;
        public int IconUpdates;
        public void UpdateIconSize(ViewMode mode) { LastIconMode = mode; IconUpdates++; }
    }
    public class PreviewView { public void StopMedia() {} }
    public class MainWindow {
        public ViewModels.MainViewModel ViewModel = new();
        private bool _isClosed = false;
        private ViewModels.ExplorerViewModel? _subscribedRightExplorer;
        public FolderView DetailsViewRight = new();
        public FolderView ListViewRight = new();
        public FolderView IconViewRight = new();
        public PreviewView RightPreviewPanel = new();
        public bool _suppressFocusOnViewModeChange;
        private ViewMode _previousViewMode = ViewMode.MillerColumns;
        public FilterBar LeftFilterBar = new();
        public int LayoutUpdates;
        public bool LastSplit;
        private void SetViewModeVisibility(ViewMode mode) { LayoutUpdates++; LastSplit = ViewModel.IsSplitViewEnabled; }
        private void CloseFilterBar() { LeftFilterBar.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed; }
        private void UpdateSplitViewButtonState() {}
        private void SyncWorkspaceLayoutForActiveTab() {}
        private void UnsubscribeRightExplorerForAddressBar() {}
        private void SubscribeRightExplorerForAddressBar() {}
        private void UnsubscribePreviewSelection(bool isLeft) {}
        private void SubscribePreviewToLastColumn(bool isLeft) {}
        private void ResubscribeGitStatusBar(bool isLeft) {}
        private void SyncRightAddressBar() {}
        private void OnRightColumnsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) {}
        private void OnRightColumnsChangedForPreview(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) {}
        private void OnNavigationError() {}
        private void OnPathHighlightsUpdated() {}
        public void RebindRight() => ResubscribeRightExplorer();
        public void ApplyLayout() => UpdateViewModeVisibility();
'@
$adapterSource = [regex]::Replace([IO.File]::ReadAllText((Join-Path $root 'ViewModels\MainViewModel.MultiPane.cs')), '(?m)^using .*;\r?\n', '')
$paneModelSource = [IO.File]::ReadAllText((Join-Path $root 'Models\WorkspacePaneState.cs'))
$tabModelSource = [IO.File]::ReadAllText((Join-Path $root 'Models\TabItem.MultiPane.cs'))
$geometrySource = [IO.File]::ReadAllText((Join-Path $root 'Models\PaneLayoutGeometry.cs')).Replace('namespace OrielFolder.Models;', 'namespace OrielFolder.Models {') + "`n}"
$geometrySource = [regex]::Replace($geometrySource, '(?m)^using .*;\r?\n', '')
Add-Type -TypeDefinition ($hostPrefix + $rightProperty.Value + ($methods -join "`n") + $hostWindow + $layout.Value + $rightRebind.Value + "`n    }`n}" + $geometrySource + $paneModelSource + $tabModelSource + $adapterSource)

$vm = [OrielFolder.ViewModels.MainViewModel]::new()
$vm.AddNewTab()
$vm.CurrentViewMode = [OrielFolder.Models.ViewMode]::MillerColumns
$vm.ActiveTab.ViewMode = $vm.CurrentViewMode
$vm.LeftExplorer.CurrentPath = 'A-left'
$rightA = $vm.RightExplorer
$rightA.CurrentPath = 'A-right'
$vm.IsSplitViewEnabled = $true
$vm.RightViewMode = [OrielFolder.Models.ViewMode]::Details
$vm.ActivePane = [OrielFolder.Models.ActivePane]::Right
$vm.AddNewTab()
if ($vm.IsSplitViewEnabled -or $vm.ActivePane -ne [OrielFolder.Models.ActivePane]::Left) { throw 'A new single-folder group inherited the previous active right pane or split state.' }
if ([object]::ReferenceEquals($rightA, $vm.RightExplorer)) { throw 'New groups share the right explorer.' }
$rightB = $vm.RightExplorer
$rightB.CurrentPath = 'B-right'
$vm.CurrentViewMode = [OrielFolder.Models.ViewMode]::MillerColumns
$vm.ActiveTab.ViewMode = $vm.CurrentViewMode
$vm.LeftExplorer.CurrentPath = 'B-left'
$vm.IsSplitViewEnabled = $true
$vm.RightViewMode = [OrielFolder.Models.ViewMode]::List
$vm.SwitchToTab(0)
if (-not $vm.IsSplitViewEnabled -or -not [object]::ReferenceEquals($rightA, $vm.RightExplorer) -or $vm.RightExplorer.CurrentPath -ne 'A-right' -or $vm.LeftExplorer.CurrentPath -ne 'A-left') { throw 'Returning to group A did not restore its independent split folders.' }
if ($vm.RightViewMode -ne [OrielFolder.Models.ViewMode]::Details -or $vm.ActivePane -ne [OrielFolder.Models.ActivePane]::Right) { throw 'Group A lost its right view mode or active pane.' }
Write-Output 'PASS: new single-folder groups preserve the original independent split group'
$vm.SwitchToTab(1)
if (-not [object]::ReferenceEquals($rightB, $vm.RightExplorer) -or $vm.RightViewMode -ne [OrielFolder.Models.ViewMode]::List) { throw 'Group B did not restore its own right pane.' }
$vm.IsSplitViewEnabled = $false
$vm.SwitchToTab(0)
if (-not $vm.IsSplitViewEnabled) { throw 'Disabling split in B affected A.' }
$vm.SwitchToTab(1)
if ($vm.IsSplitViewEnabled -or $vm.ActivePane -ne [OrielFolder.Models.ActivePane]::Left) { throw 'Standalone-folder group B did not stay independent.' }
Write-Output 'PASS: standalone and split groups keep separate layouts and right view modes'
$vm.SwitchToTab(0)
$vm.CloseTab(1)
if ($rightB.CleanupCount -ne 1 -or $rightA.CleanupCount -ne 0) { throw 'Closing an inactive group did not clean only its own right explorer.' }
Write-Output 'PASS: closing a group leaves the other group intact'

$window = [OrielFolder.MainWindow]::new()
$window.ViewModel.CurrentViewMode = [OrielFolder.Models.ViewMode]::MillerColumns
$window.ViewModel.IsSplitViewEnabled = $true
$window.ApplyLayout()
if ($window.LayoutUpdates -ne 1 -or -not $window.LastSplit) { throw 'Same-mode tab switching skipped split layout restoration.' }
if (-not $window.ViewModel.Notifications.Contains('IsSplitViewEnabled') -or -not $window.ViewModel.Notifications.Contains('RightViewMode')) { throw 'Split and right-view bindings were not notified.' }
Write-Output 'PASS: switching groups with the same view mode reapplies split layout and bindings'

$vm = [OrielFolder.ViewModels.MainViewModel]::new()
$vm.AddNewTab()
$vm.CurrentViewMode = [OrielFolder.Models.ViewMode]::MillerColumns
$vm.IsSplitViewEnabled = $true
$vm._rightPreferredViewMode = [OrielFolder.Models.ViewMode]::Details
$vm.EnableRecentProbe()
$rightA = $vm.RightExplorer
$vm.AddNewTab()
$rightB = $vm.RightExplorer
$rightA.PublishPathChange()
$rightB.PublishPathChange()
if ($vm.RecentEvents -ne 1) { throw 'Right explorer subscriptions leaked across groups.' }
$vm.CloseTab(1)
if (-not [object]::ReferenceEquals($rightA, $vm.RightExplorer) -or -not $vm.IsSplitViewEnabled -or $rightB.CleanupCount -ne 1 -or $vm._rightPreferredViewMode -ne [OrielFolder.Models.ViewMode]::Details) { throw 'Closing the active group failed to restore the previous split group.' }
$rightA.PublishPathChange()
$rightB.PublishPathChange()
if ($vm.RecentEvents -ne 2) { throw 'Right explorer subscriptions were duplicated or left on the closed group.' }
Write-Output 'PASS: active-group close restores the previous group and moves subscriptions exactly once'

$special = [OrielFolder.Models.TabItem]::new()
$special.ViewMode = [OrielFolder.Models.ViewMode]::Settings
$vm.Tabs.Add($special)
$vm.SwitchToTab(1)
if ($vm.IsSplitViewEnabled -or $vm.ActivePane -ne [OrielFolder.Models.ActivePane]::Left) { throw 'Settings inherited a split pane.' }
$vm.SwitchToTab(0)
if (-not $vm.IsSplitViewEnabled -or -not [object]::ReferenceEquals($rightA, $vm.RightExplorer)) { throw 'Settings tab changed the folder group.' }
Write-Output 'PASS: settings stays standalone without changing the folder group'

[OrielFolder.Services.SettingsService]::RightStartupMode = 1
$vm = [OrielFolder.ViewModels.MainViewModel]::new()
$vm.AddNewTab()
if ($vm.RightViewMode -ne [OrielFolder.Models.ViewMode]::Home -or $vm._rightPreferredViewMode -ne [OrielFolder.Models.ViewMode]::Details) { throw 'A new group lost the configured right Home-to-folder view preference.' }
$vm.AddNewTab()
$vm.SwitchToTab(0)
if ($vm._rightPreferredViewMode -ne [OrielFolder.Models.ViewMode]::Details) { throw 'Switching groups lost the right Home startup preference.' }
Write-Output 'PASS: new groups retain configured right Home-to-folder view preference'

$window = [OrielFolder.MainWindow]::new()
$window.ViewModel.RightViewMode = [OrielFolder.Models.ViewMode]::IconLarge
$window.RebindRight()
if ($window.IconViewRight.LastIconMode -ne [OrielFolder.Models.ViewMode]::IconLarge -or $window.IconViewRight.IconUpdates -ne 1) { throw 'Rebinding a group did not restore its right icon size.' }
Write-Output 'PASS: rebinding a split group restores its right icon size'
