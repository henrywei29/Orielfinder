$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\Views\ListModeView.xaml.cs'))
$sync = [regex]::Match($source, '(?sm)^        private void SyncListItems\(.*?^        }').Value
$navigate = [regex]::Match($source, '(?sm)^        private void OnExplorerPropertyChanged\(.*?^        }').Value
if (-not $sync -or -not $navigate) { throw 'List refresh methods not found.' }
$driver = @'
#nullable enable annotations
using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
namespace ViewModels { public record FileSystemViewModel(string Path); }
public class ExplorerViewModel { public object CurrentFolder,CurrentItems; public string CurrentPath; }
public class Dispatch { public void TryEnqueue(Action action)=>action(); }
public class ListRefreshProbe {
    public ObservableCollection<ViewModels.FileSystemViewModel> _listItems=new();
    public Dispatch DispatcherQueue=new();
    public string _currentGroupBy="Type";
    public bool _isCleanedUp;
    public int Plain,Grouped;
    void RebuildListItems()=>Plain++;
    void RebuildGroupedListItems()=>Grouped++;
    public static string Run() {
        var probe=new ListRefreshProbe();
        probe.OnExplorerPropertyChanged(null,new(nameof(ExplorerViewModel.CurrentPath)));
        if(probe.Grouped!=1 || probe.Plain!=0) throw new Exception("Grouped navigation rebuilds inactive plain source, leaving old grouped rows visible");
        probe._currentGroupBy="None";
        probe.OnExplorerPropertyChanged(null,new(nameof(ExplorerViewModel.CurrentItems)));
        if(probe.Plain!=1) throw new Exception("Plain navigation did not rebuild");
        var a=new ViewModels.FileSystemViewModel("A"); var b=new ViewModels.FileSystemViewModel("B");
        var nextA=new ViewModels.FileSystemViewModel("A"); var nextB=new ViewModels.FileSystemViewModel("B");
        probe._listItems.Add(a); probe._listItems.Add(b);
        probe.SyncListItems(new(){nextB,nextA});
        if(!ReferenceEquals(probe._listItems[0],nextB) || !ReferenceEquals(probe._listItems[1],nextA))
            throw new Exception("Reordered same-path row retains an obsolete VM instead of refreshed file identity");
        return "PASS: actual List methods rebuild the active grouped/plain source and replace moved stale VM references";
    }
'@
$rows = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\Helpers\VisibleRows.cs'))
$rows = [regex]::Replace($rows, '(?m)^using .*;\r?\n', '') -replace 'namespace Span.Helpers;', 'namespace Helpers {'
Add-Type -TypeDefinition ($driver + $sync + $navigate + "`n}" + $rows + "`n}")
[ListRefreshProbe]::Run()
