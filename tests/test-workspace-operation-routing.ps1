$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$operations = [IO.File]::ReadAllText((Join-Path $repo 'src\OrielFolder\OrielFolder\ViewModels\MainViewModel.FileOperations.cs'))
$drag = [IO.File]::ReadAllText((Join-Path $repo 'src\OrielFolder\OrielFolder\MainWindow.DragDropHandler.cs'))
function Get-Method([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $open = $source.IndexOf('{', $start)
    $arrow = $source.IndexOf('=>', $start)
    if ($arrow -ge 0 -and $arrow -lt $open) {
        return $source.Substring($start, $source.IndexOf(';', $arrow) + 1 - $start)
    }
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$methods = Get-Method $operations 'public async Task ExecuteFileOperationAsync('
$methods += "`n" + (Get-Method $operations 'private async Task ExecuteViaConcurrentManagerAsync(')
$methods += "`n" + (Get-Method $operations 'private static bool IsOperationCancelled(')
$methods += "`n" + (Get-Method $operations 'private async Task UndoAsync(')
$methods += "`n" + (Get-Method $operations 'private async Task RedoAsync(')
foreach ($signature in @('private static string? GetOperationParentFolder(', 'private string[] CaptureOperationFolders(', 'private async Task RefreshOperationFoldersAsync(', 'public async Task RefreshAffectedFoldersAsync(')) {
    if ($operations.Contains($signature)) { $methods += "`n" + (Get-Method $operations $signature) }
}
$stubs = @'
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
namespace Microsoft.UI.Dispatching {
    public class DispatcherQueue {
        public static DispatcherQueue GetForCurrentThread() => new();
        public bool TryEnqueue(Action callback) { callback(); return true; }
    }
}
namespace Services {
    public enum OperationStatus { Completed, Failed, Cancelled }
    public class FileOperationEntry { public Guid Id = Guid.NewGuid(); public object? DispatcherQueue; public OperationStatus Status; public CancellationTokenSource? CancellationTokenSource; }
}
namespace Models { public class ActionLogEntry { public string OperationType = ""; public string Description = ""; public bool Success; public string? ErrorMessage; } }
public class ActionLog { public void LogOperation(Models.ActionLogEntry value) {} }
public class OperationCompletedEventArgs : EventArgs { public Services.FileOperationEntry Entry = new(); public OperationResult Result = new(); }
public class Manager {
    public event EventHandler<OperationCompletedEventArgs>? OperationCompleted;
    public Services.FileOperationEntry Entry = new();
    public Services.FileOperationEntry StartOperation(IFileOperation op, object queue) { Entry = new(); return Entry; }
    public Task<OperationResult> RunWithIndeterminateProgressAsync(string description, Func<Task<OperationResult>> action, object queue) => action();
    public void Complete(bool success, bool cancelled = false) {
        Entry.Status = cancelled ? Services.OperationStatus.Cancelled : success ? Services.OperationStatus.Completed : Services.OperationStatus.Failed;
        OperationCompleted?.Invoke(this, new OperationCompletedEventArgs { Entry = Entry, Result = new OperationResult { Success = success } });
    }
}
namespace Helpers {
    public static class DebugLogger { public static void Log(string value) {} }
    public static class ArchivePathHelper { public static bool IsArchivePath(string path) => path.StartsWith("archive://"); }
}
public class Folder { public string Path = ""; public string Name = ""; }
public class ExplorerViewModel { public string Name = ""; public List<Folder> Columns = new(); public Folder? CurrentFolder => Columns.LastOrDefault(); }
public class OperationResult { public bool Success = true; public string? ErrorMessage; public List<string> AffectedPaths = new(); }
public class FileOperationProgress {}
public static class FileSystemRouter { public static bool IsRemotePath(string path) => path.Contains("://"); }
public interface IFileOperation { string Description { get; } bool CanUndo { get; } }
public class RenameFileOperation : IFileOperation { public string Description => "Rename"; public bool CanUndo => true; }
public class CopyFileOperation : RenameFileOperation { public List<string> SourcePaths = new(); public string DestinationDirectory = ""; }
public class MoveFileOperation : CopyFileOperation {}
public class DeleteFileOperation : RenameFileOperation { public List<string> SourcePaths = new(); }
public class CompressOperation : RenameFileOperation {}
public class ExtractOperation : RenameFileOperation {}
public class CompletedOperationWrapper : RenameFileOperation { public CompletedOperationWrapper(IFileOperation op, OperationResult result) {} }
public class ProgressStub { public bool IsVisible; public string OperationDescription = ""; public void UpdateProgress(FileOperationProgress value) {} }
public class Loc { public string Get(string value) => value; }
public class History {
    public TaskCompletionSource<OperationResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<OperationResult> ExecuteAsync(IFileOperation op, IProgress<FileOperationProgress> progress) => Completion.Task;
    public Task<OperationResult> ExecuteAsync(IFileOperation op, object? progress, CancellationToken token) => Task.FromResult(new OperationResult());
    public Task<OperationResult> UndoAsync() => Completion.Task;
    public Task<OperationResult> RedoAsync() => Completion.Task;
}
public class Harness {
    public ExplorerViewModel? ActiveExplorer;
    public List<ExplorerViewModel> Owners = new();
    public List<string> Refreshed = new();
    private readonly ProgressStub _progressViewModel = new();
    private readonly Loc _loc = new();
    public readonly History _operationHistory = new();
    public readonly Manager _fileOperationManager = new();
    private readonly ActionLog _actionLogService = new();
    public string? UndoDescription = "rename";
    public string? RedoDescription = "rename";
    public IEnumerable<ExplorerViewModel> EnumerateOwnedExplorers() => Owners;
    private Task<HashSet<int>> RefreshSourceColumnsForMove(MoveFileOperation op, ExplorerViewModel explorer) => Task.FromResult(new HashSet<int>());
    public Task RefreshCurrentFolderAsync(int? column = null, ExplorerViewModel? explorer = null) { Refreshed.Add((explorer ?? ActiveExplorer)!.Name); return Task.CompletedTask; }
    private Task RefreshOppositeExplorerAsync() => Task.CompletedTask;
    private void LogOperationResult(IFileOperation operation, OperationResult result) {}
    private void ShowToast(string value) {}
    private void ShowError(string value) {}
'@
$checks = @'
    public static void CheckPendingFocusChange() {
        var original = new ExplorerViewModel { Name = "initiating", Columns = new() { new Folder { Path = @"D:\source" } } };
        var unrelated = new ExplorerViewModel { Name = "new-focus", Columns = new() { new Folder { Path = @"D:\unrelated" } } };
        var third = new ExplorerViewModel { Name = "third-pane", Columns = new() { new Folder { Path = @"D:\source" } } };
        var vm = new Harness { ActiveExplorer = original, Owners = new() { original, unrelated, third } };
        var pending = vm.ExecuteFileOperationAsync(new RenameFileOperation());
        vm.ActiveExplorer = unrelated;
        vm._operationHistory.Completion.SetResult(new OperationResult { AffectedPaths = new() { @"D:\source\renamed.txt" } });
        pending.GetAwaiter().GetResult();
        if (vm.Refreshed.Contains("new-focus") || !vm.Refreshed.Contains("initiating")) throw new Exception("Changing focus while rename is pending refreshed the wrong owner.");
        if (!vm.Refreshed.Contains("third-pane")) throw new Exception("Another pane showing the affected folder was not refreshed.");
    }
    public static void CheckConcurrentCompletion() {
        foreach (var outcome in new[] { "success", "failure", "cancel" }) {
            var source = new ExplorerViewModel { Name = "source", Columns = new() { new Folder { Path = @"D:\source" } } };
            var destination = new ExplorerViewModel { Name = "third-destination", Columns = new() { new Folder { Path = @"D:\destination" } } };
            var unrelated = new ExplorerViewModel { Name = "new-focus", Columns = new() { new Folder { Path = @"D:\other" } } };
            var vm = new Harness { ActiveExplorer = source, Owners = new() { source, unrelated, destination } };
            vm.ExecuteFileOperationAsync(new MoveFileOperation { SourcePaths = new() { @"D:\source\file.txt" }, DestinationDirectory = @"D:\destination" }).GetAwaiter().GetResult();
            vm.ActiveExplorer = unrelated;
            vm._fileOperationManager.Complete(outcome == "success", outcome == "cancel");
            if (vm.Refreshed.Contains("new-focus") || !vm.Refreshed.Contains("source") || !vm.Refreshed.Contains("third-destination"))
                throw new Exception("Concurrent move " + outcome + " did not refresh its actual source and destination owners.");
        }
        var original = new ExplorerViewModel { Name = "source", Columns = new() { new Folder { Path = @"D:\source" } } };
        var other = new ExplorerViewModel { Name = "new-focus", Columns = new() { new Folder { Path = @"D:\other" } } };
        var deleteVm = new Harness { ActiveExplorer = original, Owners = new() { original, other } };
        var pendingDelete = deleteVm.ExecuteFileOperationAsync(new DeleteFileOperation { SourcePaths = new() { @"D:\source\file.txt" } });
        if (pendingDelete.IsCompleted) throw new Exception("Delete returned before post-operation cleanup.");
        deleteVm.ActiveExplorer = other;
        deleteVm._fileOperationManager.Complete(true);
        pendingDelete.GetAwaiter().GetResult();
        if (deleteVm.Refreshed.Count != 1 || deleteVm.Refreshed[0] != "source") throw new Exception("Delete cleanup followed the new focus.");
    }
    public static void CheckHistoryFocusChange() {
        foreach (bool redo in new[] { false, true }) {
            var original = new ExplorerViewModel { Name = "source", Columns = new() { new Folder { Path = @"D:\source" } } };
            var other = new ExplorerViewModel { Name = "new-focus", Columns = new() { new Folder { Path = @"D:\other" } } };
            var vm = new Harness { ActiveExplorer = original, Owners = new() { original, other } };
            var pending = redo ? vm.RedoAsync() : vm.UndoAsync();
            vm.ActiveExplorer = other;
            vm._operationHistory.Completion.SetResult(new OperationResult { AffectedPaths = new() { @"D:\source\renamed.txt" } });
            pending.GetAwaiter().GetResult();
            if (!vm.Refreshed.Contains("source")) throw new Exception((redo ? "Redo" : "Undo") + " completion forgot the initiating pane after focus changed.");
        }
    }
}
'@
$paneDrop = Get-Method $drag 'private async void OnPaneDrop('
$paneOver = Get-Method $drag 'private void OnPaneDragOver('
$dropStubs = @'
#nullable enable
namespace DropTests {
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UIElement = DropTests.FrameworkElement;
using DataPackageOperation = DropTests.DragDropMode;
public enum DragDropMode { None, Copy, Move, Link }
public class FrameworkElement { public string Name = ""; public ExplorerViewModel? Owner; }
public class DataView { public Dictionary<string, object> Properties = new(); }
public class DragUI { public bool IsCaptionVisible; public bool IsGlyphVisible; }
public class DragEventArgs { public DataView DataView = new(); public DragUI DragUIOverride = new(); public bool Handled; public DragDropMode AcceptedOperation; }
public class Overlay { public double Opacity; }
public class Vm { public ExplorerViewModel? Explorer; public ExplorerViewModel? RightExplorer; public ExplorerViewModel? ActiveExplorer; }
public class PaneHarness {
    public Vm ViewModel = new();
    public Overlay LeftDropOverlay = new(), RightDropOverlay = new();
    public FrameworkElement Content = new();
    public TaskCompletionSource<List<string>> Paths = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<(string, ExplorerViewModel?)> Dropped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ExplorerViewModel? ResolvePaneExplorer(FrameworkElement element) => element.Owner;
    private void HideDragTooltip() {}
    private void ShowShelfForDrag() {}
    private DragDropMode ResolveDragDropMode(DragEventArgs e, string path) => DragDropMode.Move;
    private DragDropMode ToAcceptedOperation(DragDropMode mode) => mode;
    private string GetDragCaption(DragDropMode mode, string path) => path;
    private void UpdateDragTooltip(string caption, DragEventArgs e, FrameworkElement element) {}
    private Task<List<string>> ExtractDropPaths(DragEventArgs e) => Paths.Task;
    private Task HandleDropAsync(List<string> paths, string path, DragDropMode mode, ExplorerViewModel? owner = null) { Dropped.SetResult((path, owner)); return Task.CompletedTask; }
'@
$dropChecks = @'
    public static void CheckThirdPaneDrop() {
        var left = new ExplorerViewModel { Name = "left", Columns = new() { new Folder { Path = @"D:\left" } } };
        var right = new ExplorerViewModel { Name = "right", Columns = new() { new Folder { Path = @"D:\right" } } };
        var third = new ExplorerViewModel { Name = "third", Columns = new() { new Folder { Path = @"D:\third" } } };
        var harness = new PaneHarness { ViewModel = new Vm { Explorer = left, RightExplorer = right, ActiveExplorer = left } };
        var sender = new FrameworkElement { Name = "WorkspacePane", Owner = third };
        harness.OnPaneDrop(sender, new DragEventArgs());
        harness.ViewModel.ActiveExplorer = right;
        harness.Paths.SetResult(new List<string> { @"D:\left\file.txt" });
        if (!harness.Dropped.Task.Wait(TimeSpan.FromSeconds(5))) throw new Exception("Pane drop did not finish.");
        var drop = harness.Dropped.Task.Result;
        if (drop.Item1 != @"D:\third" || drop.Item2 != third) throw new Exception("Third-pane drop was routed to the legacy right pane or lost its captured owner.");
        var over = new DragEventArgs();
        over.DataView.Properties["SourcePane"] = "Left";
        over.DataView.Properties["SourcePaths"] = new List<string> { @"D:\left\file.txt" };
        harness.OnPaneDragOver(sender, over);
        if (over.AcceptedOperation != DragDropMode.Move) throw new Exception("Shared Left drag metadata blocked a different workspace folder.");
        over.DataView.Properties["SourcePane"] = "Right";
        over.DataView.Properties["SourcePaths"] = new List<string> { @"D:\third\file.txt" };
        harness.OnPaneDragOver(sender, over);
        if (over.AcceptedOperation != DragDropMode.None) throw new Exception("A same-folder move must be rejected using actual paths.");
    }
}
}
'@
Add-Type -TypeDefinition ($stubs + "`n" + $methods + "`n" + $checks + "`n" + $dropStubs + "`n" + $paneDrop + "`n" + $paneOver + "`n" + $dropChecks)
[Harness]::CheckPendingFocusChange()
Write-Output 'PASS: pending rename retains its initiating folder and refreshes every matching owner'
[Harness]::CheckConcurrentCompletion()
Write-Output 'PASS: concurrent move success/failure/cancel and awaited delete retain affected owners'
[Harness]::CheckHistoryFocusChange()
Write-Output 'PASS: undo/redo completion retains panes across pending focus changes'
[DropTests.PaneHarness]::CheckThirdPaneDrop()
Write-Output 'PASS: third-pane drops keep the captured owner; same-folder checks use actual paths'
