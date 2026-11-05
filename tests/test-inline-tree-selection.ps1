$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\Views\DetailsModeView.InlineTree.cs'))
$method = [regex]::Match($source, '(?sm)^    private void SetTreeItemsSource\(.*?(?=^    private )').Value
if (-not $method) { throw 'Selection-preserving source setter not found.' }
$driver = @'
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using Span.Helpers;
namespace Microsoft.UI.Xaml.Data {
 public class CollectionViewSource {
  public object Source { get; set; } public bool IsSourceGrouped {get;set;}
  private object _view;
  public object View=>_view??=Flatten();
  private IEnumerable<FileSystemViewModel> Flatten(){foreach(var group in (IEnumerable<VisibleRowGroup<FileSystemViewModel>>)Source) foreach(var item in group) yield return item;}
 }
}
public class FileSystemViewModel { public string Path; public FileSystemViewModel(string path) { Path=path; } }
public class Folder {
    public FileSystemViewModel SelectedChild;
    public List<FileSystemViewModel> SelectedItems=new();
    public void SyncSelectedItems(IEnumerable<FileSystemViewModel> items) {
        SelectedItems=items.ToList(); SelectedChild=SelectedItems.Count==1?SelectedItems[0]:null;
    }
}
public class Explorer { public Folder CurrentFolder=new(); }
public class ListBoundary {
    public bool PreserveOnReset;
    public Folder Folder;
    public List<FileSystemViewModel> Items=>_source?.ToList()??new();
    public List<FileSystemViewModel> SelectedItems=new();
    private IEnumerable<FileSystemViewModel> _source;
    public int SourceAssignments;
    private void Prune()=>SelectedItems.RemoveAll(item=>!Items.Contains(item));
    public object ItemsSource { get=>_source; set { SourceAssignments++; _source=(IEnumerable<FileSystemViewModel>)value;
        if(value is System.Collections.Specialized.INotifyCollectionChanged observable) observable.CollectionChanged+=(_,_)=>Prune();
        if(!PreserveOnReset) {SelectedItems.Clear();Folder.SelectedChild=null;} else Prune(); } }
    public object SelectedItem { set { SelectedItems.Clear(); if(value is FileSystemViewModel item) SelectedItems.Add(item); } }
}
public class SelectionProbe {
    public readonly ObservableCollection<FileSystemViewModel> _treeRows=new();
    public readonly ObservableCollection<VisibleRowGroup<FileSystemViewModel>> _treeGroups=new();
    public Microsoft.UI.Xaml.Data.CollectionViewSource _treeGroupSource;
    public Explorer ViewModel=new();
    public ListBoundary DetailsListView;
    public bool _isSyncingSelection;
    SelectionProbe() { DetailsListView=new() { Folder=ViewModel.CurrentFolder }; }
    public static string Run() {
        var nested=new FileSystemViewModel("A/deep/file.pdf"); var other=new FileSystemViewModel("B/file.txt");
        var probe=new SelectionProbe(); probe.DetailsListView.SelectedItems.Add(nested); probe.ViewModel.CurrentFolder.SelectedChild=nested;
        probe.SetTreeItemsSource(new[]{other,nested});
        if(probe.DetailsListView.SelectedItems.Count!=1 || !ReferenceEquals(nested,probe.DetailsListView.SelectedItems[0])
            || !ReferenceEquals(nested,probe.ViewModel.CurrentFolder.SelectedChild)) throw new Exception("Expansion rebuild loses nested selection/preview target");
        probe.DetailsListView.PreserveOnReset=true;
        probe.SetTreeItemsSource(new[]{other,nested});
        if(probe.DetailsListView.SelectedItems.Count!=1) throw new Exception("Selection binding already preserved an item; restoration must not duplicate it");
        probe.DetailsListView.PreserveOnReset=false;
        var replacement=new FileSystemViewModel(nested.Path);
        probe.SetTreeItemsSource(new[]{replacement,other});
        if(probe.DetailsListView.SelectedItems.Count!=1 || !ReferenceEquals(replacement,probe.DetailsListView.SelectedItems[0]))
            throw new Exception("Refresh must restore the selected path using the new visible VM instance");
        probe.DetailsListView.SelectedItems.Add(other);
        probe.SetTreeItemsSource(new[]{nested,other});
        if(probe.ViewModel.CurrentFolder.SelectedItems.Count!=2) throw new Exception("Rebuild loses multi-selection");
        probe.SetTreeItemsSource(Array.Empty<FileSystemViewModel>());
        if(probe.DetailsListView.SelectedItems.Count!=0 || probe.ViewModel.CurrentFolder.SelectedChild!=null)
            throw new Exception("Collapsed/filtered-away files must not remain actionable");
        if(probe.DetailsListView.SourceAssignments!=1) throw new Exception("Tree changes rebound the entire ItemsSource");
        probe.DetailsListView.SelectedItems.Add(nested);
        probe.SetTreeItemsSource(new[]{new VisibleRowGroup<FileSystemViewModel>("Group (2)",new[]{nested,other})});
        var fresh=new FileSystemViewModel(nested.Path);
        probe.SetTreeItemsSource(new[]{new VisibleRowGroup<FileSystemViewModel>("Group (2)",new[]{fresh,other})});
        if(probe.DetailsListView.SelectedItems.Count!=1 || !ReferenceEquals(fresh,probe.DetailsListView.SelectedItems[0])) throw new Exception("Grouped replacement loses selected path");
        if(probe.DetailsListView.SourceAssignments!=2) throw new Exception("Grouped expansion rebound stable source");
        if(probe._isSyncingSelection) throw new Exception("Selection guard remained set");
        return "PASS: actual Details source setter retains visible nested/multiple selection and clears hidden action targets";
    }
'@
$rows = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\Helpers\VisibleRows.cs'))
$rows = [regex]::Replace($rows, '(?m)^using .*;\r?\n', '') -replace 'namespace Span.Helpers;', 'namespace Span.Helpers {'
Add-Type -TypeDefinition ($driver + $method + "`n}" + $rows + "`n}")
[SelectionProbe]::Run()
