$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
$shell=[IO.File]::ReadAllText((Join-Path $root 'Services\ShellContextMenu.cs'))
$methods=foreach($signature in @('public void Dispose','private void DisposeCore','internal void DisposeOnSta')) {
    $match=[regex]::Match($shell,'(?sm)^            '+[regex]::Escape($signature)+'\(.*?^            }')
    if(-not $match.Success){throw "Missing actual method: $signature"};$match.Value
}
$tabs=[IO.File]::ReadAllText((Join-Path $root 'MainWindow.TabManager.cs'))
$newTab=[regex]::Match($tabs,'(?sm)^        private void OnNewTabClick\(.*?^        }').Value
$prefix=@'
#nullable enable
#pragma warning disable CS0169
using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace OrielFolder.Helpers {
    public static class DebugLogger { public static void Log(string text) {} }
    public static class ViewModeExtensions { public static bool IsIconMode(OrielFolder.ViewMode mode)=>false; }
}
namespace Microsoft.UI.Dispatching { public enum DispatcherQueuePriority { Low } }
namespace OrielFolder {
    public static class Marshal { public static int ReleaseComObject(object value)=>0; }
    public class Session {
        private readonly object _lock=new();
        private bool _disposed,_resourcesReleased;
        private BlockingCollection<Action>? _staWorkQueue;
        private IntPtr _hMenu=(IntPtr)1,_pidl=IntPtr.Zero;
        private object _contextMenuObj=new(),_shellFolderObj=new();
        public int CleanupCalls,CleanupThread;
        public Session(BlockingCollection<Action>? queue){_staWorkQueue=queue;}
        private bool DestroyMenu(IntPtr menu){CleanupCalls++;CleanupThread=Thread.CurrentThread.ManagedThreadId;return true;}
        private void CoTaskMemFree(IntPtr pointer){}
'@
$hostSource=@'
    }
    public enum ViewMode { Home,Details,List,MillerColumns }
    public class Tab { public string Id=Guid.NewGuid().ToString();public ViewMode ViewMode=ViewMode.Home; }
    public class Vm { public Tab ActiveTab=new();public void AddNewTab(){ActiveTab=new();} }
    public class RoutedEventArgs:EventArgs {}
    public class Queue {
        public bool TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority priority,Action action){action();return true;}
    }
    public partial class MainWindow {
        public Vm ViewModel=new();public Queue DispatcherQueue=new();
        public int Panels,Subscriptions,VisibilityUpdates,FocusUpdates,TitleUpdates;
        private void CreateMillerPanelForTab(Tab tab){Panels++;}
        private void SwitchMillerPanel(string id){}
        private void SwitchDetailsPanel(string id,bool enabled){}
        private void SwitchListPanel(string id,bool enabled){}
        private void SwitchIconPanel(string id,bool enabled){}
        private void ResubscribeLeftExplorer(){Subscriptions++;}
        private void UpdateViewModeVisibility(){VisibilityUpdates++;}
        private void FocusActiveView(){FocusUpdates++;}
        private void UpdateTitleBarRegions(){TitleUpdates++;}
        public void ClickPlus()=>OnNewTabClick(this,new());
'@
$driver=@'
    }
    public static class PerfTests {
        public static async Task<string[]> Run(){
            var output=new List<string>();
            void Check(bool ok,string message)=>output.Add((ok?"PASS: ":"FAIL: ")+message);
            foreach(bool alreadyCompleted in new[]{false,true}){
                using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
                using var queue=new BlockingCollection<Action>();
                queue.Add(()=>{entered.Set();release.Wait();});
                var session=new Session(queue);
                int ownerThread=0;
                var worker=new Thread(()=>{ownerThread=Thread.CurrentThread.ManagedThreadId;foreach(var action in queue.GetConsumingEnumerable())action();session.DisposeOnSta();});
                worker.SetApartmentState(ApartmentState.STA);worker.IsBackground=true;worker.Start();
                if(!entered.Wait(2000))throw new Exception("STA fixture did not start");
                if(alreadyCompleted)queue.CompleteAdding();
                var disposing=Task.Run(session.Dispose);
                bool responsive=await Task.WhenAny(disposing,Task.Delay(200))==disposing;
                release.Set();await disposing;
                if(!worker.Join(3000))throw new Exception("STA fixture did not finish");
                Check(responsive,"closing menu returns while STA is busy; completedQueue="+alreadyCompleted);
                Check(session.CleanupCalls==1 && session.CleanupThread==ownerThread,"COM cleanup executes once on its owning STA; completedQueue="+alreadyCompleted);
                session.Dispose();Check(session.CleanupCalls==1,"repeated session disposal is harmless; completedQueue="+alreadyCompleted);
            }
            var direct=new Session(null);direct.Dispose();direct.Dispose();
            Check(direct.CleanupCalls==1,"direct same-apartment session still releases once");
            var window=new MainWindow();string old=window.ViewModel.ActiveTab.Id;window.ClickPlus();
            Check(window.Panels==0,"new Home group does not construct an unused Miller view");
            Check(window.Subscriptions==1 && window.VisibilityUpdates==1 && window.FocusUpdates==1 && window.TitleUpdates==1 && window.ViewModel.ActiveTab.Id!=old,"plus still creates and activates a distinct group and updates its visible UI");
            return output.ToArray();
        }
    }
}
'@
Add-Type -TypeDefinition ($prefix+($methods -join "`n")+$hostSource+$newTab+$driver)
$results=[OrielFolder.PerfTests]::Run().GetAwaiter().GetResult()
$results
if(@($results|Where-Object {$_ -like 'FAIL:*'}).Count){throw 'Tab/menu performance regressions failed.'}
