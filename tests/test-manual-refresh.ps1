$ErrorActionPreference = 'Stop'
$xaml = [xml][IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\MainWindow.xaml'))
$refresh = $xaml.SelectSingleNode("//*[local-name()='Button' and @AutomationProperties.AutomationId='Button_Refresh']")
$cut = $xaml.SelectSingleNode("//*[local-name()='Button' and @AutomationProperties.AutomationId='Button_Cut']")
if (-not $refresh -or -not [object]::ReferenceEquals($refresh.ParentNode, $cut.ParentNode)) {
    throw 'Refresh must be in the shared Cut/Copy operation bar so split panes can use it.'
}
for ($parent = $refresh.ParentNode; $null -ne $parent; $parent = $parent.ParentNode) {
    if ($parent.Attributes -and $parent.Attributes['Visibility'] -and $parent.Attributes['Visibility'].Value -match 'IsNotSplitVisible|IsSingleNonSettingsVisible') {
        throw 'The refresh operation bar is hidden in split view.'
    }
}
if ($refresh.GetAttribute('AllowFocusOnInteraction') -ne 'False') { throw 'Toolbar refresh must retain the active pane and column focus.' }
if ($refresh.GetAttribute('IsEnabled') -notmatch 'IsFolderRefreshEnabled\(ViewModel.IsSplitViewEnabled, ViewModel.ActivePane, ViewModel.CurrentViewMode, ViewModel.RightViewMode\)') {
    throw 'Refresh availability must track the active split pane, including a folder opposite Home.'
}
Write-Output 'PASS: refresh is beside Cut/Copy and remains available in split view'

$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\MainWindow.FileOperationHandler.cs'))
$handler = [regex]::Match($source, '(?s)        private async void HandleRefresh\(\).*?(?=\r?\n        #endregion)')
if (-not $handler.Success) { throw 'Cannot locate the production refresh handler.' }
$splitSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\MainWindow.SplitPreviewManager.cs'))
$enabledMethod = [regex]::Match($splitSource, '(?s)        public bool IsFolderRefreshEnabled\(.*?\r?\n        \}')
if (-not $enabledMethod.Success) { throw 'Cannot locate the active-pane refresh availability method.' }

# Execute the real handler with synchronous reload collaborators; no WinUI deployment required.
$hostSource = @'
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Span.Models;
using Span.ViewModels;
namespace Span.Models {
    public enum ViewMode { MillerColumns, Details, Home, Settings, ActionLog, RecycleBin }
    public enum ActivePane { Left, Right }
}
namespace Span.Helpers {
    public static class DebugLogger { public static void Log(string message) {} }
}
namespace Span.ViewModels {
    public class FileItem { public string Name = "test.txt"; }
    public class FolderViewModel {
        public List<FileItem> Children = new();
        public FileItem? SelectedChild;
        public int ReloadCount;
        public Task ReloadAsync() { ReloadCount++; return Task.CompletedTask; }
    }
    public class ExplorerViewModel {
        public List<FolderViewModel> Columns = new();
        public FolderViewModel? CurrentFolder;
    }
    public class MainViewModel {
        public bool IsSplitViewEnabled;
        public ActivePane ActivePane;
        public ViewMode CurrentViewMode;
        public ViewMode RightViewMode;
        public ExplorerViewModel ActiveExplorer = new();
    }
}
namespace Span {
    public class MainWindow {
        public MainViewModel ViewModel = new();
        public int FocusedColumn = -1;
        private int GetActiveColumnIndex() => FocusedColumn;
        public void Refresh() => HandleRefresh();
'@
Add-Type -TypeDefinition ($hostSource + "`n" + $handler.Value + "`n" + $enabledMethod.Value + "`n    }`n}")

$window = [Span.MainWindow]::new()
foreach ($case in @(
    @{ Split = $true; Pane = 'Right'; Left = 'Home'; Right = 'MillerColumns'; Expected = $true },
    @{ Split = $true; Pane = 'Left'; Left = 'Home'; Right = 'MillerColumns'; Expected = $false },
    @{ Split = $true; Pane = 'Left'; Left = 'Details'; Right = 'Home'; Expected = $true },
    @{ Split = $true; Pane = 'Right'; Left = 'Details'; Right = 'Home'; Expected = $false },
    @{ Split = $false; Pane = 'Right'; Left = 'MillerColumns'; Right = 'Home'; Expected = $true },
    @{ Split = $false; Pane = 'Left'; Left = 'Settings'; Right = 'Details'; Expected = $false },
    @{ Split = $false; Pane = 'Left'; Left = 'ActionLog'; Right = 'Details'; Expected = $false },
    @{ Split = $false; Pane = 'Left'; Left = 'RecycleBin'; Right = 'Details'; Expected = $false }
)) {
    $actual = $window.IsFolderRefreshEnabled($case.Split, [Span.Models.ActivePane]::$($case.Pane),
        [Span.Models.ViewMode]::$($case.Left), [Span.Models.ViewMode]::$($case.Right))
    if ($actual -ne $case.Expected) { throw "Wrong refresh availability: $($case | ConvertTo-Json -Compress)" }
    Write-Output "PASS: refresh availability split=$($case.Split) pane=$($case.Pane) left=$($case.Left) right=$($case.Right)"
}

foreach ($case in @(
    @{ Name = 'Toolbar focus loss refreshes current directory, not deepest retained column'; Mode = 'MillerColumns'; Focus = -1; Target = 0; Split = $false },
    @{ Name = 'F5 retains focused-column behavior'; Mode = 'MillerColumns'; Focus = 1; Target = 1; Split = $false },
    @{ Name = 'Details view refreshes current directory'; Mode = 'Details'; Focus = -1; Target = 0; Split = $false },
    @{ Name = 'Right active pane uses its own view mode and directory'; Mode = 'Details'; Focus = -1; Target = 0; Split = $true },
    @{ Name = 'Right Miller pane refreshes its focused column'; Mode = 'MillerColumns'; Focus = 1; Target = 1; Split = $true }
)) {
    $window = [Span.MainWindow]::new()
    $window.ViewModel.CurrentViewMode = [Span.Models.ViewMode]::MillerColumns
    if ($case.Split) {
        $window.ViewModel.IsSplitViewEnabled = $true
        $window.ViewModel.ActivePane = [Span.Models.ActivePane]::Right
        $window.ViewModel.RightViewMode = [Span.Models.ViewMode]::$($case.Mode)
    } else { $window.ViewModel.CurrentViewMode = [Span.Models.ViewMode]::$($case.Mode) }
    $explorer = $window.ViewModel.ActiveExplorer
    1..3 | ForEach-Object { $explorer.Columns.Add([Span.ViewModels.FolderViewModel]::new()) }
    $explorer.CurrentFolder = $explorer.Columns[0]
    $window.FocusedColumn = $case.Focus
    $selected = [Span.ViewModels.FileItem]::new()
    $target = $explorer.Columns[$case.Target]
    $target.Children.Add($selected)
    $target.SelectedChild = $selected
    $window.Refresh()
    if ($target.ReloadCount -ne 1 -or ($explorer.Columns.ReloadCount | Measure-Object -Sum).Sum -ne 1) {
        throw "$($case.Name): refreshed the wrong column."
    }
    if ($explorer.Columns.Count -ne 3 -or -not [object]::ReferenceEquals($selected, $target.SelectedChild)) {
        throw "$($case.Name): expanded columns or current selection changed."
    }
    Write-Output "PASS: $($case.Name)"
}
