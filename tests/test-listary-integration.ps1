$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$files = @('src\OrielFolder\OrielFolder\Helpers\ListaryFolderPath.cs', 'src\OrielFolder\OrielFolder\Services\ListaryWindowBridge.cs',
    'src\OrielFolder\Listary.FileAppPlugin.OrielFolder\OrielFolderPlugin.cs')
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $repo $file))) { throw 'Listary directory-provider integration is not implemented.' }
}
$sources = $files | ForEach-Object { [IO.File]::ReadAllText((Join-Path $repo $_)) }
$integration = [IO.File]::ReadAllText((Join-Path $repo 'src\OrielFolder\OrielFolder\MainWindow.ListaryIntegration.cs'))
$pathMethod = [regex]::Match($integration, '(?sm)^        private string GetListaryFolderPath\(\).*?^        }').Value
if (-not $pathMethod) { throw 'Cannot find the active-pane Listary path resolver.' }
$stubs = @'
using System;
using System.Threading.Tasks;
namespace Listary.FileAppPlugin {
    public enum SearchBarType { Floating, Fixed }
    public interface IFileAppPluginHost {}
    public interface IFileAppPlugin {
        bool IsOpenedFolderProvider { get; }
        bool IsQuickSwitchTarget { get; }
        bool IsSharedAcrossApplications { get; }
        SearchBarType SearchBarType { get; }
        Task<bool> Initialize(IFileAppPluginHost host);
        IFileWindow BindFileWindow(IntPtr handle);
    }
    public interface IFileWindow { IntPtr Handle { get; } Task<IFileTab> GetCurrentTab(); }
    public interface IFileTab {}
    public interface IGetFolder { Task<string> GetCurrentFolder(); }
}
'@
$mainWindowHost = @'
namespace OrielFolder {
    public enum ViewMode { MillerColumns, Details, List, IconSmall, IconMedium, IconLarge, IconExtraLarge, Home, Settings, ActionLog, RecycleBin }
    public enum ActivePane { Left, Right }
    public class Folder { public string Path = @"D:\temporary\Child"; }
    public class Vm {
        public bool IsSplitViewEnabled;
        public ActivePane ActivePane;
        public ViewMode CurrentViewMode, RightViewMode;
    }
    public class MainWindow {
        public bool _isClosed;
        public Vm ViewModel = new Vm();
        private Folder GetNewItemTargetFolder() => new Folder();
        public string ReadPath() => GetListaryFolderPath();
'@
$mainWindowEnd = @'
    }
}
namespace OrielFolder.Helpers {
    public static class ViewModeExtensions {
        public static bool IsIconMode(OrielFolder.ViewMode mode) => mode >= OrielFolder.ViewMode.IconSmall && mode <= OrielFolder.ViewMode.IconExtraLarge;
    }
}
'@
$checks = @'
using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using OrielFolder.Helpers;
using OrielFolder.Services;
using Listary.FileAppPlugin.OrielFolder;
public static class ListaryChecks {
    private static string ReadPath(IntPtr window) {
        var tab = new OrielFolderWindow(window).GetCurrentTab().GetAwaiter().GetResult();
        return ((Listary.FileAppPlugin.IGetFolder)tab).GetCurrentFolder().GetAwaiter().GetResult();
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
    public static void Normalize(string input, string expected) {
        if (ListaryFolderPath.Normalize(input) != expected) throw new Exception("Incorrect Listary path filtering.");
    }
    public static void BridgeRoundTrip() {
        var window = CreateWindowExW(0,"STATIC","SPAN Dev test",0,0,0,0,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if (window == IntPtr.Zero) throw new Exception("Test window creation failed.");
        try {
            string activePath = @"D:\temporary\左侧 文件夹";
            using (var bridge = new ListaryWindowBridge(window, () => activePath)) {
                if (ReadPath(window) != activePath) throw new Exception("Native Unicode path round-trip failed.");
                activePath = @"D:\temporary\Right\Child";
                if (ReadPath(window) != activePath) throw new Exception("Provider returned a stale pane path.");
                activePath = "sftp://name:password@example.com/private";
                if (ReadPath(window) != "") throw new Exception("Non-filesystem path escaped to Listary.");
                activePath = @"D:\temporary\Second tab";
                if (ReadPath(window) != activePath) throw new Exception("Provider did not recover after leaving a special page.");
            }
            if (ReadPath(window) != "") throw new Exception("Disposed bridge still returns a directory.");
            var plugin = new OrielFolderPlugin();
            if (plugin.BindFileWindow(window) != null) throw new Exception("Plugin bound an unrelated process.");
            if (plugin.BindFileWindow(IntPtr.Zero) != null) throw new Exception("Plugin bound an invalid window.");
            if (!plugin.IsOpenedFolderProvider || plugin.IsQuickSwitchTarget || plugin.IsSharedAcrossApplications)
                throw new Exception("Plugin role can modify other applications instead of only providing paths.");
        } finally { DestroyWindow(window); }
    }
    public static void BusyWindowTimeout() {
        IntPtr window = IntPtr.Zero;
        using (var ready = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim()) {
            var thread = new Thread(() => {
                window = CreateWindowExW(0,"STATIC","Busy test",0,0,0,0,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
                using (var bridge = new ListaryWindowBridge(window, () => @"D:\temporary\busy")) {
                    ready.Set(); release.Wait();
                }
                DestroyWindow(window);
            });
            thread.Start();
            try {
                if (!ready.Wait(3000)) throw new Exception("Busy test thread did not initialize.");
                var watch = Stopwatch.StartNew();
                var path = ReadPath(window);
                if (path != "" || watch.ElapsedMilliseconds > 1500) throw new Exception("Busy UI blocked the path query or returned stale data.");
            } finally { release.Set(); thread.Join(3000); }
        }
    }
}
'@
# Compile all real sources in one generated unit; hoist only top-level using directives.
$all = @($stubs) + $sources + @($checks, ($mainWindowHost + $pathMethod + $mainWindowEnd))
$using = @(); $body = @()
foreach ($unit in $all) {
    $using += [regex]::Matches($unit,'(?m)^using [^\r\n]+;\s*$') | ForEach-Object {$_.Value.Trim()}
    $body += [regex]::Replace($unit,'(?m)^using [^\r\n]+;\s*$','')
}
Add-Type -TypeDefinition ((($using | Select-Object -Unique) -join "`n") + "`n" + ($body -join "`n"))
foreach ($case in @(
    @('D:\project\资料 文件夹','D:\project\资料 文件夹'), @('C:\','C:\'),
    @('\\server\share\Folder','\\server\share\Folder'), @('\\server\share','\\server\share'),
    @('',''), @('PC',''), @('Downloads',''), @('D:relative',''), @('\relative',''),
    @('sftp://user:password@example.com/private',''), @('https://example.com/folder',''),
    @('D:\archive.zip::inner',''), @('\\server',''), @('\\.\PhysicalDrive0',''),
    @("D:\bad`nfolder",''), @("D:\bad`0folder",'')
)) {
    [ListaryChecks]::Normalize($case[0],$case[1])
}
'PASS: only absolute local and UNC-share directory paths are exposed; URI credentials and virtual paths are excluded'
[ListaryChecks]::BridgeRoundTrip()
'PASS: real native bridge and plugin preserve Unicode paths and follow pane changes without polling'
'PASS: bridge disposal, unrelated windows and directory-provider-only role remain safe'
[ListaryChecks]::BusyWindowTimeout()
'PASS: busy target window returns no path within a bounded timeout'
$window = [OrielFolder.MainWindow]::new()
foreach($mode in @('MillerColumns','Details','List','IconSmall','IconMedium','IconLarge','IconExtraLarge','Home','Settings','ActionLog','RecycleBin')) {
    $window.ViewModel.CurrentViewMode = [OrielFolder.ViewMode]::$mode
    $expected = if($mode -in @('Home','Settings','ActionLog','RecycleBin')) { '' } else { 'D:\temporary\Child' }
    if($window.ReadPath() -ne $expected) { throw "Wrong Listary directory for view mode $mode" }
}
'PASS: special pages never expose an old directory; folder views provide the resolved active target'
$window.ViewModel.IsSplitViewEnabled = $true
$window.ViewModel.ActivePane = [OrielFolder.ActivePane]::Right
$window.ViewModel.CurrentViewMode = [OrielFolder.ViewMode]::Home
$window.ViewModel.RightViewMode = [OrielFolder.ViewMode]::Details
if($window.ReadPath() -ne 'D:\temporary\Child') { throw 'Right-side folder was hidden by left-side Home.' }
$window.ViewModel.CurrentViewMode = [OrielFolder.ViewMode]::Details
$window.ViewModel.RightViewMode = [OrielFolder.ViewMode]::Home
if($window.ReadPath() -ne '') { throw 'Right-side Home exposed a stale left-side folder.' }
$window._isClosed = $true
if($window.ReadPath() -ne '') { throw 'Closed window exposed its previous path.' }
'PASS: split pane view modes and closed windows do not leak the opposite or stale directory'
