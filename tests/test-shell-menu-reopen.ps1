$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Services\ContextMenuService.cs'))
$method = [regex]::Match($source, '(?sm)^        public async Task RebuildMenuWithShellExtensionsAsync\(.*?^        }').Value
if (-not $method) { throw 'FAIL: manual shell-extension menu rebuild is missing.' }
$header = @'
#nullable enable
#pragma warning disable CS0649
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
namespace Windows.Foundation { public struct Point {} }
namespace Microsoft.UI.Xaml {
 public class FrameworkElement { public Queue DispatcherQueue=new(); }
}
namespace Microsoft.UI.Xaml.Controls.Primitives {
 public class FlyoutShowOptions { public Windows.Foundation.Point Position; }
}
namespace Microsoft.UI.Xaml.Controls {
 public class Queue {
  public List<Action> Work=new();public bool TryEnqueue(Action action){Work.Add(action);return true;}
  public void Flush(){foreach(var action in Work.ToArray())action();Work.Clear();}
 }
 public class MenuFlyout {
  public static bool Dismissing;public static int Shows;public Queue DispatcherQueue=new();
  public event EventHandler<object>? Closed;
  public void Hide(){Dismissing=true;}
  public void CompleteClose(){Dismissing=false;Closed?.Invoke(this,EventArgs.Empty);}
  public void ShowAt(Microsoft.UI.Xaml.FrameworkElement element,Primitives.FlyoutShowOptions options){
   if(Dismissing)throw new Exception("Replacement popup shown during original popup dismissal");Shows++;
  }
 }
}
namespace OrielFolder.Services {
 public class FileSystemViewModel {} public class FileViewModel:FileSystemViewModel {} public class FolderViewModel:FileSystemViewModel {}
 public interface IContextMenuHost {} public class Host:IContextMenuHost {}
 public class ContextMenuService {
  private IContextMenuHost? _lastMenuHost=new Host();
  private Microsoft.UI.Xaml.FrameworkElement? _lastMenuElement=new();
  private FileSystemViewModel? _lastMenuTarget=new FolderViewModel();private string? _lastEmptyAreaFolderPath;
  private Windows.Foundation.Point _lastMenuPosition;
  private MenuFlyout? _activeFlyout;private int Builds;
  private Task<MenuFlyout> BuildFolderMenuAsync(FolderViewModel folder,IContextMenuHost host,bool forceShellExtensions){Builds++;return Task.FromResult(new MenuFlyout());}
  private Task<MenuFlyout> BuildFileMenuAsync(FileViewModel file,IContextMenuHost host,bool forceShellExtensions){Builds++;return Task.FromResult(new MenuFlyout());}
  private Task<MenuFlyout> BuildEmptyAreaMenuAsync(string path,IContextMenuHost host,bool forceShellExtensions){Builds++;return Task.FromResult(new MenuFlyout());}
'@
$driver = @'
  public static async Task<string> Run(){
   var service=new ContextMenuService();var original=new MenuFlyout();service._activeFlyout=original;
   original.Closed+=(_,_)=>service._activeFlyout=null;
   var rebuilding=service.RebuildMenuWithShellExtensionsAsync();
   if(service.Builds!=0 || MenuFlyout.Shows!=0 || rebuilding.IsCompleted)
    throw new Exception("Manual shell loading rebuilds/reopens before the original flyout Closed event.");
   original.CompleteClose();
   if(service.Builds!=0)throw new Exception("Replacement popup was built within the Closed callback instead of deferred dispatch.");
   original.DispatcherQueue.Flush();service._lastMenuElement!.DispatcherQueue.Flush();
   await rebuilding.WaitAsync(TimeSpan.FromSeconds(2));
   if(service.Builds!=1 || MenuFlyout.Shows!=1)throw new Exception("Manual shell loading did not reopen exactly once after dismissal.");
   return "PASS: manual shell extension loading waits for Closed and a dispatcher turn before reopening exactly once";
  }
 }
}
'@
Add-Type -TypeDefinition ($header+$method+$driver)
[OrielFolder.Services.ContextMenuService]::Run().GetAwaiter().GetResult()
