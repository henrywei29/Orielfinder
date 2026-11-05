param([string]$SourcePath=(Join-Path $PSScriptRoot '..\src\Span\Span\Views\DetailsModeView.xaml.cs'))
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText($SourcePath)
$method=[regex]::Match($source,'(?sm)^        private void OnDetailsSelectionChanged\(.*?^        }').Value
if(!$method){throw 'Selection handler missing'}
$driver=@'
using System;
using System.Collections.Generic;
public class SelectionChangedEventArgs:EventArgs{}
public class ListView {public List<object> SelectedItems=new();}
public class Folder {public int Count;public void SyncSelectedItems(List<object> items){Count=items.Count;}}
public class VM {public Folder CurrentFolder=new();public void UpdateStatusBar(){}}
public class MainWindow {public VM ViewModel=new();public bool ToolbarNotified;public void NotifyFileSelectionChanged(){ToolbarNotified=true;}}
public class Probe {
 bool _isSyncingSelection=false; VM ViewModel=new();object ContextMenuHost=new MainWindow();
 __METHOD__
 public static void Run(){var p=new Probe();var list=new ListView();list.SelectedItems.Add(new object());p.OnDetailsSelectionChanged(list,new());
 if(p.ViewModel.CurrentFolder.Count!=1)throw new Exception("Selection not synchronized");
 if(!((MainWindow)p.ContextMenuHost).ToolbarNotified)throw new Exception("Selected row leaves tag/actions buttons disabled");}
}
'@
Add-Type -TypeDefinition $driver.Replace('__METHOD__',$method)
[Probe]::Run()
'PASS: production Details selection handler synchronizes selection and notifies toolbar state.'
