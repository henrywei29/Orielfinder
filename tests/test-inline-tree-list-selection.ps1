$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Views\ListModeView.InlineTree.cs'))
$method = [regex]::Match($source, '(?sm)^    private void RestoreTreeSelection\(.*?(?=^    private )').Value
if (-not $method) { throw 'List selection restoration not found.' }
$driver = @'
#nullable enable annotations
using System;
using System.Linq;
using System.Collections.Generic;
public class FileSystemViewModel { public string Path,Name; public FileSystemViewModel(string path,string name=null){Path=path;Name=name??path;} }
public class Folder { public FileSystemViewModel SelectedChild; public List<object> SelectedItems=new(); public void SyncSelectedItems(IList<object> items){SelectedItems=items.ToList(); if(items.Count==1)SelectedChild=(FileSystemViewModel)items[0];} }
public class Explorer { public Folder CurrentFolder=new(); }
public class GridBoundary {
    public List<FileSystemViewModel> Items=new(); public List<object> SelectedItems=new();
    public object SelectedItem { set {SelectedItems.Clear();if(value!=null)SelectedItems.Add(value);} }
}
public class ListSelectionProbe {
    public List<FileSystemViewModel> _listItems;
    public GridBoundary ListGridView=new(); public Explorer ViewModel=new(); public bool _isSyncingSelection;
    ListSelectionProbe(){_listItems=ListGridView.Items;}
    bool IsParentDotDot(FileSystemViewModel item)=>item.Name=="..";
    public static string Run(){
        var oldA=new FileSystemViewModel("A/file"); var oldB=new FileSystemViewModel("B/file");
        var freshA=new FileSystemViewModel("A/file"); var freshB=new FileSystemViewModel("B/file"); var parent=new FileSystemViewModel("parent","..");
        var p=new ListSelectionProbe();p._listItems.AddRange(new[]{parent,freshB,freshA});p.ListGridView.SelectedItem=oldA;p.ViewModel.CurrentFolder.SelectedChild=oldA;
        p.RestoreTreeSelection(oldA,new[]{oldA.Path});
        if(p.ListGridView.SelectedItems.Count!=1 || !ReferenceEquals(freshA,p.ListGridView.SelectedItems[0]) || !ReferenceEquals(freshA,p.ViewModel.CurrentFolder.SelectedChild))
            throw new Exception("List refresh selects parent/old VM instead of the new visible same-path file");
        p.RestoreTreeSelection(oldA,new[]{oldA.Path,oldB.Path});
        if(p.ListGridView.SelectedItems.Count!=2 || p.ViewModel.CurrentFolder.SelectedItems.Count!=2) throw new Exception("List multi-selection was lost");
        p._listItems.Remove(freshA);p.RestoreTreeSelection(oldA,new[]{oldA.Path});
        if(p.ViewModel.CurrentFolder.SelectedChild!=null || p.ViewModel.CurrentFolder.SelectedItems.Count!=0) throw new Exception("Hidden/collapsed files remain actionable");
        if(p._isSyncingSelection) throw new Exception("Selection guard was not reset");
        return "PASS: actual List selection restoration maps paths to current VMs, retains multi-selection and clears hidden targets";
    }
'@
Add-Type -TypeDefinition ($driver + $method + "`n}")
[ListSelectionProbe]::Run()
