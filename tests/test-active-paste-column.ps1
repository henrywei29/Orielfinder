$ErrorActionPreference='Stop'
$source=Get-Content (Join-Path $PSScriptRoot '..\src\Span\Span\MainWindow.NavigationManager.cs') -Raw
$method=[regex]::Match($source,'(?sm)^        private int GetCurrentColumnIndex\(\).*?^        }').Value
$driver=@'
using System;
using System.Collections.Generic;
class Column {public bool IsActive;public object SelectedChild;}
class Explorer {public List<Column> Columns=new();}
class VM {public Explorer ActiveExplorer=new();}
public class Probe {
 VM ViewModel=new();int Focused=0;int GetActiveColumnIndex()=>Focused;
 __METHOD__
 public static void Run(){var p=new Probe();p.ViewModel.ActiveExplorer.Columns.Add(new(){SelectedChild=new()});p.ViewModel.ActiveExplorer.Columns.Add(new(){IsActive=true});
 if(p.GetCurrentColumnIndex()!=1)throw new Exception("Old keyboard focus overrides the explicitly active destination");
 p.Focused=-1;if(p.GetCurrentColumnIndex()!=1)throw new Exception("Toolbar focus restores an older selection instead of the active directory");
 p.ViewModel.ActiveExplorer.Columns.Clear();if(p.GetCurrentColumnIndex()!=-1)throw new Exception("Empty explorer has a destination");}
}
'@
Add-Type -TypeDefinition $driver.Replace('__METHOD__',$method)
[Probe]::Run()
'PASS: active destination wins over stale keyboard focus and old selected items; empty view has no target.'
