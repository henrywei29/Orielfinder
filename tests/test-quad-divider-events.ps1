$ErrorActionPreference='Stop'
$source=Get-Content (Join-Path $PSScriptRoot '..\src\Span\Span\MainWindow.WorkspaceLayout.cs') -Raw
$method=[regex]::Match($source,'(?sm)^        private void AddWorkspaceDivider\(.*?^        }').Value
$fixture=@'
#nullable enable annotations
using System;
using System.Linq;
using System.Collections.Generic;
class Point {public double X,Y;}
class Props {public bool IsLeftButtonPressed=true;}
class PointerPoint {public Point Position=new();public Props Properties=new();}
class E:EventArgs {public object Pointer=new();public bool Handled;public PointerPoint P=new();public PointerPoint GetCurrentPoint(object? el)=>P;}
class Border {
 public double Width,Height,Left,Top;public object? Background;public bool Captured;public Microsoft.UI.Input.InputSystemCursorShape Cursor;
 public event EventHandler<E>? PointerEntered,PointerPressed,PointerMoved,PointerReleased,PointerCaptureLost;
 public bool CapturePointer(object p){Captured=true;return true;}public void ReleasePointerCapture(object p){Captured=false;PointerCaptureLost?.Invoke(this,new());}
 public E Fire(string kind,double x,double y){var e=new E();e.P.Position.X=x;e.P.Position.Y=y;
 switch(kind){case "enter":PointerEntered?.Invoke(this,e);break;case "press":PointerPressed?.Invoke(this,e);break;case "move":PointerMoved?.Invoke(this,e);break;case "release":PointerReleased?.Invoke(this,e);break;case "lost":PointerCaptureLost?.Invoke(this,e);break;}return e;}
}
class Canvas {public double ActualWidth=1000,ActualHeight=600;public List<Border> Children=new();public static void SetLeft(Border b,double v)=>b.Left=v;public static void SetTop(Border b,double v)=>b.Top=v;public static double GetLeft(Border b)=>b.Left;public static double GetTop(Border b)=>b.Top;public static void SetZIndex(Border b,int z){} }
namespace Microsoft.UI.Input {public enum InputSystemCursorShape {SizeWestEast,SizeNorthSouth}}
namespace Helpers {static class CursorHelper {public static void SetCursor(Border b,Microsoft.UI.Input.InputSystemCursorShape s)=>b.Cursor=s;}}
class WorkspaceLayoutState {public double[] ColumnWeights=Array.Empty<double>(),RowWeights=Array.Empty<double>();}
public class QuadProbe {
 Canvas? _workspaceCanvas=new();bool _workspaceDividerDragging;List<Border> _workspaceDividers=new();
 object GetThemeBrush(string s)=>new();void ArrangeWorkspacePanes(){}
 __METHOD__
 public static void Run(){
  var p=new QuadProbe();var state=new WorkspaceLayoutState();p.AddWorkspaceDivider(state,true,0,500,600,2);var v=p._workspaceDividers[0];v.Fire("enter",500,0);
  if(v.Cursor!=Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast)throw new Exception("Vertical cursor");
  if(!v.Fire("press",500,0).Handled)throw new Exception("Press bubbles into selection");v.Fire("move",750,0);
  if(Math.Abs(state.ColumnWeights[0]-1.5)>0.001 || Math.Abs(state.ColumnWeights.Sum()-2)>0.001)throw new Exception("Horizontal resize/adjacent conservation");
  v.Fire("move",-300,0);if(Math.Abs(state.ColumnWeights[0]-.3)>0.001)throw new Exception("Minimum width");v.Fire("release",-300,0);
  if(p._workspaceDividerDragging)throw new Exception("Capture not cleared");
  v.Fire("press",150,0);v.Fire("move",250,0);if(Math.Abs(v.Left-250)>0.001)throw new Exception("Second drag jumps to original position");v.Fire("lost",250,0);
  double previous=state.ColumnWeights[0];v.Fire("move",900,0);if(previous!=state.ColumnWeights[0])throw new Exception("Moves after capture loss");
  p.AddWorkspaceDivider(state,false,0,300,1000,2);var h=p._workspaceDividers[1];h.Fire("enter",0,300);h.Fire("press",0,300);h.Fire("move",0,450);
  if(h.Cursor!=Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth || Math.Abs(state.RowWeights[0]-1.5)>0.001)throw new Exception("Vertical pane resize");
 }
}
'@
Add-Type -TypeDefinition $fixture.Replace('__METHOD__',$method) -IgnoreWarnings
[QuadProbe]::Run()
'PASS: production quad divider events resize both axes, enforce minimums, conserve adjacent weights, retain drag position, capture pointers and stop after capture loss.'
