$ErrorActionPreference = 'Stop'
# Needs PowerShell 7 (the extracted production methods use modern C#). Own title-bar frame: no extended title-bar mode,
# explicit Caption/Passthrough regions plus our own Minimize/Maximize/Close regions (Trellis 11-06-window-drag-stutter).
$root = Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
$main = Get-Content "$root\MainWindow.xaml.cs" -Raw
if ($main -match 'SetTitleBar\(') { throw 'MainWindow must not use SetTitleBar: regions are explicit geometry (and CustomWindowFrame answers WM_NCHITTEST).' }
if ($main -match 'ExtendsContentIntoTitleBar\s*=\s*true') { throw 'MainWindow is back in extended title-bar mode, which caps window dragging at ~120 moves/s.' }
$quickLook = Get-Content "$root\Views\QuickLookWindow.xaml.cs" -Raw
if ($quickLook -match 'SetTitleBar\(' -or $quickLook -match 'ExtendsContentIntoTitleBar\s*=\s*true') { throw 'Quick Look is back in extended title-bar mode.' }
if ($main -notmatch 'new Helpers\.CustomWindowFrame\(_hwnd\)' -or $quickLook -notmatch 'new Helpers\.CustomWindowFrame\(') { throw 'A window lost its own title-bar frame.' }
$source = Get-Content "$root\MainWindow.TabManager.cs" -Raw
$method = [regex]::Match($source, '(?sm)^        private void UpdateTitleBarRegions\(\).*?^        }').Value
if (!$method) { throw 'Production region method missing.' }
$buttons = [regex]::Match($source, '(?sm)^        private void UpdateCaptionButtonRegions\(double scale\).*?^        }').Value
if (!$buttons) { throw 'Production caption-button region method missing.' }
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
public class AppWin { public int Id=1; }
public class Change { public bool DidSizeChange,DidPresenterChange,DidVisibilityChange,DidPositionChange; }
public enum NonClientRegionKind { Caption,Passthrough,Minimize,Maximize,Close }
public enum Visibility { Visible, Collapsed }
public class CaptionButtonsStub {
 public double ActualWidth=138; public Visibility Visibility=Visibility.Visible; public double Right=1000;
 // Three 46-DIP buttons at the right end of the title bar.
 public IReadOnlyList<(NonClientRegionKind Kind, Windows.Graphics.RectInt32 Rect)> GetRegionRects(double s) => new List<(NonClientRegionKind, Windows.Graphics.RectInt32)> {
  (NonClientRegionKind.Minimize, new((int)Math.Round((Right-138)*s),0,(int)Math.Round(46*s),(int)Math.Round(32*s))),
  (NonClientRegionKind.Maximize, new((int)Math.Round((Right-92)*s),0,(int)Math.Round(46*s),(int)Math.Round(32*s))),
  (NonClientRegionKind.Close, new((int)Math.Round((Right-46)*s),0,(int)Math.Round(46*s),(int)Math.Round(32*s))) };
}
public class FrameStub { public Windows.Graphics.RectInt32 MaximizeButtonRect; }
public class InputNonClientPointerSource {
 public static InputNonClientPointerSource Instance=new();
 public List<(NonClientRegionKind kind,Windows.Graphics.RectInt32[] rects)> Writes=new();
 public static InputNonClientPointerSource GetForWindowId(int id)=>Instance;
 public void SetRegionRects(NonClientRegionKind k,Windows.Graphics.RectInt32[] r)=>Writes.Add((k,r));
}
public class Probe {
 bool _isClosed;
 FrameworkElement TabScrollViewer=new(){X=100,ActualWidth=600};
 FrameworkElement AppTitleBar=new(){ActualWidth=1000};
 FrameworkElement NewTabButton=new(){X=202,ActualWidth=34};
 CaptionButtonsStub AppCaptionButtons=new();
 FrameStub _windowFrame=new();
 Repeater TabRepeater=new(); AppWin AppWindow=new();
 Windows.Graphics.RectInt32[] _lastTitleBarRects; Windows.Graphics.RectInt32[] _lastCaptionButtonRects;
 Windows.Graphics.RectInt32? _lastTitleBarCaptionRect;
 int Queued; void QueueUpdateTitleBarRegions(){Queued++;}
 void Changed(Change args) { __CHANGED__ }
 static Windows.Foundation.Rect? GetClippedRect(FrameworkElement el,Windows.Foundation.Rect v){double l=Math.Max(el.X,v.X),r=Math.Min(el.X+el.ActualWidth,v.X+v.Width);return r>l?new(l,el.Y,r-l,el.ActualHeight):null;}
 static Windows.Graphics.RectInt32 ToRectInt32(Windows.Foundation.Rect r,double s)=>new((int)Math.Round(r.X*s),(int)Math.Round(r.Y*s),(int)Math.Round(r.Width*s),(int)Math.Round(r.Height*s));
 static bool IsTab(NonClientRegionKind k)=>k==NonClientRegionKind.Caption||k==NonClientRegionKind.Passthrough;
 public static void Run(){
  var p=new Probe(); p.TabRepeater.ItemsSourceView.Add(new(){X=100});
  var all=InputNonClientPointerSource.Instance.Writes;
  Func<List<(NonClientRegionKind kind,Windows.Graphics.RectInt32[] rects)>> tab=()=>all.Where(c=>IsTab(c.kind)).ToList();
  Func<List<(NonClientRegionKind kind,Windows.Graphics.RectInt32[] rects)>> caps=()=>all.Where(c=>!IsTab(c.kind)).ToList();
  p.Changed(new(){DidPositionChange=true});
  if(p.Queued!=0)throw new Exception("Position-only window movement schedules layout work");
  p.Changed(new(){DidSizeChange=true});p.Changed(new(){DidPresenterChange=true});p.Changed(new(){DidVisibilityChange=true});
  if(p.Queued!=3)throw new Exception("Size, presenter and visibility changes must schedule geometry updates");
  p.UpdateTitleBarRegions();
  if(tab().Count!=2 || !tab().Any(c=>c.kind==NonClientRegionKind.Caption))throw new Exception("Missing explicit caption and passthrough");
  var cap=tab().Single(c=>c.kind==NonClientRegionKind.Caption).rects.Single();
  if(cap.Width!=862 || cap.Height!=32)throw new Exception("Caption overlaps own caption buttons or misses title bar");
  var pass=tab().Single(c=>c.kind==NonClientRegionKind.Passthrough).rects;
  if(pass.Length!=2 || pass[0].X!=100 || pass[1].X!=202)throw new Exception("Tabs and plus must remain interactive");
  if(caps().Count!=3 || caps().Select(c=>c.kind).Distinct().Count()!=3)throw new Exception("Own minimize/maximize/close regions missing");
  if(caps().Single(c=>c.kind==NonClientRegionKind.Close).rects.Single().X!=954)throw new Exception("Close region misplaced");
  if(p._windowFrame.MaximizeButtonRect.X!=908 || p._windowFrame.MaximizeButtonRect.Width!=46)throw new Exception("Snap-layout maximize rect not handed to CustomWindowFrame");
  for(int i=0;i<100;i++)p.UpdateTitleBarRegions();
  if(tab().Count!=2 || caps().Count!=3)throw new Exception("Unchanged geometry repeats native writes");
  p.AppTitleBar.ActualWidth=1200;p.AppCaptionButtons.Right=1200;p.UpdateTitleBarRegions();
  if(tab().Count!=3 || tab().Last().rects[0].Width!=1062)throw new Exception("Resize must update only caption");
  if(caps().Count!=6)throw new Exception("Resize must move the caption-button regions");
  p.AppTitleBar.XamlRoot.RasterizationScale=2;p.UpdateTitleBarRegions();
  if(tab().Count!=5 || tab().Last(c=>c.kind==NonClientRegionKind.Caption).rects[0].Width!=2124)throw new Exception("DPI change lost physical caption geometry");
  if(caps().Count!=9 || p._windowFrame.MaximizeButtonRect.Width!=92)throw new Exception("DPI change lost physical caption-button geometry");
  p._lastTitleBarRects=null;p.UpdateTitleBarRegions();
  if(tab().Count!=6 || tab().Last().kind!=NonClientRegionKind.Passthrough)throw new Exception("Drag completion must restore passthrough");
  p.AppCaptionButtons.Visibility=Visibility.Collapsed;p.UpdateTitleBarRegions();
  if(caps().Count!=12 || caps().Skip(9).Any(c=>c.rects.Length!=0) || p._windowFrame.MaximizeButtonRect.Width!=0)throw new Exception("Hidden caption buttons (full screen) must clear their regions");
  p.AppCaptionButtons.Visibility=Visibility.Visible;
  int before=all.Count;
  p._isClosed=true;p.AppTitleBar.ActualWidth=1400;p.UpdateTitleBarRegions();
  if(all.Count!=before)throw new Exception("Closed window writes regions");
  p._isClosed=false; p.AppTitleBar.XamlRoot.RasterizationScale=1;
  p.AppTitleBar.X=200; p.AppTitleBar.ActualWidth=1000; p.UpdateTitleBarRegions();
  cap=tab().Last(c=>c.kind==NonClientRegionKind.Caption).rects.Single();
  if(cap.X!=200 || cap.Width!=862)throw new Exception("Full-height sidebar overlaps the caption region");
  p.AppTitleBar.X=260; p.AppTitleBar.ActualWidth=940; p.UpdateTitleBarRegions();
  cap=tab().Last(c=>c.kind==NonClientRegionKind.Caption).rects.Single();
  if(cap.X!=260 || cap.Width!=802)throw new Exception("Sidebar resizing leaves stale caption geometry");
 }
__METHOD__
__BUTTONS__
}
'@
$code = $fixture.Replace('__METHOD__',$method).Replace('__BUTTONS__',$buttons).Replace('__CHANGED__',$changed)
if ($env:ORIELFOLDER_FIXTURE_OUT) { Set-Content -LiteralPath $env:ORIELFOLDER_FIXTURE_OUT -Value $code -Encoding UTF8; 'Fixture written.'; return }
Add-Type -TypeDefinition $code -WarningAction SilentlyContinue
[Probe]::Run()
'PASS: no extended title bar, own frame, caption/passthrough/caption-button regions, 100 unchanged updates, resize, DPI, drag restore, full screen, closed window, sidebar.'
