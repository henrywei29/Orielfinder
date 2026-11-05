$ErrorActionPreference='Stop'
$script=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'test-context-menu-responsiveness.ps1'))
$prefix=$script.Substring(0,$script.IndexOf('$driver=@'))
Invoke-Expression $prefix.Replace('$PSScriptRoot',"'$($PSScriptRoot.Replace("'","''"))'")
$header=$header.Replace('public string Glyph="";public double FontSize;','public string Glyph="";public double FontSize;public object? FontFamily;')
$header=$header.Replace('CreateItem(string text,string icon,Action action)','CreateItem(string text,string? icon,Action action)')
$extra=foreach($name in @('BuildNewItemMenu','PopulateShellNewItems','QueueShellNewItemsAfterOpened')) {
    $match=[regex]::Match($source,'(?sm)^        (?:private|public) (?:void|MenuFlyout) '+$name+'\(.*?^        }')
    if($match.Success){$match.Value}elseif($name -ne 'QueueShellNewItemsAfterOpened'){throw "Missing $name"}
}
$registry=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\Services\ShellNewService.cs'))
$getCache=[regex]::Match($registry,'(?sm)^    public List<ShellNewItem> GetShellNewItems\(.*?^    }').Value
$invalidate=[regex]::Match($registry,'(?m)^    public void InvalidateCache\(\) =>.*').Value
if(-not $invalidate){$invalidate=[regex]::Match($registry,'(?sm)^    public void InvalidateCache\(.*?^    }').Value}
$inside=@'
        public ShellNewFixture _shellNewService=new();
        private void TrackFlyout(MenuFlyout menu){_activeFlyout=menu;menu.Closed+=OnMenuClosed;}
        private static string GetShellNewIcon(string extension)=>"icon";
        private MenuFlyoutItem CreateItem(string text,string icon,Action action,string key)=>CreateItem(text,icon,action);
'@
$outside=@'
    }
    public class IContextMenuHost { public void PerformNewFolder(string path){} public void PerformNewFileFromShellNew(string path,ShellNewItem item){} }
    public class ShellNewItem { public string DisplayName="fixture type",Extension=".txt"; }
    public class ShellNewFixture {
        public ManualResetEventSlim Entered=new(),Release=new();public int ScanThread;
        public List<ShellNewItem> GetShellNewItems(){ScanThread=Thread.CurrentThread.ManagedThreadId;Entered.Set();Release.Wait();return new(){new()};}
    }
    public class CacheFixture {
        private List<ShellNewItem>? _cache;
        private readonly object _cacheLock=new();
        public int Scans;public ManualResetEventSlim Entered=new(),Second=new(),Release=new();
        private List<ShellNewItem> ScanRegistry(){if(Interlocked.Increment(ref Scans)>1)Second.Set();Entered.Set();Release.Wait();return new(){new()};}
'@
$driver=@'
    }
    public static class NewTypeTests {
        public static async Task<string[]> Run(){
            var output=new List<string>();void Check(bool ok,string text)=>output.Add((ok?"PASS: ":"FAIL: ")+text);
            var service=new ContextMenuService();int buildThread=0;
            var building=Task.Factory.StartNew(()=>{buildThread=Thread.CurrentThread.ManagedThreadId;return service.BuildNewItemMenu(@"D:\fixture",new IContextMenuHost());},CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
            bool ready=await Task.WhenAny(building,Task.Delay(200))==building;
            if(!ready)service._shellNewService.Release.Set();
            var menu=await building;
            Check(ready,"new-file menu opens before slow registry enumeration completes");
            menu.Open();menu.DispatcherQueue.Flush();
            Check(service._shellNewService.Entered.Wait(2000) && service._shellNewService.ScanThread!=buildThread,"file-type registry discovery is performed off the UI build thread");
            service._shellNewService.Release.Set();
            bool populated=SpinWait.SpinUntil(()=>menu.Items.Exists(i=>i is MenuFlyoutItem item && item.Text=="fixture type"),2000);
            Check(populated,"discovered file types are still added to the open menu");menu.Close();
            var cache=new CacheFixture();var first=Task.Run(cache.GetShellNewItems);cache.Entered.Wait(2000);var second=Task.Run(cache.GetShellNewItems);
            bool duplicated=cache.Second.Wait(200);cache.Release.Set();await Task.WhenAll(first,second);
            Check(!duplicated && cache.Scans==1 && ReferenceEquals(first.Result,second.Result),"concurrent menus share one registry scan");
            cache.InvalidateCache();cache.GetShellNewItems();Check(cache.Scans==2,"explicit cache invalidation still refreshes discovered types");
            return output.ToArray();
        }
    }
}
namespace Span.Helpers { public static class ArchivePathHelper { public static bool IsArchivePath(string path)=>false; } }
namespace Microsoft.UI.Xaml.Media { public class FontFamily { public FontFamily(string path){} } }
'@
Add-Type -TypeDefinition ($header+($methods -join "`n")+$inside+($extra -join "`n")+$outside+$getCache+$invalidate+$driver)
$results=[Span.Services.NewTypeTests]::Run().GetAwaiter().GetResult();$results
if(@($results|Where-Object {$_ -like 'FAIL:*'}).Count){throw 'ShellNew responsiveness regressions failed.'}
