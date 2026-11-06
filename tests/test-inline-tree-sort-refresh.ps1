$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Views\DetailsModeView.InlineTree.cs'))
$method = [regex]::Match($source, '(?sm)^    private void OnTreeExplorerChanged\(.*?(?=^    private void OnInlineTreeChanged)').Value
if (-not $method) { throw 'Tree refresh handler not found.' }
$driver = @'
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.ComponentModel;
public class Folder { public bool IsSorting; }
public class ExplorerViewModel { public Folder CurrentFolder=new(); public object CurrentItems; public string CurrentPath; }
public class Dispatch {
    public Queue<Action> Work=new();
    public bool TryEnqueue(Action action) { Work.Enqueue(action); return true; }
}
public class TreeRefreshProbe {
    public bool _treeRefreshQueued, _isCleanedUp, _isLoaded=true;
    public object _unfilteredItems;
    public ExplorerViewModel _viewModel=new();
    public Dispatch DispatcherQueue=new();
    public int Sorts;
    void ApplyCurrentView() {
        Sorts++;
        _viewModel.CurrentFolder.IsSorting=true;
        OnTreeExplorerChanged(_viewModel,new(nameof(ExplorerViewModel.CurrentItems)));
        _viewModel.CurrentFolder.IsSorting=false;
    }
    public static string Run() {
        var probe=new TreeRefreshProbe();
        probe.OnTreeExplorerChanged(probe._viewModel,new(nameof(ExplorerViewModel.CurrentItems)));
        int turns=0;
        while(probe.DispatcherQueue.Work.TryDequeue(out var next)) {
            if(++turns>5) throw new Exception("Sorting recursively schedules itself after CurrentItems changes");
            next();
        }
        if(probe.Sorts!=1) throw new Exception("A genuine folder update must rebuild exactly once");
        probe._viewModel.CurrentFolder.IsSorting=true;
        probe.OnTreeExplorerChanged(probe._viewModel,new(nameof(ExplorerViewModel.CurrentItems)));
        if(probe.DispatcherQueue.Work.Count!=0) throw new Exception("Sort notifications must not enqueue another sort");
        return "PASS: real Details refresh handler rebuilds external folder changes once without a self-sorting loop";
    }
'@
Add-Type -TypeDefinition ($driver + $method + "`n}")
[TreeRefreshProbe]::Run()
