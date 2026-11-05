$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\Services\ContextMenuService.cs'))
$method = [regex]::Match($source, '(?sm)^        private void AppendFullWindowsContextMenu\(.*?^        }').Value
if (-not $method) { throw 'FAIL: ordinary local item menus have no deferred full Windows context-menu entry.' }
$header = @'
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
namespace Microsoft.UI.Xaml.Controls {
    public class MenuFlyoutItemBase {}
    public class MenuFlyoutSeparator : MenuFlyoutItemBase {}
    public class MenuFlyoutItem : MenuFlyoutItemBase { public Action Click = null!; }
    public class Queue {
        public List<Action> Work = new();
        public bool TryEnqueue(Action action) { Work.Add(action); return true; }
        public void Flush() { foreach(var action in Work.ToArray()) action(); Work.Clear(); }
    }
    public class MenuFlyout {
        public List<MenuFlyoutItemBase> Items = new(); public Queue DispatcherQueue = new();
        public event EventHandler<object>? Closed;
        public void Hide() { Closed?.Invoke(this, EventArgs.Empty); }
    }
}
namespace Span.Helpers { public static class DebugLogger { public static void Log(string text){} } }
namespace Span.Services {
    public class Loc { public string Get(string key) => key; }
    public static class ShellContextMenu {
        public static int Calls; public static IntPtr Owner; public static string Path=""; public static int X,Y;
        public static TaskCompletionSource<bool>? Pending;
        public static Task<bool> ShowForItemAtAsync(IntPtr owner, string path, int x, int y) { Calls++;Owner=owner;Path=path;X=x;Y=y;return Pending?.Task ?? Task.FromResult(true); }
    }
    public class ContextMenuService {
        public IntPtr OwnerHwnd = (IntPtr)4;
        private Loc _loc = new(); public Action? ShellCommandExecutedCallback;
        public Action<string>? InvokeFailedCallback;
        private struct NativePoint { public int X,Y; }
        private static bool GetCursorPos(out NativePoint point) { point=new(){X=20,Y=30};return true; }
        private MenuFlyoutItem CreateItem(string text,string glyph,Action action) => new(){Click=action};
        public void Add(MenuFlyout menu,string path) => AppendFullWindowsContextMenu(menu,path);
'@
$driver = @'
    }
    public static class FullMenuTests {
        public static async Task<string> Run() {
            var service = new ContextMenuService(); var menu = new MenuFlyout(); int refreshed=0;
            service.ShellCommandExecutedCallback=()=>refreshed++;
            service.Add(menu,@"D:\synthetic\folder");
            if(ShellContextMenu.Calls!=0 || menu.Items.Count!=2) throw new Exception("Native menu was loaded eagerly or entry was missing.");
            ((MenuFlyoutItem)menu.Items[1]).Click();
            service.OwnerHwnd=(IntPtr)9;
            if(ShellContextMenu.Calls!=0) throw new Exception("Native menu ran before flyout closure dispatched.");
            menu.DispatcherQueue.Flush();
            if(ShellContextMenu.Calls!=1 || ShellContextMenu.Owner!=(IntPtr)4 || ShellContextMenu.Path!=@"D:\synthetic\folder" || ShellContextMenu.X!=20 || ShellContextMenu.Y!=30 || refreshed!=1)
                throw new Exception("Clicked target, owner or screen position was lost during flyout closure.");
            menu.Hide();menu.DispatcherQueue.Flush();
            if(ShellContextMenu.Calls!=1) throw new Exception("Full menu was reopened by an unrelated closure.");
            var pendingMenu=new MenuFlyout();var finished=new TaskCompletionSource();int failures=0;
            service.InvokeFailedCallback=_=>failures++;
            service.ShellCommandExecutedCallback=()=>finished.SetResult();
            ShellContextMenu.Pending=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Add(pendingMenu,@"D:\synthetic\second");
            ((MenuFlyoutItem)pendingMenu.Items[1]).Click();
            await Task.Run(()=>pendingMenu.DispatcherQueue.Flush()).WaitAsync(TimeSpan.FromSeconds(2));
            if(ShellContextMenu.Calls!=2 || finished.Task.IsCompleted) throw new Exception("Deferred native operation blocked the dispatcher or completed refresh before the shell returned.");
            ShellContextMenu.Pending.SetResult(false);await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if(failures!=1) throw new Exception("Native menu failure was not reported after asynchronous completion.");
            return "PASS: actual full-menu entry defers native rendering, captures target/owner/location, executes once, and lets the dispatcher return while native work is pending";
        }
    }
}
'@
Add-Type -TypeDefinition ($header + $method + $driver)
[Span.Services.FullMenuTests]::Run().GetAwaiter().GetResult()
