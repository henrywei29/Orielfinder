$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Span\Span'
$folderSource = [IO.File]::ReadAllText((Join-Path $root 'ViewModels\FolderViewModel.cs'))
$widthSource = [IO.File]::ReadAllText((Join-Path $root 'MainWindow.DragDropHandler.cs'))
$load = [regex]::Match($folderSource, '(?sm)^        public async Task EnsureChildrenLoadedAsync\(\).*?^        }').Value
$width = [regex]::Match($widthSource, '(?sm)^        private double MeasureColumnContentWidth\(.*?^        }').Value
if (-not $load -or -not $width) { throw 'Cannot locate production folder loading/width methods.' }

# Execute production methods. Replace only disk latency, WinUI text measurement and app services.
$stubs = @'
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Span.Helpers {
    public static class DebugLogger { public static void Log(string message) {} }
    public static class ArchivePathHelper { public static bool IsArchivePath(string path) => path == "archive"; }
    public static class UncPathHelper {
        public static bool IsShellNamespaceRoot(string path) => path == "wsl";
        public static bool IsServerRoot(string path) => path == "server";
    }
}
namespace Span.Services {
    public class SettingsService { public bool ShowHiddenFiles = true; }
    public static class FileSystemRouter { public static bool IsRemotePath(string path) => path == "remote"; }
    public class FolderContentCache {
        public class CachedFolder {}
        public readonly ManualResetEventSlim Entered = new(false), Release = new(true);
        public CachedFolder? Value;
        public bool Hidden;
        public CachedFolder? TryGet(string path, bool hidden) {
            Hidden = hidden; Entered.Set(); Release.Wait(); return Value;
        }
    }
}
namespace Span {
    public class App {
        public static App Current = new();
        public ServiceProvider Services = new();
    }
    public class ServiceProvider {
        public Services.FolderContentCache Cache = new();
        public object? GetService(Type type) => type == typeof(Services.SettingsService) ? new Services.SettingsService() : Cache;
    }
}
namespace Span.ViewModels {
    using Span.Services;
    public class FileSystemViewModel { public string DisplayName = "short"; }
    public partial class FolderViewModel : FileSystemViewModel {
        public class Model { public string Path = "local"; public bool HasChildEntries = true; }
        public Model _folderModel = new();
        public List<FileSystemViewModel> Children = new();
        public bool _isLoaded, IsLoading, IsEmpty, HasError;
        public bool HasChildren => Children.Count > 0;
        public string ChildCountText => Children.Count.ToString();
        public CancellationTokenSource? _cts;
        public string Route = "";
        public int Populates;
        public void OnPropertyChanged(string name) {}
        private Task Loaded(string route, CancellationToken token) {
            token.ThrowIfCancellationRequested(); Route = route; Populates++; Children.Add(new()); return Task.CompletedTask;
        }
        private Task LoadFromCacheAsync(FolderContentCache.CachedFolder cached, CancellationToken token) => Loaded("cache", token);
        private Task LoadFromArchiveAsync(string path, CancellationToken token) => Loaded("archive", token);
        private Task LoadFromRemoteAsync(string path, CancellationToken token) => Loaded("remote", token);
        private Task LoadWslDistributionsAsync(CancellationToken token) => Loaded("wsl", token);
        private Task LoadServerSharesAsync(string path, CancellationToken token) => Loaded("server", token);
        private Task LoadFromDiskAsync(string path, bool hidden, FolderContentCache? cache, CancellationToken token) => Loaded("local", token);
'@
$hostSource = @'
    }
}
namespace Span {
    using Span.ViewModels;
    public partial class MainWindow {
        public int Measurements;
        public double Measure(FolderViewModel folder) => MeasureColumnContentWidth(folder);
        private double MeasureTextWidth(string name, double size) { Measurements++; return name.Length * 7; }
'@
Add-Type -TypeDefinition ($stubs + $load + $hostSource + $width + "`n} }")

$failures = [Collections.Generic.List[string]]::new()
$uiReturned = [Threading.ManualResetEventSlim]::new($false)
$cache = [Span.App]::Current.Services.Cache
$cache.Value = [Span.Services.FolderContentCache+CachedFolder]::new()
$cache.Release.Reset()
$vm = [Span.ViewModels.FolderViewModel]::new()
# A small C# thread driver avoids PowerShell runspace behavior in the simulated UI thread.
# Reflection lets the driver call the separately compiled production-method harness.
$driverSource = @'
#nullable enable
public static class Driver {
    public static System.Exception? Error;
    public static System.Threading.Thread Start(object model, object returned) {
        Error = null;
        var type = model.GetType();
        var signal = (System.Threading.ManualResetEventSlim)returned;
        var thread = new System.Threading.Thread(() => {
            try {
                var task = (System.Threading.Tasks.Task)type.GetMethod("EnsureChildrenLoadedAsync")!.Invoke(model, null)!;
                signal.Set(); task.GetAwaiter().GetResult();
            } catch (System.Exception error) { Error = error; }
        });
        thread.Start(); return thread;
    }
}
'@
Add-Type -TypeDefinition $driverSource
$thread = [Driver]::Start($vm, $uiReturned)
if (-not $cache.Entered.Wait(2000)) { throw 'Cache probe did not start.' }
$responsive = $uiReturned.Wait(200)
$cache.Release.Set()
if (-not $thread.Join(5000)) { throw 'Folder load did not finish.' }
if ([Driver]::Error) { throw [Driver]::Error }
if (-not $responsive) { $failures.Add('Slow cache freshness I/O blocked the calling UI thread.') }
else { 'PASS: slow cache freshness I/O yields the calling UI thread' }
if ($vm.Route -ne 'cache' -or $vm.Populates -ne 1 -or -not $cache.Hidden -or $vm.IsLoading) { throw 'Cache hit or hidden-file behavior changed.' }
'PASS: cache hit populates once and preserves hidden-file settings/loading state'

[Span.App]::Current.Services.Cache = [Span.Services.FolderContentCache]::new()
$cache = [Span.App]::Current.Services.Cache
$cache.Value = [Span.Services.FolderContentCache+CachedFolder]::new()
$cache.Release.Reset()
$uiReturned.Reset()
$vm = [Span.ViewModels.FolderViewModel]::new()
$thread = [Driver]::Start($vm, $uiReturned)
if (-not $cache.Entered.Wait(2000)) { throw 'Cancellation test did not enter freshness check.' }
$vm._cts.Cancel()
$cache.Release.Set()
if (-not $thread.Join(5000)) { throw 'Cancelled load did not finish.' }
if ([Driver]::Error -or $vm.Populates -ne 0 -or $null -ne $vm._cts) { throw 'Cancelled metadata check populated a stale folder or retained its token.' }
'PASS: cancelling slow metadata I/O prevents stale population and releases token'

foreach ($route in @('local', 'archive', 'remote', 'wsl', 'server')) {
    [Span.App]::Current.Services.Cache = [Span.Services.FolderContentCache]::new()
    $vm = [Span.ViewModels.FolderViewModel]::new()
    $vm._folderModel.Path = $route
    $null = $vm.EnsureChildrenLoadedAsync().GetAwaiter().GetResult()
    $null = $vm.EnsureChildrenLoadedAsync().GetAwaiter().GetResult()
    if ($vm.Route -ne $route -or $vm.Populates -ne 1) { throw "Loading route changed: $route" }
    "PASS: $route load routing and already-loaded short circuit"
}

$folder = [Span.ViewModels.FolderViewModel]::new()
$long = [Span.ViewModels.FileSystemViewModel]::new()
$long.DisplayName = 'x' * 100
$folder.Children.Add($long)
for ($i=0; $i -lt 9999; $i++) { $folder.Children.Add([Span.ViewModels.FileSystemViewModel]::new()) }
$window = [Span.MainWindow]::new()
$result = $window.Measure($folder)
"MEASURE: 10000 items, width=$result, text measurements=$($window.Measurements)"
if ($result -ne 600) { throw 'Maximum width changed.' }
if ($window.Measurements -ne 1) { $failures.Add('Text measurement continued after the maximum possible width was reached.') }
else { 'PASS: saturated width avoids 9999 unnecessary UI text measurements' }

$folder.Children.Clear()
$folder.Children.Add([Span.ViewModels.FileSystemViewModel]::new())
$childFolder = [Span.ViewModels.FolderViewModel]::new()
$childFolder.DisplayName = 'folder-name'
$folder.Children.Add($childFolder)
$window = [Span.MainWindow]::new()
if ($window.Measure($folder) -ne 185 -or $window.Measurements -ne 2) { throw 'Exact small-folder width or folder badges changed.' }
'PASS: exact unsaturated width includes folder badge/chevron'
$folder.Children.Clear()
if ($window.Measure($folder) -ne 120) { throw 'Empty folder minimum width changed.' }
'PASS: empty-folder minimum width retained'
if ($failures.Count) { throw ($failures -join "`n") }
