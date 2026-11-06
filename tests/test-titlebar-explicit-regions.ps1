$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
$main = Get-Content "$root\MainWindow.xaml.cs" -Raw
if ($main -match 'SetTitleBar\(AppTitleBar\)') { throw 'XAML still owns automatic title-bar regions; candidate has not switched to explicit geometry.' }
$source = Get-Content "$root\MainWindow.TabManager.cs" -Raw
$method = [regex]::Match($source, '(?sm)^        private void UpdateTitleBarRegions\(\).*?^        }').Value
if (!$method) { throw 'Production region method missing.' }
$changed = [regex]::Match($main, '(?s)AppWindow.Changed \+= \(_, args\) =>\s*\{(.*?)\n            };').Groups[1].Value
if (!$changed) { throw 'Geometry-only AppWindow handler missing.' }
$fixture = @'
using System;
using System.Linq;
using System.Collections.Generic;
namespace Windows.Foundation { public struct Rect { public double X,Y,Width,Height; public Rect(double x,double y,double w,double h){X=x;Y=y;Width=w;Height=h;} } }
namespace Windows.Graphics { public struct RectInt32 { public int X,Y,Width,Height; public RectInt32(int x,int y,int w,int h){X=x;Y=y;Width=w;Height=h;} } }
public class Root { public double RasterizationScale=1; }
public class GeneralTransform { double x,y; public GeneralTransform(double x,double y){this.x=x;this.y=y;} public Windows.Foundation.Rect TransformBounds(Windows.Foundation.Rect r)=>new(r.X+x,r.Y+y,r.Width,r.Height); }
public class FrameworkElement { public double X,Y,ActualWidth=100,ActualHeight=32; public Root XamlRoot=new(); public GeneralTransform TransformToVisual(object target)=>new(X,Y); }
public class Repeater { public List<FrameworkElement> ItemsSourceView=new(); public FrameworkElement TryGetElement(int i)=>ItemsSourceView[i]; }
public class TitleBar { public double RightInset=100; }
public class AppWin { public TitleBar TitleBar=new(); public int Id=1; }
public class GridLength { public GridLength(double w){} }
public class Column { public GridLength Width; }
public class Change { public bool DidSizeChange,DidPresenterChange,DidVisibilityChange,DidPositionChange; }
public enum NonClientRegionKind { Caption,Passthrough }
public class InputNonClientPointerSource {
 public static InputNonClientPointerSource Instance=new();
 public List<(NonClientRegionKind kind,Windows.Graphics.RectInt32[] rects)> Writes=new();
 public static InputNonClientPointerSource GetForWindowId(int id)=>Instance;
 public void SetRegionRects(NonClientRegionKind k,Windows.Graphics.RectInt32[] r)=>Writes.Add((k,r));
}
public class Probe {
 bool _isClosed; bool ExtendsContentIntoTitleBar=true;
 FrameworkElement TabScrollViewer=new(){X=100,ActualWidth=600};
 FrameworkElement AppTitleBar=new(){ActualWidth=1000};
 FrameworkElement NewTabButton=new(){X=202,ActualWidth=34};
 Repeater TabRepeater=new(); AppWin AppWindow=new(); Column RightPaddingColumn=new();
 double _lastRightPaddingWidth; Windows.Graphics.RectInt32[] _lastTitleBarRects;
 Windows.Graphics.RectInt32? _lastTitleBarCaptionRect;
 int Queued; void QueueUpdateTitleBarRegions(){Queued++;}
 void Changed(Change args) { __CHANGED__ }
 static Windows.Foundation.Rect? GetClippedRect(FrameworkElement el,Windows.Foundation.Rect v){double l=Math.Max(el.X,v.X),r=Math.Min(el.X+el.ActualWidth,v.X+v.Width);return r>l?new(l,el.Y,r-l,el.ActualHeight):null;}
 static Windows.Graphics.RectInt32 ToRectInt32(Windows.Foundation.Rect r,double s)=>new((int)Math.Round(r.X*s),(int)Math.Round(r.Y*s),(int)Math.Round(r.Width*s),(int)Math.Round(r.Height*s));
 public static void Run(){
  var p=new Probe(); p.TabRepeater.ItemsSourceView.Add(new(){X=100});
  var calls=InputNonClientPointerSource.Instance.Writes;
  p.Changed(new(){DidPositionChange=true});
  if(p.Queued!=0)throw new Exception("Position-only window movement schedules layout work");
  p.Changed(new(){DidSizeChange=true});p.Changed(new(){DidPresenterChange=true});p.Changed(new(){DidVisibilityChange=true});
  if(p.Queued!=3)throw new Exception("Size, presenter and visibility changes must schedule geometry updates");
  p.UpdateTitleBarRegions();
  if(calls.Count!=2 || !calls.Any(c=>c.kind==NonClientRegionKind.Caption))throw new Exception("Missing explicit caption and passthrough");
  var cap=calls.Single(c=>c.kind==NonClientRegionKind.Caption).rects.Single();
  if(cap.Width!=900 || cap.Height!=32)throw new Exception("Caption overlaps system buttons or misses title bar");
  var pass=calls.Single(c=>c.kind==NonClientRegionKind.Passthrough).rects;
  if(pass.Length!=2 || pass[0].X!=100 || pass[1].X!=202)throw new Exception("Tabs and plus must remain interactive");
  for(int i=0;i<100;i++)p.UpdateTitleBarRegions();
  if(calls.Count!=2)throw new Exception("Unchanged geometry repeats native writes");
  p.AppTitleBar.ActualWidth=1200;p.UpdateTitleBarRegions();
  if(calls.Count!=3 || calls.Last().rects[0].Width!=1100)throw new Exception("Resize must update only caption");
  p.AppTitleBar.XamlRoot.RasterizationScale=2;p.UpdateTitleBarRegions();
  if(calls.Count!=5 || calls.Last(c=>c.kind==NonClientRegionKind.Caption).rects[0].Width!=2300)throw new Exception("DPI change lost physical caption geometry");
  p._lastTitleBarRects=null;p.UpdateTitleBarRegions();
  if(calls.Count!=6 || calls.Last().kind!=NonClientRegionKind.Passthrough)throw new Exception("Drag completion must restore passthrough");
  p._isClosed=true;p.AppTitleBar.ActualWidth=1400;p.UpdateTitleBarRegions();
  if(calls.Count!=6)throw new Exception("Closed window writes regions");
  p._isClosed=false; p.AppTitleBar.XamlRoot.RasterizationScale=1;
  p.AppTitleBar.X=200; p.AppTitleBar.ActualWidth=1000; p.UpdateTitleBarRegions();
  cap=calls.Last(c=>c.kind==NonClientRegionKind.Caption).rects.Single();
  if(cap.X!=200 || cap.Width!=900)throw new Exception("Full-height sidebar overlaps the caption region");
  p.AppTitleBar.X=260; p.AppTitleBar.ActualWidth=940; p.UpdateTitleBarRegions();
  cap=calls.Last(c=>c.kind==NonClientRegionKind.Caption).rects.Single();
  if(cap.X!=260 || cap.Width!=840)throw new Exception("Sidebar resizing leaves stale caption geometry");
 }
__METHOD__
}
'@
Add-Type -TypeDefinition $fixture.Replace('__METHOD__',$method).Replace('__CHANGED__',$changed) -WarningAction SilentlyContinue
[Probe]::Run()
'PASS: initial caption, interactive tabs/plus, 100 unchanged updates, resize, DPI, drag restore and closed window.'
