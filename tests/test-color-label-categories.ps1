$ErrorActionPreference='Stop'
$source=Get-Content (Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\ViewModels\ExplorerViewModel.ColorLabels.cs') -Raw
$source=$source.Replace('namespace OrielFolder.ViewModels;','namespace OrielFolder.ViewModels {')+"`n}"
$stubs=@'
namespace Microsoft.Extensions.DependencyInjection { public static class Extensions {public static T GetRequiredService<T>(this object o) => (T)(typeof(T)==typeof(OrielFolder.Services.SettingsService)?(object)OrielFolder.Services.SettingsService.Instance:OrielFolder.Services.FolderTagService.Instance);} }
namespace OrielFolder.Models {
 public enum FolderTagColor {None,Red,Blue}
 public class FolderItem {public string Name="",Path="";public bool IsHidden;}
 public class FileItem:FolderItem {public long Size;public System.DateTime DateModified;public string FileType="";}
}
namespace OrielFolder.Services {
 public class SettingsService {public static SettingsService Instance=new();public bool FolderTagsEnabled=true;}
 public class FolderTagService {public static FolderTagService Instance=new();public System.Collections.Generic.List<string> Paths=new(); public string GetName(OrielFolder.Models.FolderTagColor c)=>"";public string[] GetTaggedPaths(OrielFolder.Models.FolderTagColor c)=>Paths.ToArray();}
 public static class LocalizationService {public static string L(string s)=>s;}
}
namespace OrielFolder {
 public class App {public static App Current=new();public object Services=new();}
}
namespace OrielFolder.ViewModels {
 public class FileSystemViewModel {public string Name="",Path="",LocationPath="";public bool IsSelected,TagInjected;public OrielFolder.Models.FolderTagColor TagColor;}
 public class FolderViewModel:FileSystemViewModel {
 public FolderViewModel(OrielFolder.Models.FolderItem f,object svc){Name=f.Name;Path=f.Path;}
 public System.Collections.ObjectModel.ObservableCollection<FileSystemViewModel> Children=new();
 #pragma warning disable CS0067
 public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;public event System.EventHandler LoadError;
 #pragma warning restore CS0067
 public void CancelLoading(){}public void MarkAsManuallyPopulated(){}
 }
 public class FileViewModel:FileSystemViewModel {public FileViewModel(OrielFolder.Models.FileItem f){Name=f.Name;Path=f.Path;}}
 public partial class ExplorerViewModel {
 object _fileService=new();public System.Collections.Generic.List<FolderViewModel> Columns=new();
 public string CurrentPath="";public object SelectedFile; public int Notifications;
 void CancelRecursiveSearchInternal(bool restoreColumns){}void FolderVm_PropertyChanged(object s,System.ComponentModel.PropertyChangedEventArgs e){}void OnColumnLoadError(object s,System.EventArgs e){}
 void PushToHistory(string p){}void AddColumn(FolderViewModel f)=>Columns.Add(f);public void NotifyCurrentItemsChanged()=>Notifications++;
 }
}
public static class LabelProbe {
 public static void Run(string root){
  System.IO.Directory.CreateDirectory(root);var folder=System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root,"folder")).FullName;
  var file=System.IO.Path.Combine(root,"one.txt");System.IO.File.WriteAllText(file,"fixture");
  var missing=System.IO.Path.Combine(root,"never-created.txt");var index=OrielFolder.Services.FolderTagService.Instance.Paths;
  index.Add(folder);index.Add(file);index.Add(missing);
  var vm=new OrielFolder.ViewModels.ExplorerViewModel();vm.ShowColorLabelAsync(OrielFolder.Models.FolderTagColor.Red).GetAwaiter().GetResult();
  var rows=vm.Columns[0].Children;if(rows.Count!=2)throw new System.Exception("Missing item not filtered or file/folder missing");
  if(rows[1].LocationPath!=root || rows[1].Path!=file)throw new System.Exception("Original location lost");
  rows[1].IsSelected=true;index.Remove(folder);vm.RefreshColorLabelCategoryAsync().GetAwaiter().GetResult();
  if(vm.Columns[0].Children.Count!=1 || !vm.Columns[0].Children[0].IsSelected)throw new System.Exception("Tag removal or retained selection failed");
  OrielFolder.Services.SettingsService.Instance.FolderTagsEnabled=false;vm.RefreshColorLabelCategoryAsync().GetAwaiter().GetResult();
  if(vm.Columns[0].Children[0].TagColor!=OrielFolder.Models.FolderTagColor.None)throw new System.Exception("Disabled marker visibility ignored");
  vm.Columns.Clear();int notifications=vm.Notifications;vm.RefreshColorLabelCategoryAsync().GetAwaiter().GetResult();
  if(vm.Columns.Count!=0 || vm.Notifications!=notifications)throw new System.Exception("Category restored after navigation away");
 }
}
'@
Add-Type -TypeDefinition ("#nullable enable annotations`n"+$source+"`n"+$stubs)
[LabelProbe]::Run(('D:\codex\Orielfolder-development\label-fixtures\'+[guid]::NewGuid().ToString('N')))
'PASS: production category method loads indexed files/folders, omits missing paths, retains original locations and selection, refreshes removed labels, and leaves navigated-away views alone.'
