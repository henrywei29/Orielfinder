$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Services\ContextMenuService.cs'))
$methods=foreach($name in @('AppendShellExtensionItemsAsync','AppendBackgroundShellExtensionItemsAsync','AppendShellExtensionItemsCoreAsync','OnMenuClosed','QueueShellExtensionsAfterOpened','TryInvokeInItems')) {
    $match=[regex]::Match($source,'(?sm)^        private (?:async )?(?:Task|void|bool) '+$name+'\(.*?^        }')
    if($match.Success){$match.Value}elseif($name -ne 'QueueShellExtensionsAfterOpened'){throw "Missing method $name"}
}
$header=@'
#nullable enable
#pragma warning disable CS0414
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
namespace Microsoft.UI.Dispatching { public enum DispatcherQueuePriority { Low } public static class DispatcherQueue { public static Microsoft.UI.Xaml.Controls.Queue? GetForCurrentThread()=>null; } }
namespace Microsoft.UI.Xaml { public enum FocusState { Keyboard } }
namespace Microsoft.UI.Xaml.Controls {
    public class MenuFlyoutItemBase {}
    public class MenuFlyoutItem:MenuFlyoutItemBase {
        public string Text="";public double FontSize,MinHeight;public object? Padding,Icon,Tag;public bool IsEnabled;
    }
    public class MenuFlyoutSeparator:MenuFlyoutItemBase {}
    public class ToggleMenuFlyoutItem:MenuFlyoutItem {}
    public class MenuFlyoutSubItem:MenuFlyoutItemBase {
        public object? Tag;public string Text="";public List<MenuFlyoutItemBase> Items=new();public Queue DispatcherQueue=new();
        public void Focus(Microsoft.UI.Xaml.FocusState state){}
    }
    public class FontIcon { public string Glyph="";public double FontSize; }
    public class Queue {
        public System.Collections.Generic.Queue<Action> Pending=new();
        public bool TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority priority,Action action){Pending.Enqueue(action);return true;}
        public void Flush(){while(Pending.Count>0)Pending.Dequeue()();}
    }
    public class MenuFlyout {
        public List<MenuFlyoutItemBase> Items=new(){new MenuFlyoutItem{Text="Copy"},new MenuFlyoutItem{Text="Properties"}};
        public Queue DispatcherQueue=new();public ManualResetEventSlim Loaded=new();public int ShellItems;
        public event EventHandler<object>? Opened,Closed;
        public void Open()=>Opened?.Invoke(this,EventArgs.Empty);
        public void Close()=>Closed?.Invoke(this,EventArgs.Empty);public void Hide()=>Close();
    }
}
namespace OrielFolder.Helpers {
    public static class DebugLogger { public static void Log(string text){} }
    public static class DispatcherHelper { public static void SafeEnqueue(Microsoft.UI.Xaml.Controls.Queue queue,Action action)=>action(); }
    public static class NativeMethods { public static void keybd_event(byte key,byte scan,uint flags,uint extra){} }
}
namespace OrielFolder {
    public class App { public static App Current=new();public Provider Services=new(); }
    public class Provider { public T? GetService<T>() where T:class=>null; }
}
namespace OrielFolder.Services {
    public class CrashReportingService { public void CaptureException(Exception ex,string context){} }
    public class Settings { public bool ShowShellExtensions=true; }
    public class Loc { public string Get(string key)=>key; }
    public static class ShellContextMenu {
        public class Session {
            public List<int> Items=new(){1};public int Disposals;public ManualResetEventSlim Disposed=new();
            public void Dispose(){Disposals++;Disposed.Set();}
        }
        public static Dictionary<string,TaskCompletionSource<Session?>> Requests=new();
        public static Dictionary<string,CancellationToken> Cancellation=new();
        public static int Calls;public static bool Background;
        public static Task<Session?> CreateSessionAsync(IntPtr owner,string path,int timeoutMs=3000,CancellationToken cancellationToken=default){Calls++;Cancellation[path]=cancellationToken;return Requests[path].Task;}
        public static Task<Session?> CreateBackgroundSessionAsync(IntPtr owner,string path,int timeoutMs=3000,CancellationToken cancellationToken=default){Background=true;return CreateSessionAsync(owner,path,timeoutMs,cancellationToken);}
    }
    public partial class ContextMenuService {
        private ShellContextMenu.Session? _currentSession;
        private Dictionary<MenuFlyout,ShellContextMenu.Session> _menuSessionMap=new();
        private Dictionary<MenuFlyout,CancellationTokenSource> _menuShellLoads=new();
        private MenuFlyout? _activeFlyout;private MenuFlyoutSubItem? _openedSubItem=null;
        public IntPtr OwnerHwnd=(IntPtr)1;
        private object CompactPadding=new();public Settings _settings=new();private Loc _loc=new();
        private MenuFlyoutItem CreateItem(string text,string icon,Action action)=>new(){Text=text};
        private Task RebuildMenuWithShellExtensionsAsync()=>Task.CompletedTask;
        private void InsertShellItemsToMenu(MenuFlyout menu,IList<int> items){menu.ShellItems+=items.Count;menu.Loaded.Set();}
        public Task Prepare(MenuFlyout menu,string path,bool background=false,bool force=false){
            _activeFlyout=menu;menu.Closed+=OnMenuClosed;
            return background?AppendBackgroundShellExtensionItemsAsync(menu,path,force):AppendShellExtensionItemsAsync(menu,path,force);
        }
        public ShellContextMenu.Session? Current=>_currentSession;
        private static string? ExtractAccessKeyFromText(string text)=>text.Length>2?text[^2].ToString():null;
        public bool Invoke(MenuFlyout menu,MenuFlyoutItem item,string key){_activeFlyout=menu;return TryInvokeInItems(new List<MenuFlyoutItemBase>{item},key,false);}
'@
$driver=@'
    }
    public static class MenuTests {
        public static async Task<string[]> Run(){
            var output=new List<string>();void Check(bool ok,string text)=>output.Add((ok?"PASS: ":"FAIL: ")+text);
            TaskCompletionSource<ShellContextMenu.Session?> Gate(string path){var gate=new TaskCompletionSource<ShellContextMenu.Session?>(TaskCreationOptions.RunContinuationsAsynchronously);ShellContextMenu.Requests[path]=gate;return gate;}
            {
                var service=new ContextMenuService();var menu=new MenuFlyout();bool closed=false,accepted=false;
                menu.Closed+=(_,_)=>closed=true;var completion=new TaskCompletionSource();
                Func<Task> shellAction=async()=>{accepted=!closed;await completion.Task;};
                bool invoked=service.Invoke(menu,new MenuFlyoutItem{Text="Shell(S)",Tag=shellAction},"S");
                Check(invoked && accepted && closed,"shell accelerator accepts its async command before menu closure releases the session");
                completion.SetResult();
                bool ordinarySawClosed=false;var ordinary=new MenuFlyout();ordinary.Closed+=(_,_)=>ordinarySawClosed=true;
                bool ranAfterClose=false;
                service.Invoke(ordinary,new MenuFlyoutItem{Text="Open(O)",Tag=(Action)(()=>ranAfterClose=ordinarySawClosed)},"O");
                Check(ranAfterClose,"ordinary menu actions retain close-before-invoke ordering");
            }
            foreach(bool background in new[]{false,true}){
                var service=new ContextMenuService();var menu=new MenuFlyout();var gate=Gate("normal");
                var ready=service.Prepare(menu,"normal",background);
                Check(ready.IsCompleted,"base menu is available while shell extensions are delayed; background="+background);
                menu.Open();menu.DispatcherQueue.Flush();var session=new ShellContextMenu.Session();gate.SetResult(session);await ready;
                Check(menu.Loaded.Wait(2000) && menu.ShellItems==1,"shell extension items arrive after the menu is shown; background="+background);
                menu.Close();Check(session.Disposals==1,"closing releases only the menu-owned session; background="+background);
            }
            {
                var service=new ContextMenuService();var menu=new MenuFlyout();var gate=Gate("closed");
                var ready=service.Prepare(menu,"closed");menu.Open();menu.DispatcherQueue.Flush();menu.Close();
                Check(ShellContextMenu.Cancellation["closed"].IsCancellationRequested,"closing cancels pending shell enumeration without waiting on native code");
                var session=new ShellContextMenu.Session();gate.SetResult(session);await ready;
                Check(session.Disposed.Wait(2000) && menu.ShellItems==0,"late result for a closed menu is disposed without inserting items");
            }
            {
                var service=new ContextMenuService();var old=new MenuFlyout();var current=new MenuFlyout();var a=Gate("old");var b=Gate("current");
                var first=service.Prepare(old,"old");old.Open();old.DispatcherQueue.Flush();
                var second=service.Prepare(current,"current");current.Open();current.DispatcherQueue.Flush();
                var currentSession=new ShellContextMenu.Session();b.SetResult(currentSession);await second;current.Loaded.Wait(2000);
                old.Close();Check(currentSession.Disposals==0,"closing an older pending menu leaves the current menu session alive");
                var oldSession=new ShellContextMenu.Session();a.SetResult(oldSession);await first;
                Check(oldSession.Disposed.Wait(2000) && ReferenceEquals(service.Current,currentSession),"out-of-order completion cannot replace the active menu session");
                current.Close();
            }
            {
                var service=new ContextMenuService();service._settings.ShowShellExtensions=false;var menu=new MenuFlyout();int before=ShellContextMenu.Calls;
                var ready=service.Prepare(menu,"disabled");
                Check(ready.IsCompleted && ShellContextMenu.Calls==before && menu.Items.Count==4,"disabled automatic extensions retain the manual-load menu entry");
                var force=new MenuFlyout();var gate=Gate("forced");var forced=service.Prepare(force,"forced",force:true);force.Open();force.DispatcherQueue.Flush();
                var session=new ShellContextMenu.Session();gate.SetResult(session);await forced;
                Check(force.Loaded.Wait(2000) && force.ShellItems==1,"explicit loading still works when automatic extensions are disabled");force.Close();
            }
            return output.ToArray();
        }
    }
}
'@
Add-Type -TypeDefinition ($header+($methods -join "`n")+$driver)
$results=[OrielFolder.Services.MenuTests]::Run().GetAwaiter().GetResult();$results
if(@($results|Where-Object {$_ -like 'FAIL:*'}).Count){throw 'Context menu responsiveness regressions failed.'}
