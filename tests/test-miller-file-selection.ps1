$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\ViewModels\ExplorerViewModel.cs'
$source = [System.IO.File]::ReadAllText($sourcePath)
$match = [regex]::Match($source,
    '(?s)        private void HandleFileSelection\(FileViewModel fileVm, int nextIndex\).*?(?=\r?\n        /// <summary>)')
if (-not $match.Success) { throw 'Cannot locate the production file-selection handler.' }
$folderProperty = [regex]::Match($source,
    '(?s)        public FolderViewModel\? CurrentFolder =>.*?;')
if (-not $folderProperty.Success) { throw 'Cannot locate the production current-folder property.' }

# Compile the actual handler, with a small host for its WinUI-dependent collaborators.
# No copy of the selection algorithm is maintained in this test.
$hostPrefix = @'
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace OrielFolder.Helpers {
    public static class DebugLogger { public static void Log(string message) {} }
    public static class ArchivePathHelper {
        public static bool IsBrowsableArchive(string path) => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }
}
namespace OrielFolder.ViewModels {
    public sealed class FileViewModel {
        public string Path { get; set; } = string.Empty;
        public string Name => System.IO.Path.GetFileName(Path);
    }
    public sealed class FolderViewModel {
        public string Path { get; set; } = string.Empty;
    }
    public sealed class ExplorerViewModel {
        public List<FolderViewModel> Columns = new List<FolderViewModel>();
        public string CurrentPath = string.Empty;
        public FileViewModel? SelectedFile;
        public int HighlightUpdates;
        public int ArchiveRequests;
        public int ShortcutRequests;
        private void RemoveColumnsFrom(int index) => Columns.RemoveRange(index, Columns.Count - index);
        private void UpdatePathHighlights() => HighlightUpdates++;
        private Task NavigateIntoArchiveAsync(FileViewModel file, int index) { ArchiveRequests++; return Task.CompletedTask; }
        private Task HandleLnkSelectionAsync(FileViewModel file, int index) { ShortcutRequests++; return Task.CompletedTask; }
        public void SelectFile(FileViewModel file, int nextIndex) => HandleFileSelection(file, nextIndex);
'@
$hostSuffix = "`n    }`n}"
Add-Type -TypeDefinition ($hostPrefix + "`n" + $folderProperty.Value + "`n" + $match.Value + $hostSuffix)

$cases = @(
    @{ Name = 'Ancestor column retains all expanded folders'; Columns = @('D:\', 'D:\app', 'D:\app\config'); Index = 1; Path = 'D:\resinfo' },
    @{ Name = 'Middle column retains its expanded child'; Columns = @('D:\', 'D:\app', 'D:\app\config'); Index = 2; Path = 'D:\app\notes.txt' },
    @{ Name = 'Last-column selection still updates preview'; Columns = @('D:\', 'D:\app'); Index = 2; Path = 'D:\app\notes.txt' }
)
foreach ($case in $cases) {
    $explorer = [OrielFolder.ViewModels.ExplorerViewModel]::new()
    foreach ($path in $case.Columns) {
        $folder = [OrielFolder.ViewModels.FolderViewModel]::new()
        $folder.Path = $path
        $explorer.Columns.Add($folder)
    }
    if ($explorer.CurrentFolder.Path -ne $case.Columns[-1]) {
        throw "$($case.Name): the initial current-folder fallback changed."
    }
    $selected = [OrielFolder.ViewModels.FileViewModel]::new()
    $selected.Path = $case.Path
    $explorer.SelectFile($selected, $case.Index)
    if (($explorer.Columns.Path -join '|') -ne ($case.Columns -join '|')) {
        throw "$($case.Name): selecting a regular file removed expanded folder columns."
    }
    if (-not [object]::ReferenceEquals($selected, $explorer.SelectedFile)) {
        throw "$($case.Name): the clicked file was not selected for preview."
    }
    if ($explorer.CurrentPath -ne [System.IO.Path]::GetDirectoryName($case.Path)) {
        throw "$($case.Name): the active path no longer identifies the selected file's parent."
    }
    if ($explorer.CurrentFolder.Path -ne $explorer.CurrentPath) {
        throw "$($case.Name): flat views would show the retained child instead of the active directory."
    }
    if ($explorer.HighlightUpdates -ne 1) { throw "$($case.Name): selection highlights were not updated." }
    Write-Output "PASS: $($case.Name)"
}

# Archive browsing and shortcut resolution retain their original dispatch behavior.
foreach ($extension in @('.zip', '.lnk')) {
    $explorer = [OrielFolder.ViewModels.ExplorerViewModel]::new()
    $selected = [OrielFolder.ViewModels.FileViewModel]::new()
    $selected.Path = 'D:\sample' + $extension
    $explorer.SelectFile($selected, 0)
    if ($explorer.ArchiveRequests + $explorer.ShortcutRequests -ne 1 -or $explorer.SelectedFile) {
        throw "Special navigation dispatch changed for $extension."
    }
    Write-Output "PASS: existing $extension navigation dispatch"
}
