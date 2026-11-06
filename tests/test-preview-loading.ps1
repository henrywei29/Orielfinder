$ErrorActionPreference = 'Stop'
$view = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Views\DocumentPreviewView.cs'))
$methods = foreach ($name in @('UpdatePreparationProgress', 'HideNativeLoading')) {
    $method = [regex]::Match($view, '(?sm)^    private void ' + $name + '\(.*?^    }').Value
    if (-not $method) { throw "Missing actual native loading method: $name" }
    $method
}
$loadMethod = [regex]::Match($view, '(?sm)^    private async Task LoadAsync\(\).*?^    }').Value
if (-not $loadMethod) { throw 'Missing actual native load method.' }
$hostSource = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
public enum Visibility { Visible, Collapsed }
public enum DocumentPreviewStage { Copying, Checking, Queued, Converting, Ready, OpeningDocument, Exporting }
public sealed record DocumentPreviewProgress(DocumentPreviewStage Stage, long BytesCopied = 0, long TotalBytes = 0, long OutputBytes = 0);
public class TextBlock { public string Text = ""; public Visibility Visibility; }
public class ProgressBar { public double Value, Maximum; public bool IsIndeterminate; public Visibility Visibility; }
public class LoadingHarness {
    public readonly TextBlock _status = new();
    public readonly ProgressBar _progress = new();
    public int _version = 3;
    public bool _unloaded;
    public int _viewerOwnsLoadingVersion = -1;
    private int _preparationProgressVersion = -1;
    private double _preparationProgressValue;
    private bool IsCurrent(int version) => !_unloaded && version == _version;
    public void Update(DocumentPreviewStage stage, long copied, long total, int version) => UpdatePreparationProgress(new(stage, copied, total), version);
    public void UpdateOutput(long bytes, int version) => UpdatePreparationProgress(new(DocumentPreviewStage.Exporting, OutputBytes: bytes), version);
    public void Hide(int version) => HideNativeLoading(version);
'@
$loadHost = @'
public class DocumentPreviewResult : IDisposable {
    public string PdfPath = "preview.pdf", Limitation = "";
    public int Disposals;
    public void Dispose() { Disposals++; }
}
public class ImagePreviewSnapshot : IDisposable {
    public string Path = "image.png";
    public static Task<ImagePreviewSnapshot> CreateAsync(string path, CancellationToken token) => Task.FromResult(new ImagePreviewSnapshot());
    public void Dispose() { }
}
public class PreparationStub {
    public readonly Dictionary<string, TaskCompletionSource<DocumentPreviewResult>> Requests = new();
    public Task<DocumentPreviewResult> PrepareCachedAsync(string path, CancellationToken token, IProgress<DocumentPreviewProgress> progress) {
        var request = new TaskCompletionSource<DocumentPreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Requests.Add(path, request);
        return request.Task;
    }
}
public class LoadHarness {
    public int _version, BrowserStarts, BrowserCloses, Opens;
    public bool _unloaded, _isImage;
    public string SourcePath = "", _sourceForThisLoad = "", CurrentPdfPath;
    public int CurrentPageIndex;
    public Action PageChanged;
    public readonly TextBlock _status = new();
    public readonly ProgressBar _progress = new();
    public readonly PreparationStub _service = new();
    public CancellationTokenSource _loadCancellation;
    public DocumentPreviewResult _currentResult;
    public ImagePreviewSnapshot _currentImageSnapshot;
    public Task _initializeTask;
    public readonly Queue<TaskCompletionSource<bool>> BrowserGates = new();
    public Task Start(string path) { SourcePath = path; return LoadAsync(); }
    private Task InitializeBrowserAsync() { BrowserStarts++; var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); BrowserGates.Enqueue(gate); return gate.Task; }
    private void ReleasePreview() { if (_initializeTask != null) BrowserCloses++; _initializeTask = null; _currentResult?.Dispose(); _currentResult = null; }
    private void UpdatePreparationProgress(DocumentPreviewProgress value, int version) { }
    private void ReportImagePreviewFailure(int version) { }
    private async Task OpenPreviewBrowserAsync(string path, string entry, string fileName, int version) { await _initializeTask; if (version == _version && !_unloaded) Opens++; }
'@
Add-Type -TypeDefinition ($hostSource + ($methods -join "`n") + "`n}`n" + $loadHost + $loadMethod + "`n}") -IgnoreWarnings -WarningAction SilentlyContinue
$harness = [LoadingHarness]::new()
$harness.Update([DocumentPreviewStage]::Copying, 25, 100, 3)
if ($harness._progress.IsIndeterminate -or $harness._progress.Maximum -ne 5 -or $harness._progress.Value -ne 0.25 -or $harness._progress.Visibility -ne 'Visible' -or $harness._status.Text -notmatch '25/100') { throw 'Actual copied-byte fraction within the first preparation step was not displayed.' }
foreach ($stage in @('Checking', 'Queued', 'Converting', 'OpeningDocument', 'Exporting')) {
    $harness.Update([DocumentPreviewStage]::$stage, 0, 0, 3)
    $expected = @{ Checking=1; Queued=2; Converting=2; OpeningDocument=3; Exporting=4 }[$stage]
    if ($harness._progress.IsIndeterminate -or $harness._progress.Value -ne $expected -or $harness._status.Text -match '\d+%') { throw "$stage must show actual completed steps without indeterminate animation or invented Office percentages." }
}
$harness.UpdateOutput(8192, 3)
if ($harness._progress.Value -ne 4 -or $harness._status.Text -notmatch '8,192') { throw 'Actual generated PDF bytes were not displayed.' }
$harness.Update([DocumentPreviewStage]::Ready, 0, 0, 3)
if ($harness._progress.Value -ne 5) { throw 'Completed preparation did not finish all five actual steps.' }
$completedText = $harness._status.Text
$harness.Update([DocumentPreviewStage]::OpeningDocument, 0, 0, 3)
if ($harness._progress.Value -ne 5 -or $harness._status.Text -ne $completedText) { throw 'Delayed worker progress regressed a completed preparation.' }
$harness.Update([DocumentPreviewStage]::Copying, 200, 100, 2)
if ($harness._progress.Value -ne 5) { throw 'Stale progress changed the current load.' }
$harness.Hide(2)
if ($harness._progress.Visibility -ne 'Visible') { throw 'Stale viewer message hid current progress.' }
$harness.Hide(3)
if ($harness._progress.Visibility -ne 'Collapsed' -or $harness._status.Visibility -ne 'Collapsed') { throw 'Viewer ownership did not hide both native loading controls.' }
$harness.Update([DocumentPreviewStage]::Copying, 50, 100, 3)
if ($harness._progress.Visibility -ne 'Collapsed') { throw 'Queued native progress covered the viewer loading bar after viewerReady.' }
$harness._version = 4
$harness.Update([DocumentPreviewStage]::Copying, 50, 100, 4)
if ($harness._progress.Value -ne 0.5 -or $harness._progress.Visibility -ne 'Visible') { throw 'A new load could not own native loading again.' }
$harness._unloaded = $true
$harness.Hide(4)
if ($harness._progress.Visibility -ne 'Visible') { throw 'An unloaded view accepted a viewer message.' }
'PASS: actual five-step preparation and byte counts, no indeterminate Office animation, stale/regressive events rejected, viewer ownership preserved'
$load = [LoadHarness]::new()
$first = $load.Start('first.doc')
if ($load.BrowserStarts -ne 1 -or $first.IsCompleted -or $load._service.Requests.Count -ne 1) { throw 'Browser initialization did not overlap the pending Office conversion.' }
$firstResult = [DocumentPreviewResult]::new()
$load._service.Requests['first.doc'].SetResult($firstResult)
$load.BrowserGates.Dequeue().SetResult($true)
$first.GetAwaiter().GetResult() | Out-Null
if ($load.Opens -ne 1) { throw 'Prepared preview did not open after both independent startup tasks completed.' }
$stale = $load.Start('stale.doc')
$staleBrowser = $load.BrowserGates.Dequeue()
$current = $load.Start('current.doc')
$currentBrowser = $load.BrowserGates.Dequeue()
$staleResult = [DocumentPreviewResult]::new()
$load._service.Requests['stale.doc'].SetResult($staleResult)
$staleBrowser.SetResult($true)
$stale.GetAwaiter().GetResult() | Out-Null
if ($staleResult.Disposals -ne 1 -or $load.Opens -ne 1) { throw 'A stale conversion opened or leaked its private PDF lease.' }
$currentResult = [DocumentPreviewResult]::new()
$load._service.Requests['current.doc'].SetResult($currentResult)
$currentBrowser.SetResult($true)
$current.GetAwaiter().GetResult() | Out-Null
if ($load.Opens -ne 2 -or $firstResult.Disposals -ne 1 -or $currentResult.Disposals -ne 0 -or $load.BrowserCloses -ne 2) { throw 'File switching changed private lease ownership or browser-close safety.' }
'PASS: the actual load method starts browser initialization during conversion, discards stale completions, and retains close-before-lease-release switching'
