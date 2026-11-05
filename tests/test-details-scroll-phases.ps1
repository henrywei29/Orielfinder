$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '..\src\Span\Span\Views\DetailsModeView.xaml.cs') -Raw
$method = [regex]::Match($source, '(?sm)^        private void OnContainerContentChanging\(.*?^        }').Value
if (-not $method) { throw 'Cannot locate production container callback.' }
$fixture = @'
using System;
using System.Collections.Generic;
public class Grid { public double Height; }
public class Container { public object ContentTemplateRoot = new Grid(); }
public class ListViewBase {}
public class ContainerContentChangingEventArgs {
 public bool InRecycleQueue; public uint Phase; public object Item; public int ItemIndex;
 public Container ItemContainer = new();
 public Action<ListViewBase,ContainerContentChangingEventArgs> Next;
 public void RegisterUpdateCallback(Action<ListViewBase,ContainerContentChangingEventArgs> callback) {Next=callback;}
 public void RegisterUpdateCallback(uint phase,Action<ListViewBase,ContainerContentChangingEventArgs> callback) {if(phase!=1)throw new Exception("Unexpected phase");Next=callback;}
}
namespace ViewModels {
 public class FileSystemViewModel { public int Cloud,Git,Tags; }
 public class FolderViewModel : FileSystemViewModel {public int Sizes;public void RequestFolderSizeCalculation(){Sizes++;}}
 public class FileViewModel : FileSystemViewModel {
  public bool IsThumbnailSupported=true,HasThumbnail;public int Loads,Unloads;
  public System.Threading.Tasks.Task LoadThumbnailAsync(){Loads++;return System.Threading.Tasks.Task.CompletedTask;}
  public void UnloadThumbnail(){Unloads++;}
 }
}
namespace Helpers {public static class DebugLogger {public static void Log(string s){throw new Exception(s);}}}
public static class InlineTreeViewHelper {public static int Stripes;public static void ApplyStripe(ListViewBase s,Container c,int i){Stripes++;}}
public class Folder {
 public void InjectCloudStateIfNeeded(ViewModels.FileSystemViewModel f){f.Cloud++;}
 public void InjectGitStateIfNeeded(ViewModels.FileSystemViewModel f){f.Git++;}
 public void InjectTagIfNeeded(ViewModels.FileSystemViewModel f){f.Tags++;}
}
public class Explorer {public Folder CurrentFolder=new();}
public class ScrollPhaseProbe {
 public bool _isCleanedUp=false;bool _gitColumnVisible=true;double _densityRowHeight=28;Explorer _viewModel=new();int layouts;
 void ApplyCellWidths(Grid g){layouts++;}
 public static string Run(){
  var p=new ScrollPhaseProbe();var sender=new ListViewBase();
  var folder=new ViewModels.FolderViewModel();var first=new ContainerContentChangingEventArgs{Item=folder};
  p.OnContainerContentChanging(sender,first);
  if(folder.Sizes+folder.Cloud+folder.Git+folder.Tags!=0)throw new Exception("Initial row display started secondary metadata work before the first frame");
  if(first.Next==null || p.layouts!=1)throw new Exception("Initial row must lay out and schedule metadata");
  var second=new ContainerContentChangingEventArgs{Item=folder,Phase=1,ItemContainer=first.ItemContainer};first.Next(sender,second);
  if(folder.Sizes!=1 || folder.Cloud!=1 || folder.Git!=1 || folder.Tags!=1 || p.layouts!=1)throw new Exception("Metadata phase must preserve all fields without repeating row layout");
  var file=new ViewModels.FileViewModel();var pending=new ContainerContentChangingEventArgs{Item=file};p.OnContainerContentChanging(sender,pending);
  if(file.Loads!=0)throw new Exception("Thumbnail decoding started in first-frame phase");
  var recycled=new ContainerContentChangingEventArgs{Item=file,InRecycleQueue=true,Phase=1};pending.Next(sender,recycled);
  if(file.Loads!=0 || file.Cloud!=0 || file.Unloads!=1 || recycled.Next!=null)throw new Exception("Recycled rows must not load discarded content");
  var visible=new ContainerContentChangingEventArgs{Item=file};p.OnContainerContentChanging(sender,visible);
  visible.Next(sender,new ContainerContentChangingEventArgs{Item=file,Phase=1});
  if(file.Loads!=1 || file.Cloud!=1 || file.Tags!=1)throw new Exception("Visible files must still load metadata and thumbnails");
  var closedFile=new ViewModels.FileViewModel();var closing=new ContainerContentChangingEventArgs{Item=closedFile};p.OnContainerContentChanging(sender,closing);
  p._isCleanedUp=true;closing.Next(sender,new ContainerContentChangingEventArgs{Item=closedFile,Phase=1});
  if(closedFile.Loads!=0 || closedFile.Cloud!=0)throw new Exception("Deferred work must not run after view cleanup");
  return "PASS: first-frame row layout defers metadata; later phase fills fields; recycled rows cancel discarded work";
 }
'@
Add-Type -TypeDefinition ($fixture + $method + '}')
[ScrollPhaseProbe]::Run()
