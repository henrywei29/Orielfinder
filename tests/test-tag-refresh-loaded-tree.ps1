param([string]$SourcePath=(Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.FolderTags.cs'))
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText($SourcePath)
$method=[regex]::Match($source,'(?sm)^    internal void RefreshFolderTags\(\).*?^    }').Value
if(!$method){throw 'Tag refresh method missing'}
$driver=@'
using System;
using System.Linq;
using System.Collections.Generic;
enum FolderTagColor {None,Blue}
namespace ViewModels {
 class Item {public string Path="";public FolderTagColor TagColor;public bool TagInjected;public int Notifications;public void NotifyTagTextChanged(){Notifications++;}}
 class FolderViewModel:Item {public List<Item> Children=new();}
}
class FolderTagService {public FolderTagColor? GetCachedTag(string p)=>FolderTagColor.Blue;}
class Services {public object GetService(Type t)=>new FolderTagService();}
class App {public static App Current=new(); public Services Services=new();}
class Settings {public bool FolderTagsEnabled=true;}
class Explorer {public System.Threading.Tasks.Task RefreshColorLabelCategoryAsync()=>System.Threading.Tasks.Task.CompletedTask;public List<ViewModels.FolderViewModel> Columns=new();public ViewModels.FolderViewModel CurrentFolder=new();}
class VM {public List<Explorer> Explorers=new();public IEnumerable<Explorer> EnumerateOwnedExplorers()=>Explorers;}
public class Probe {
 bool _isClosed=false; VM ViewModel=new();Settings _settings=new();void RefreshToolbarFolderTags(){} void RefreshSidebarColorLabels(){}
 __METHOD__
 public static void Run(){
  var p=new Probe();var details=new Explorer();var otherPane=new Explorer();
  var file=new ViewModels.Item(){Path="file.pdf"};var nested=new ViewModels.FolderViewModel(){Path="sub"};var child=new ViewModels.Item(){Path="sub/child.txt"};var other=new ViewModels.Item(){Path="other.docx"};
  details.CurrentFolder.Children.Add(file);details.CurrentFolder.Children.Add(nested);nested.Children.Add(child);otherPane.Columns.Add(otherPane.CurrentFolder);otherPane.CurrentFolder.Children.Add(other);
  p.ViewModel.Explorers.Add(details);p.ViewModel.Explorers.Add(otherPane);
  p.RefreshFolderTags();
  foreach(var item in new ViewModels.Item[]{file,nested,child,other})if(item.TagColor!=FolderTagColor.Blue || item.Notifications!=1)throw new Exception("Current-folder, nested or split-pane item did not refresh exactly once");
  p._settings.FolderTagsEnabled=false;p.RefreshFolderTags();
  foreach(var item in new ViewModels.Item[]{file,nested,child,other})if(item.TagColor!=FolderTagColor.None)throw new Exception("Disabled tag display did not clear loaded rows");
 }
}
'@
Add-Type -TypeDefinition $driver.Replace('__METHOD__',$method)
[Probe]::Run()
'PASS: real tag-refresh method covers ordinary files, nested tree rows and split panes once, without filesystem enumeration.'
