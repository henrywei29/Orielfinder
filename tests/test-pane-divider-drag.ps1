$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
$source=Get-Content "$root\MainWindow.PaneSizing.cs" -Raw
$source=$source.Substring($source.IndexOf('namespace OrielFolder'))
$fixture=@'
#nullable enable annotations
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using OrielFolder.Models;
namespace Microsoft.UI.Input { public enum InputSystemCursorShape {SizeWestEast} }
namespace Microsoft.UI.Xaml {
 public enum GridUnitType {Pixel,Star}
 public struct GridLength {public double Value; public GridLength(double v,GridUnitType t=GridUnitType.Pixel){Value=v;}}
 public class RoutedEventArgs{} public class SizeChangedEventArgs{}
 public class Element { public GridLength Width; public double ActualWidth; public bool CapturePointer(Pointer p)=>true; public void ReleasePointerCapture(Pointer p){} }
}
namespace Microsoft.UI.Xaml.Input {
 public class Pointer {public uint PointerId=1;}
 public class Point {public double X;}
 public class Properties {public bool IsLeftButtonPressed=true;}
 public class PointerPoint {public Point Position=new();public Properties Properties=new();}
 public class PointerRoutedEventArgs {public Pointer Pointer=new();public PointerPoint Point=new();public bool Handled;public PointerPoint GetCurrentPoint(object e)=>Point;}
}
namespace OrielFolder.Models {
 public class TabItem {public double SplitRatio=.5;}
 public enum ViewMode {MillerColumns,Settings,ActionLog,RecycleBin}
 public static class PaneLayoutGeometry {public static double ClampSplitRatio(double width,double ratio){double min=Math.Min(150/width,.5);return Math.Clamp(ratio,min,1-min);}}
}
namespace OrielFolder.Helpers {public static class CursorHelper {public static void SetCursor(object e,Microsoft.UI.Input.InputSystemCursorShape c){}}}
namespace OrielFolder {
 public class Model {public bool IsSplitViewEnabled=true;public ViewMode CurrentViewMode;public TabItem ActiveTab=new();}
 public sealed partial class MainWindow {
  Model ViewModel=new();Element ClassicPaneHost=new(){ActualWidth=1001},LeftPaneCol=new(){ActualWidth=500},RightPaneCol=new(){ActualWidth=500},SplitterCol=new(),PaneDivider=new();
  public static void Run(){
   var w=new MainWindow(); w.ApplyClassicPaneSizing();
   if(w.SplitterCol.Width.Value!=1)throw new Exception("Split line has no layout space");
   var down=new PointerRoutedEventArgs();down.Point.Position.X=500;w.OnPaneDividerDragStarted(null,down);
   var move=new PointerRoutedEventArgs();move.Point.Position.X=700;w.OnPaneDividerDrag(null,move);
   if(Math.Abs(w.ViewModel.ActiveTab.SplitRatio-.7)>1e-8 || Math.Abs(w.LeftPaneCol.Width.Value-.7)>1e-8 || Math.Abs(w.RightPaneCol.Width.Value-.3)>1e-8)throw new Exception("Drag did not resize both panes");
   move.Point.Position.X=5000;w.OnPaneDividerDrag(null,move);
   if(Math.Abs(w.ViewModel.ActiveTab.SplitRatio-.85)>1e-8)throw new Exception("Other pane collapsed");
   w.OnPaneDividerCaptureLost(null,move);move.Point.Position.X=10;w.OnPaneDividerDrag(null,move);
   if(Math.Abs(w.ViewModel.ActiveTab.SplitRatio-.85)>1e-8)throw new Exception("Continued resizing after capture lost");
   w.OnPaneDividerDragStarted(null,down);var old=w.ViewModel.ActiveTab;w.ViewModel.ActiveTab=new();w.OnPaneDividerDrag(null,move);
   if(w.ViewModel.ActiveTab.SplitRatio!=.5 || old.SplitRatio!=.85)throw new Exception("Drag modified a different tab");
   w.ViewModel.IsSplitViewEnabled=false;w.ApplyClassicPaneSizing();
   if(w.SplitterCol.Width.Value!=0 || w.RightPaneCol.Width.Value!=0 || w.LeftPaneCol.Width.Value!=1)throw new Exception("Single pane left an empty gap");
  }
 }
}
'@
Add-Type -TypeDefinition ($fixture+$source) -WarningAction SilentlyContinue
[OrielFolder.MainWindow]::Run()
'PASS: production drag handlers resize both panes, clamp minimum width, stop on capture loss, protect other tabs and remove single-pane gap.'
