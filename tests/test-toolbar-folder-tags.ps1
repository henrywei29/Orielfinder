$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '..\src\Span\Span\MainWindow.FolderTags.cs') -Raw
$method = [regex]::Match($source, '(?sm)^    private async Task ApplyToolbarFolderTagAsync\(FolderTagColor color\).*?^    }').Value
if (!$method) { throw 'Toolbar tag action is missing.' }
$fixture = @'
#nullable enable annotations
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
enum FolderTagColor { None, Red }
namespace ViewModels { public class FileViewModel { public string Path=""; } public class FolderViewModel:FileViewModel {} }
class FolderTagService {
 public List<string> Writes=new(); public bool Fail; public FolderTagColor Last;
 public string SetTag(string p,FolderTagColor c){ lock(Writes) Writes.Add(p); Last=c; return Fail?"failed":null; }
}
class Provider { public FolderTagService Tags=new(); public object GetService(Type t)=>Tags; }
class App { public static App Current=new(); public Provider Services=new(); }
class Settings { public bool FolderTagsEnabled=true; }
class VM { public object ActiveExplorer=new(); public string Error; public void ShowToast(string s,int ms,bool isError){Error=s;} }
public class Probe {
 Settings _settings=new(); bool _isClosed; VM ViewModel=new(); int Refreshes;
 List<ViewModels.FileViewModel> Selected=new();
 List<ViewModels.FileViewModel> GetCurrentSelectedItems()=>Selected;
 void RefreshFolderTags(){Refreshes++;}
 __METHOD__
 public static void Run(){
  var p=new Probe(); var tags=App.Current.Services.Tags;
  p.Selected.Add(new(){Path="file.txt"});
  p.ApplyToolbarFolderTagAsync(FolderTagColor.Red).GetAwaiter().GetResult();
  if(tags.Writes.Count!=1)throw new Exception("Ordinary files must receive tags");
  p.Selected.Clear();
  p.Selected.Add(new ViewModels.FolderViewModel(){Path="D:\\a"});
  p.Selected.Add(new ViewModels.FolderViewModel(){Path="d:\\A"});
  var task=p.ApplyToolbarFolderTagAsync(FolderTagColor.Red);
  p.Selected.Clear(); p.Selected.Add(new ViewModels.FolderViewModel(){Path="D:\\other"});
  task.GetAwaiter().GetResult();
  if(tags.Writes.Count!=2 || tags.Writes[1]!="D:\\a")throw new Exception("Selection must be snapshotted and deduplicated before yielding");
  p.ApplyToolbarFolderTagAsync(FolderTagColor.None).GetAwaiter().GetResult();
  if(tags.Last!=FolderTagColor.None)throw new Exception("Clear tag failed");
  p._settings.FolderTagsEnabled=false;
  p.ApplyToolbarFolderTagAsync(FolderTagColor.Red).GetAwaiter().GetResult();
  if(tags.Writes.Count!=4 || !p._settings.FolderTagsEnabled)throw new Exception("Explicit tag action must enable tag display");
  p._settings.FolderTagsEnabled=true; tags.Fail=true;
  p.ApplyToolbarFolderTagAsync(FolderTagColor.Red).GetAwaiter().GetResult();
  if(p.ViewModel.Error!="failed")throw new Exception("Write failure is hidden");
  p._isClosed=true;
  p.ApplyToolbarFolderTagAsync(FolderTagColor.Red).GetAwaiter().GetResult();
  if(tags.Writes.Count!=5)throw new Exception("Closed window still writes");
 }
}
'@
Add-Type -TypeDefinition $fixture.Replace('__METHOD__',$method) -WarningAction SilentlyContinue
[Probe]::Run()
'PASS: files and folders, selection snapshot, duplicate paths, clear color, explicit activation, errors and closed window.'
