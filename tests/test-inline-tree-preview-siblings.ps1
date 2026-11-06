$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.KeyboardHandler.cs'))
$method = [regex]::Match($source, '(?sm)^        private void SetQuickLookSiblings\(.*?^        }').Value
if (-not $method) { throw 'Preview sibling resolver not found.' }
$driver = @'
#nullable enable annotations
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace Models { public enum ViewMode { MillerColumns,Details,List,IconMedium } public enum ActivePane { Left,Right } }
public class FileSystemViewModel { public string Path; public string Name=>System.IO.Path.GetFileName(Path); public FileSystemViewModel(string path){Path=path;} }
public class FolderViewModel:FileSystemViewModel { public FolderViewModel(string path):base(path){} public FileSystemViewModel SelectedChild; public ObservableCollection<FileSystemViewModel> Children=new(); }
public class Explorer { public FolderViewModel CurrentFolder=new("root"); public List<FolderViewModel> Columns=new(); }
public class MainVm { public Explorer ActiveExplorer=new(); public Models.ViewMode CurrentViewMode,RightViewMode; public bool IsSplitViewEnabled; public Models.ActivePane ActivePane; }
public class Pane { public Models.ViewMode Mode; }
public class Host { public List<FileSystemViewModel> Items=new(); }
public class View { public Host ItemsHost=new(); }
public class Preview { public int Count,Index; public void SetSiblingItems(IReadOnlyList<FileSystemViewModel> items,int index){Count=items.Count;Index=index;} }
namespace Helpers { public static class DebugLogger { public static void Log(string value){} } }
public class PreviewProbe {
    public MainVm ViewModel=new(); public Pane ActiveWorkspaceView;
    public Preview _quickLookWindow=new(); public View _view=new();
    View GetActiveDetailsView()=>_view;
    View GetActiveListView()=>_view;
    public static string Run() {
        var probe=new PreviewProbe(); probe.ViewModel.CurrentViewMode=Models.ViewMode.Details;
        var a=new FolderViewModel("A"); var b=new FolderViewModel("B"); var text=new FileSystemViewModel("root.txt");
        var image=new FileSystemViewModel("A/image.png"); var pdf=new FileSystemViewModel("B/document.pdf");
        probe.ViewModel.ActiveExplorer.CurrentFolder.Children.Add(a); probe.ViewModel.ActiveExplorer.CurrentFolder.Children.Add(b); probe.ViewModel.ActiveExplorer.CurrentFolder.Children.Add(text);
        probe._view.ItemsHost.Items.AddRange(new FileSystemViewModel[]{a,image,b,pdf,text});
        probe.SetQuickLookSiblings(pdf);
        if(probe._quickLookWindow.Count!=5 || probe._quickLookWindow.Index!=3) throw new Exception("Nested preview resolves root siblings/index zero rather than visible tree rows");
        probe.ViewModel.CurrentViewMode=Models.ViewMode.IconMedium; probe.SetQuickLookSiblings(text);
        if(probe._quickLookWindow.Count!=3 || probe._quickLookWindow.Index!=2) throw new Exception("Non-tree preview changed");
        probe.ViewModel.CurrentViewMode=Models.ViewMode.MillerColumns; probe.ViewModel.RightViewMode=Models.ViewMode.List;
        probe.ViewModel.ActivePane=Models.ActivePane.Right; probe.ViewModel.IsSplitViewEnabled=true;
        probe.SetQuickLookSiblings(pdf);
        if(probe._quickLookWindow.Count!=5 || probe._quickLookWindow.Index!=3) throw new Exception("Preview must use the active split pane mode");
        return "PASS: actual preview sibling resolver uses visible tree rows/index and active pane while keeping non-tree fallback";
    }
'@
Add-Type -TypeDefinition ($driver + $method + "`n}")
[PreviewProbe]::Run()
