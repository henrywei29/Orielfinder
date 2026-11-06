$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
# Reuse the real query/filter source list, without executing the matching tests.
$matching = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'test-search-matching.ps1'))
$prefix = $matching.Substring(0, $matching.IndexOf('$handler ='))
Invoke-Expression $prefix.Replace('$PSScriptRoot', "'$($PSScriptRoot.Replace("'", "''"))'")
$start = $header.IndexOf('namespace OrielFolder.Models')
$header = $header.Substring(0,$start) + @'
namespace OrielFolder.Models {
    public class FolderItem { public string Name = "", Path = ""; public DateTime DateModified; public bool IsHidden; }
    public class FileItem : FolderItem { public long Size; public string FileType = ""; }
}
namespace OrielFolder.Services { public class FileSystemService {} public static class LocalizationService {
    public static string L(string key) => key;
} }
namespace OrielFolder.ViewModels {
    public class FileSystemViewModel {
        public string Name = "", Path = "", LocationPath = "";
        public long SizeValue; public DateTime DateModifiedValue;
    }
    public class FileViewModel : FileSystemViewModel {
        public static Action? BeforeCreate;
        public FileViewModel(OrielFolder.Models.FileItem model) {
            BeforeCreate?.Invoke(); Name=model.Name; Path=model.Path; SizeValue=model.Size; DateModifiedValue=model.DateModified;
        }
    }
    public class FolderViewModel : FileSystemViewModel {
        public FolderViewModel(OrielFolder.Models.FolderItem model, OrielFolder.Services.FileSystemService service) {
            Name=model.Name; Path=model.Path; DateModifiedValue=model.DateModified;
        }
        public System.Collections.ObjectModel.ObservableCollection<FileSystemViewModel> Children = new();
        public FileSystemViewModel? SelectedChild;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add {} remove {} }
        public event Action<string>? LoadError { add {} remove {} }
        public void CancelLoading() {}
        public void MarkAsManuallyPopulated() {}
    }
    public class Progress<T> : IProgress<T> {
        public static System.Collections.Concurrent.ConcurrentQueue<Action> Pending = new();
        private readonly Action<T> _callback;
        public Progress(Action<T> callback) { _callback=callback; }
        public void Report(T value) => Pending.Enqueue(() => _callback(value));
    }
}
'@
$header = "using System.Threading.Channels;`nusing System.ComponentModel;`nusing OrielFolder.Services;`n" + $header.Replace('#nullable enable', '')
$codeFiles = @('Helpers\FileVisibility.cs','Services\RecursiveSearchService.cs')
foreach($file in $codeFiles) {
    $code = [regex]::Replace([IO.File]::ReadAllText((Join-Path $root $file)), '(?m)^using .*;\r?\n', '')
    if ($code -match '(?m)^namespace ([\w.]+);') { $code = [regex]::Replace($code, '(?m)^namespace ([\w.]+);', 'namespace $1 {') + "`n}" }
    $sources += $code
}
$explorerSource = [IO.File]::ReadAllText((Join-Path $root 'ViewModels\ExplorerViewModel.cs'))
$methods = foreach($name in @('public async Task StartRecursiveSearchAsync','public void CancelRecursiveSearch','private void CancelRecursiveSearchInternal')) {
    $match = [regex]::Match($explorerSource, '(?sm)^        '+[regex]::Escape($name)+'\(.*?^        }')
    if (-not $match.Success) { throw "Missing production method: $name" }
    $match.Value
}
$vmHost = @'
namespace OrielFolder.ViewModels {
    public partial class ExplorerViewModel {
        private FolderViewModel? _searchResultFolder;
        private List<FolderViewModel>? _preSearchColumns;
        private Dictionary<FolderViewModel, FileSystemViewModel?>? _preSearchSelectedChildren;
        private string _preSearchPath = "";
        private CancellationTokenSource? _searchCts;
        private readonly FileSystemService _fileService = new();
        public System.Collections.ObjectModel.ObservableCollection<FolderViewModel> Columns = new();
        public string CurrentPath = "", SearchStatusText = "";
        public bool IsRecursiveSearching;
        public FileSystemViewModel? SelectedFile;
        public bool HasActiveSearchResults => _searchResultFolder != null;
        private void FolderVm_PropertyChanged(object? sender, PropertyChangedEventArgs args) {}
        private void OnColumnLoadError(string error) {}
        private void OnPropertyChanged(string name) {}
        private void AddColumn(FolderViewModel folder) => Columns.Add(folder);
'@
$driver = @'
namespace SearchTests {
    public class InlineProgress : IProgress<RecursiveSearchService.SearchProgress> {
        public Action<RecursiveSearchService.SearchProgress>? Action;
        public void Report(RecursiveSearchService.SearchProgress value) => Action?.Invoke(value);
    }
    public static class Run {
        public static async Task<string[]> All(string root) {
            var results = new List<string>();
            void Check(bool condition, string text) { results.Add((condition ? "PASS: " : "FAIL: ") + text); }
            Check(TraversalTest.CanScan(FileAttributes.ReparsePoint,null),"non-link cloud/provider reparse directories remain traversable");
            Check(!TraversalTest.CanScan(FileAttributes.ReparsePoint,"target"),"actual linked subdirectories are not recursively followed");
            var service = new RecursiveSearchService(new FileSystemService());
            var sparse = System.IO.Path.Combine(root,"sparse");
            Directory.CreateDirectory(sparse);
            File.WriteAllText(System.IO.Path.Combine(sparse,"needle.txt"),"fixture");
            for(int i=0;i<30;i++) Directory.CreateDirectory(System.IO.Path.Combine(sparse,"child"+i));
            using(var entered = new ManualResetEventSlim())
            using(var release = new ManualResetEventSlim())
            using(var cts = new CancellationTokenSource()) {
                var progress = new InlineProgress { Action = p => { if(p.FoldersScanned==20) { entered.Set(); release.Wait(); } } };
                var reader = service.SearchInBackground(sparse,SearchQueryParser.Parse("needle"),true,progress,cts.Token);
                var reading = reader.ReadAsync().AsTask();
                bool paused = entered.Wait(5000);
                bool early = await Task.WhenAny(reading,Task.Delay(200)) == reading;
                release.Set(); cts.Cancel();
                Check(paused && early && reading.IsCompletedSuccessfully,"sparse results appear before the whole tree finishes scanning");
            }
            var dense = System.IO.Path.Combine(root,"dense");
            Directory.CreateDirectory(dense);
            for(int i=0;i<200;i++) File.WriteAllText(System.IO.Path.Combine(dense,"needle"+i+".txt"),"fixture");
            using(var entered = new ManualResetEventSlim())
            using(var release = new ManualResetEventSlim())
            using(var cts = new CancellationTokenSource()) {
                int created=0;
                FileViewModel.BeforeCreate = () => { if(Interlocked.Increment(ref created)==51) { entered.Set(); release.Wait(); } };
                var reader = service.SearchInBackground(dense,SearchQueryParser.Parse("needle"),true,null,cts.Token);
                var reading = reader.ReadAsync().AsTask();
                bool paused = entered.Wait(5000);
                bool early = await Task.WhenAny(reading,Task.Delay(200)) == reading;
                release.Set(); cts.Cancel(); FileViewModel.BeforeCreate=null;
                Check(paused && early && reading.IsCompletedSuccessfully && reading.Result.Count<=50,"large directories stream bounded batches during enumeration");
            }
            using(var cts = new CancellationTokenSource()) {
                cts.Cancel();
                var reader = service.SearchInBackground(root,SearchQueryParser.Parse("needle"),true,null,cts.Token);
                Check(await Task.WhenAny(reader.Completion,Task.Delay(300)) == reader.Completion,"pre-cancelled searches still complete their result channel");
            }
            var nested = System.IO.Path.Combine(root,"nested"); Directory.CreateDirectory(nested);
            File.WriteAllText(System.IO.Path.Combine(nested,"年度财务报告.pdf"),"fixture");
            File.WriteAllText(System.IO.Path.Combine(nested,"report.pdf"),"fixture");
            var found = new List<FileSystemViewModel>();
            await foreach(var batch in service.SearchInBackground(root,SearchQueryParser.Parse("年度 报告 ext:pdf"),true,null,CancellationToken.None).ReadAllAsync()) found.AddRange(batch);
            Check(found.Count==1 && found[0].Name=="年度财务报告.pdf" && found[0].LocationPath=="nested","real nested files support fuzzy keywords and strict extensions");
            var vm = new ExplorerViewModel { CurrentPath=root };
            var original = new FolderViewModel(new FolderItem { Name="original",Path=root },new FileSystemService());
            vm.Columns.Add(original);
            await vm.StartRecursiveSearchAsync(SearchQueryParser.Parse("年度"),root,true);
            var stale = new List<Action>();
            while(OrielFolder.ViewModels.Progress<RecursiveSearchService.SearchProgress>.Pending.TryDequeue(out var callback)) stale.Add(callback);
            await vm.StartRecursiveSearchAsync(SearchQueryParser.Parse("report"),root,true);
            string currentStatus=vm.SearchStatusText;
            foreach(var callback in stale) callback();
            Check(vm.SearchStatusText==currentStatus,"cancelled searches cannot overwrite the latest search status");
            vm.CancelRecursiveSearch();
            Check(vm.Columns.Count==1 && ReferenceEquals(vm.Columns[0],original) && vm.CurrentPath==root && !vm.HasActiveSearchResults,"Escape after consecutive queries restores the original folder");
            await vm.StartRecursiveSearchAsync(SearchQueryParser.Parse("年度"),root,true);
            var navigated = new FolderViewModel(new FolderItem { Name="navigated",Path=nested },new FileSystemService());
            vm.Columns.Clear(); vm.Columns.Add(navigated); vm.CurrentPath=nested;
            await vm.StartRecursiveSearchAsync(SearchQueryParser.Parse("report"),nested,true);
            vm.CancelRecursiveSearch();
            Check(vm.Columns.Count==1 && ReferenceEquals(vm.Columns[0],navigated) && vm.CurrentPath==nested,"search after navigating elsewhere restores that new folder");
            string link=System.IO.Path.Combine(root,"linked-folder");
            Directory.CreateSymbolicLink(link,nested);
            found.Clear();
            await foreach(var batch in service.SearchInBackground(root,SearchQueryParser.Parse("report"),true,null,CancellationToken.None).ReadAllAsync()) found.AddRange(batch);
            Check(found.Count==1,"recursive search does not follow directory links outside its tree or into cycles");
            found.Clear();
            await foreach(var batch in service.SearchInBackground(root,SearchQueryParser.Parse("linked-folder"),true,null,CancellationToken.None).ReadAllAsync()) found.AddRange(batch);
            Check(found.Count==1 && found[0] is FolderViewModel,"directory link itself remains searchable");
            found.Clear();
            await foreach(var batch in service.SearchInBackground(link,SearchQueryParser.Parse("report"),true,null,CancellationToken.None).ReadAllAsync()) found.AddRange(batch);
            Check(found.Count==1,"an explicitly selected linked folder can still be searched");
            return results.ToArray();
        }
    }
}
'@
$serviceSource = [IO.File]::ReadAllText((Join-Path $root 'Services\RecursiveSearchService.cs'))
$linkGuard = [regex]::Match($serviceSource, '(?m)^\s*if \([^\r\n]+\)\r?\n\s*queue.Enqueue\(sub.FullName\);').Value
if (-not $linkGuard) { throw 'Cannot locate production directory traversal guard.' }
$traversalHost = @'
namespace SearchTests { public static class TraversalTest {
    public static bool CanScan(FileAttributes attributes,string? target) {
        var sub = new { Attributes=attributes,LinkTarget=target,FullName="test" };
        var queue = new Queue<string>();
'@
$traversalHost += $linkGuard + "`nreturn queue.Count == 1; } } }"
Add-Type -TypeDefinition ("#nullable enable`n" + $header + ($sources -join "`n") + $vmHost + ($methods -join "`n") + "`n} }`n" + $traversalHost + $driver)
$fixture = Join-Path 'D:\codex\Orielfolder-development\manual-data' ('search-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $fixture | Out-Null
$results = [SearchTests.Run]::All($fixture).GetAwaiter().GetResult()
$results
if (@($results | Where-Object {$_ -like 'FAIL:*'}).Count) { throw 'Search streaming/lifecycle regressions failed.' }
