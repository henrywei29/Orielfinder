$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.FileOperationHandler.cs'))
$navigation = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.NavigationManager.cs'))
function Read-Method([string]$text, [string]$name) {
    [regex]::Match($text, '(?sm)^        private [^\r\n]+ ' + $name + '\([^\r\n]*\).*?^        }').Value
}
$methods = (Read-Method $source 'GetActiveColumnPath') + (Read-Method $source 'HandleNewFolder') +
    (Read-Method $source 'GetNewItemTargetFolder') + (Read-Method $navigation 'GetCurrentColumnIndex')
$hostSource = @'
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OrielFolder.ViewModels;
namespace OrielFolder.Helpers {
    public static class ArchivePathHelper { public static bool IsArchivePath(string path) => false; }
    public static class DebugLogger { public static void Log(string text) {} }
}
namespace OrielFolder.Services {
    public class FileSystemRouter { public static bool IsRemotePath(string path) => false; }
    namespace FileOperations {
        public class NewFolderOperation {
            public string Path;
            public NewFolderOperation(string path, FileSystemRouter router) { Path = path; }
        }
    }
}
namespace OrielFolder.ViewModels {
    public class FileSystemViewModel {
        public string Path = "";
        public string Name => System.IO.Path.GetFileName(Path);
        public bool IsRenaming;
        public void BeginRename() { IsRenaming = true; }
    }
    public class FolderViewModel : FileSystemViewModel {
        public List<FileSystemViewModel> Children = new();
        public FileSystemViewModel? SelectedChild;
        public bool IsActive;
        public bool HasMultiSelection;
    }
    public class ExplorerViewModel {
        public List<FolderViewModel> Columns = new();
        public bool EnableAutoNavigation = true;
        public bool IsColorLabelCategory;
        public string CurrentPath = "";
        public FolderViewModel? CurrentFolder => Columns.LastOrDefault(c => c.Path == CurrentPath) ?? Columns.LastOrDefault();
        public IDisposable SuppressAutoNavigationScope() => new Scope();
        private class Scope : IDisposable { public void Dispose() {} }
    }
}
namespace OrielFolder {
    public enum ViewMode { MillerColumns, Details, List, IconMedium }
    public enum ActivePane { Left, Right }
    public class App {
        public static App Current = new(); public App Services => this;
        public T GetRequiredService<T>() where T : new() => new();
    }
    public class Loc { public string Get(string key) => "Created folder"; }
    public class Vm {
        public bool IsSplitViewEnabled;
        public ActivePane ActivePane;
        public ViewMode CurrentViewMode, RightViewMode;
        public ExplorerViewModel ActiveExplorer = new();
        public string? CreatedPath;
        public ExplorerViewModel? OperationOwner;
        public TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldOperation;
        public void ShowToast(string text) {}
        public async Task ExecuteFileOperationAsync(Services.FileOperations.NewFolderOperation op, int? index,
            ExplorerViewModel? initiatingExplorer = null) {
            OperationOwner = initiatingExplorer ?? ActiveExplorer;
            CreatedPath = op.Path;
            Directory.CreateDirectory(op.Path);
            var parent = OperationOwner.Columns.Single(c => c.Path == System.IO.Path.GetDirectoryName(op.Path));
            parent.Children.Add(new FolderViewModel { Path = op.Path });
            if (HoldOperation) await Gate.Task;
        }
    }
    public class MainWindow {
        public Vm ViewModel = new();
        public int FocusedIndex = -1;
        private Loc _loc = new();
        private bool _renamePendingFocus;
        private string? _renameTargetPath;
        private int _renameSelectionCycle;
        private int GetActiveColumnIndex() => FocusedIndex;
        private object? GetItemsHostForViewMode(ViewMode mode, int index) => null;
        private Task FocusRenameTextBoxWithPollingAsync(object host, FileSystemViewModel item) => Task.CompletedTask;
        public string? TargetPath() => GetActiveColumnPath();
        public void CreateFolder() => HandleNewFolder();
'@
$checks = @'
        public static MainWindow Sample(string root) {
            var parent = new FolderViewModel { Path = root, IsActive = true };
            var child = new FolderViewModel { Path = System.IO.Path.Combine(root, "Child") };
            parent.SelectedChild = child;
            var window = new MainWindow();
            window.ViewModel.IsSplitViewEnabled = true;
            window.ViewModel.ActiveExplorer.Columns.AddRange(new[] { parent, child });
            window.ViewModel.ActiveExplorer.CurrentPath = child.Path;
            return window;
        }
        public static void Check(string root, string scenario) {
            var w = Sample(root);
            var columns = w.ViewModel.ActiveExplorer.Columns;
            string expected = System.IO.Path.Combine(root, "Child");
            switch (scenario) {
                case "right pane toolbar": w.ViewModel.ActivePane = ActivePane.Right; break;
                case "focused selected folder": w.FocusedIndex = 0; break;
                case "focused empty child": w.FocusedIndex = 1; break;
                case "selection navigation still pending":
                    columns.RemoveAt(1); w.ViewModel.ActiveExplorer.CurrentPath = root; break;
                case "double click unopened selection":
                    columns.RemoveAt(1); w.ViewModel.ActiveExplorer.CurrentPath = root;
                    w.ViewModel.ActiveExplorer.EnableAutoNavigation = false; expected = root; break;
                case "multiple selection cancels opening":
                    columns.RemoveAt(1); columns[0].HasMultiSelection = true;
                    w.ViewModel.ActiveExplorer.CurrentPath = root; expected = root; break;
                case "multiple selection retains old child column":
                    columns[0].HasMultiSelection = true; expected = root; break;
                case "remembered empty parent":
                    columns[0].SelectedChild = null;
                    expected = root; break;
                case "active parent overrides stale right focus":
                    w.FocusedIndex = 1; columns[0].SelectedChild = null;
                    expected = root; break;
                case "active file selection overrides stale right focus":
                    w.FocusedIndex = 1;
                    columns[0].SelectedChild = new FileSystemViewModel { Path = System.IO.Path.Combine(root,"note.txt") };
                    expected = root; break;
                case "active child overrides stale parent focus":
                    w.FocusedIndex = 0; columns[0].IsActive = false; columns[1].IsActive = true;
                    columns[0].SelectedChild = null; break;
                case "ancestor file keeps expanded child":
                    columns[0].SelectedChild = new FileSystemViewModel { Path = System.IO.Path.Combine(root,"note.txt") };
                    w.ViewModel.ActiveExplorer.CurrentPath = root; expected = root; break;
                case "no active or selection":
                    columns[0].SelectedChild = null; columns[0].IsActive = false; break;
                case "details selected unopened folder":
                    w.ViewModel.CurrentViewMode = ViewMode.Details;
                    w.ViewModel.ActiveExplorer.CurrentPath = root; expected = root; break;
                case "right details":
                    w.ViewModel.ActivePane = ActivePane.Right; w.ViewModel.RightViewMode = ViewMode.Details;
                    w.ViewModel.ActiveExplorer.CurrentPath = root; expected = root; break;
            }
            if (w.TargetPath() != expected) throw new Exception(scenario + ": new item targeted parent/another column instead of the intended folder.");
        }
        public static void CheckCreation(string root, bool switchFocus, bool staleRightFocus) {
            var w = Sample(root);
            var owner = w.ViewModel.ActiveExplorer;
            if (staleRightFocus) { w.FocusedIndex = 1; owner.Columns[0].SelectedChild = null; }
            var targetColumn = owner.Columns[staleRightFocus ? 0 : 1];
            w.ViewModel.HoldOperation = switchFocus;
            w.CreateFolder();
            var expected = System.IO.Path.Combine(targetColumn.Path, "Created folder");
            if (w.ViewModel.CreatedPath != expected || !Directory.Exists(expected))
                throw new Exception("New-folder action physically created in the wrong directory.");
            if (switchFocus) {
                var unrelated = Sample(System.IO.Path.Combine(root, "Other")).ViewModel.ActiveExplorer;
                w.ViewModel.ActiveExplorer = unrelated;
                w.ViewModel.Gate.SetResult();
                Thread.Sleep(300);
                if (targetColumn.SelectedChild != null || w._renameTargetPath != null)
                    throw new Exception("Pending creation stole selection/rename after switching panes.");
                if (w.ViewModel.OperationOwner != owner) throw new Exception("Creation forgot its initiating explorer.");
            } else {
                if (!SpinWait.SpinUntil(() => targetColumn.Children.Any(c => c.IsRenaming), 2000))
                    throw new Exception("Created folder did not enter inline rename in the correct pane.");
            }
        }
    }
}
'@
Add-Type -TypeDefinition ($hostSource + $methods + $checks) -IgnoreWarnings -WarningAction SilentlyContinue
$fixture = Join-Path 'D:\codex\Orielfolder-development\manual-data' ('new-item-target-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$failures = @()
foreach ($scenario in @('left pane toolbar','right pane toolbar','focused selected folder','focused empty child',
    'selection navigation still pending','double click unopened selection','multiple selection cancels opening',
    'multiple selection retains old child column','remembered empty parent',
    'active parent overrides stale right focus','active file selection overrides stale right focus',
    'active child overrides stale parent focus',
    'ancestor file keeps expanded child','no active or selection','details selected unopened folder','right details')) {
    try { [OrielFolder.MainWindow]::Check($fixture, $scenario); "PASS: $scenario" }
    catch { $failures += $_.Exception.InnerException.Message }
}
foreach ($changeFocus in @($false,$true)) {
  foreach ($staleFocus in @($false,$true)) {
    try { [OrielFolder.MainWindow]::CheckCreation((Join-Path $fixture "create-$changeFocus-$staleFocus"), $changeFocus, $staleFocus); "PASS: real directory creation, switch focus=$changeFocus, stale right focus=$staleFocus" }
    catch { $failures += $_.Exception.InnerException.Message }
  }
}
if ($failures.Count) { throw ($failures -join [Environment]::NewLine) }
