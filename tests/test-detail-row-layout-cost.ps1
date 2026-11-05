$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\Views\DetailsModeView.Columns.cs'))
$method=[regex]::Match($source,'(?sm)^    private void ApplyCellWidths\(.*?^    }').Value
$stub=@'
#nullable enable annotations
using System;
using System.Linq;
using System.Collections.Generic;
public enum Visibility {Visible,Collapsed}
public record struct Thickness(double L,double T,double R,double B) {public Thickness(double all):this(all,all,all,all){}}
public class Grid {public List<Border> Children=new();public static int Writes;public static int GetColumn(Border b)=>b.Column;public static void SetColumn(Border b,int n){Writes++;b.Column=n;}}
public class Border {
 public object Tag;public int Column;double width=double.NaN;Thickness margin;Visibility visibility;
 public double Width {get=>width;set{Grid.Writes++;width=value;}}
 public Thickness Margin {get=>margin;set{Grid.Writes++;margin=value;}}
 public Visibility Visibility {get=>visibility;set{Grid.Writes++;visibility=value;}}
}
public class Length {public bool IsAbsolute=true;public double Value=100;}
public class Definition {public Length Width=new();public double MinWidth=50;}
public class Column {public Definition Definition=new();}
public class LayoutCostProbe {
 string[] _columnOrder={"Name","Size","Type","Date","Tag","Cloud","Location","Git"};
 Dictionary<string,Column> _detailColumns=new();Dictionary<string,double> _detailColumnWidths=new();
 bool showSize=true;bool IsDetailColumnVisible(string id)=>id!="Size" || showSize;
 public static string Run(){
  var p=new LayoutCostProbe();var grid=new Grid();foreach(var id in p._columnOrder){p._detailColumns[id]=new();grid.Children.Add(new Border{Tag=id});}
  p.ApplyCellWidths(grid);Grid.Writes=0;
  for(int i=0;i<10000;i++)p.ApplyCellWidths(grid);
  if(Grid.Writes!=0)throw new Exception("Unchanged row layout issued "+Grid.Writes+" dependency-property writes");
  p._detailColumnWidths["Size"]=240;p.ApplyCellWidths(grid);
  if(grid.Children[1].Width!=240 || Grid.Writes!=1)throw new Exception("Changed width must update only that cell");
  p.showSize=false;p.ApplyCellWidths(grid);
  if(grid.Children[1].Visibility!=Visibility.Collapsed || grid.Children[1].Width!=0)throw new Exception("Column visibility no longer applied");
  return "PASS: 10000 unchanged row layouts write zero properties; resizing and visibility still update";
 }
'@
Add-Type -TypeDefinition ($stub+$method+'}')
[LayoutCostProbe]::Run()
