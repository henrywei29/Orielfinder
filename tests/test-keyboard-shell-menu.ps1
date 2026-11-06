$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.KeyboardHandler.cs'))
$method = [regex]::Match($source, '(?sm)^        private (?:async )?void HandleShellContextMenu\(.*?^        }').Value
if (-not $method) { throw 'FAIL: keyboard native context-menu handler is missing.' }
$header = @'
#nullable enable
using System;
using System.Threading.Tasks;
namespace OrielFolder.Helpers { public static class DebugLogger { public static void Log(string text){} } }
namespace OrielFolder.Services {
 public static class ShellContextMenu {
  public static int SyncCalls,AsyncCalls;public static IntPtr Owner;public static string Path="";
  public static TaskCompletionSource<bool> Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public static bool ShowForItem(IntPtr owner,string path){SyncCalls++;return true;}
  public static Task<bool> ShowForItemAsync(IntPtr owner,string path){AsyncCalls++;Owner=owner;Path=path;return Pending.Task;}
 }
}
namespace OrielFolder {
 public class SelectedItem { public string? Path; }
 public class Explorer { public string? CurrentPath; }
 public class ViewModelState { public Explorer? ActiveExplorer; }
 public class MainWindow {
  private IntPtr _hwnd=(IntPtr)7;private SelectedItem? Selected;
  private ViewModelState ViewModel=new();
  private SelectedItem? GetCurrentSelected()=>Selected;
'@
$driver = @'
  public static string Run(){
   var window=new MainWindow{Selected=new(){Path=@"D:\synthetic\selected.txt"}};
   window.HandleShellContextMenu();
   if(Services.ShellContextMenu.SyncCalls!=0 || Services.ShellContextMenu.AsyncCalls!=1)
    throw new Exception("Keyboard native-menu entry still calls the synchronous shell API on the UI thread.");
   window.Selected.Path=@"D:\synthetic\changed.txt";
   if(Services.ShellContextMenu.Owner!=(IntPtr)7 || Services.ShellContextMenu.Path!=@"D:\synthetic\selected.txt")
    throw new Exception("Keyboard native-menu request lost the selected target or owner.");
   window.Selected=null;window.ViewModel.ActiveExplorer=new(){CurrentPath=@"D:\synthetic\folder"};
   window.HandleShellContextMenu();
   if(Services.ShellContextMenu.Path!=@"D:\synthetic\folder" || Services.ShellContextMenu.AsyncCalls!=2)
    throw new Exception("Keyboard fallback to the current folder was lost.");
   window.ViewModel.ActiveExplorer=null;window.HandleShellContextMenu();
   if(Services.ShellContextMenu.AsyncCalls!=2)throw new Exception("Empty selection and folder invoked a stale target.");
   Services.ShellContextMenu.Pending.SetResult(true);
   return "PASS: keyboard full Windows menu uses async shell entry, captures selection, preserves current-folder fallback, and ignores empty targets";
  }
 }
}
'@
Add-Type -TypeDefinition ($header+$method+$driver)
[OrielFolder.MainWindow]::Run()
