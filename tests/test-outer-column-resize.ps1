$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = [IO.File]::ReadAllText((Join-Path $repo 'src\OrielFolder\OrielFolder\MainWindow.DragDropHandler.cs'))
$methods = foreach ($name in @('OnColumnResizeGripPointerPressed', 'OnColumnResizeGripPointerMoved', 'OnColumnResizeGripPointerReleased')) {
    $match = [regex]::Match($source, '(?sm)^        private void ' + $name + '\(.*?(?=^        (?:private |///|// ={5}|#))')
    if (-not $match.Success) { throw "Cannot locate production method $name." }
    $match.Value.TrimEnd()
}
$outerPath = Join-Path $repo 'src\OrielFolder\OrielFolder\MainWindow.OuterColumnResize.cs'
$outer = if (Test-Path $outerPath) { [IO.File]::ReadAllText($outerPath) } else { '' }
$geometryPath = Join-Path $repo 'src\OrielFolder\OrielFolder\Models\PaneLayoutGeometry.cs'
$geometry = if (Test-Path $geometryPath) { [IO.File]::ReadAllText($geometryPath) } else { '' }

# Execute production gesture code, replacing only WinUI and native Windows calls.
# A column-only resize, relative-coordinate drift, wrong HWND or unconstrained window
# resize fails an observable size/position assertion below.
$hostSource = @'
#nullable enable annotations
#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OrielFolder.Helpers;
namespace Windows.Foundation {
    public struct Point { public double X, Y; public Point(double x, double y) { X=x; Y=y; } }
}
namespace Windows.System { public enum VirtualKey { Control } }
namespace Windows.UI.Core { [Flags] public enum CoreVirtualKeyStates { None=0, Down=1 } }
namespace Microsoft.UI.Input {
    public static class InputKeyboardSource {
        public static bool ControlDown;
        public static Windows.UI.Core.CoreVirtualKeyStates GetKeyStateForCurrentThread(Windows.System.VirtualKey key)
            => ControlDown ? Windows.UI.Core.CoreVirtualKeyStates.Down : Windows.UI.Core.CoreVirtualKeyStates.None;
    }
}
namespace Microsoft.UI.Xaml {
    public class DependencyObject { public DependencyObject? Parent; }
    public class UIElement : DependencyObject {
        public event EventHandler<Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? PointerCaptureLost;
        public bool CaptureSucceeds=true;
        public bool CapturePointer(object pointer) => CaptureSucceeds;
        public void ReleasePointerCapture(object pointer) { PointerCaptureLost?.Invoke(this, new()); }
        public void LoseCapture() { PointerCaptureLost?.Invoke(this, new()); }
    }
    public class FrameworkElement : UIElement {
        public double Width, ActualWidth, RootX;
        public Action? LayoutAction;
        public void InvalidateMeasure() {}
        public void UpdateLayout() { LayoutAction?.Invoke(); }
        public GeneralTransform TransformToVisual(UIElement root) => new() { X=RootX };
    }
    public class GeneralTransform {
        public double X;
        public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point p) => new(p.X+X,p.Y);
    }
}
namespace Microsoft.UI.Xaml.Controls {
    public class Grid : FrameworkElement {}
    public class ContentPresenter : FrameworkElement { public Grid Child = new(); }
    public class ItemsControl : FrameworkElement {
        public ContentPresenter[] Containers = { new(), new(), new() };
        public object ContainerFromIndex(int i) => Containers[i];
    }
    public class ScrollViewer : FrameworkElement {
        public double ViewportWidth=900, ExtentWidth=1500, HorizontalOffset;
        public int ViewChanges;
        public Action? OffsetChanged;
        public bool ChangeView(double? horizontal,double? vertical,float? zoom,bool disableAnimation) {
            if (horizontal.HasValue) HorizontalOffset=Math.Clamp(horizontal.Value,0,Math.Max(0,ExtentWidth-ViewportWidth));
            ViewChanges++; OffsetChanged?.Invoke(); return true;
        }
    }
}
namespace Microsoft.UI.Xaml.Shapes {
    public class Rectangle : FrameworkElement { public object? Fill; }
}
namespace Microsoft.UI.Xaml.Media {
    public static class VisualTreeHelper { public static DependencyObject? GetParent(DependencyObject element) => element.Parent; }
}
namespace Microsoft.UI.Xaml.Input {
    public class PointerRoutedEventArgs : EventArgs {
        public double X;
        public object Pointer = new();
        public bool Handled;
        public PointerPoint GetCurrentPoint(UIElement? root) => new() { Position=new(X,0) };
    }
    public class PointerPoint { public Windows.Foundation.Point Position; }
}
namespace OrielFolder.Helpers {
    public static class VisualTreeHelpers { public static T? FindChild<T>(ContentPresenter p) where T:class => p.Child as T; }
    public static class NativeMethods {
        public struct RECT { public int Left,Top,Right,Bottom; }
        public struct POINT { public int X,Y; }
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor,rcWork; public uint dwFlags; }
        public static RECT Rect=new() { Left=100,Top=50,Right=1000,Bottom=650 };
        public static RECT Work=new() { Left=0,Top=0,Right=1920,Bottom=1080 };
        public static POINT Cursor=new() { X=1000,Y=300 };
        public static uint Dpi=96;
        public static bool Zoomed,Iconic,WindowPosSucceeds=true;
        public static int Calls;
        public static IntPtr LastHandle;
        public static uint LastFlags;
        public const uint MONITOR_DEFAULTTONEAREST=2, SWP_NOZORDER=4, SWP_NOACTIVATE=16, SWP_NOOWNERZORDER=512;
        public static readonly IntPtr HWND_TOP=IntPtr.Zero;
        public static bool GetWindowRect(IntPtr h, out RECT rect) { rect=Rect; return true; }
        public static bool GetCursorPos(out POINT p) { p=Cursor; return true; }
        public static uint GetDpiForWindow(IntPtr h) => Dpi;
        public static IntPtr MonitorFromRect(ref RECT r,uint flags) => new(1);
        public static bool GetMonitorInfo(IntPtr h,ref MONITORINFO info) { info.rcWork=Work; return true; }
        public static bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags) {
            Calls++; LastHandle=h; LastFlags=flags;
            if (!WindowPosSucceeds) return false;
            Rect=new() {Left=x,Top=y,Right=x+width,Bottom=y+height}; return true;
        }
        public static void Reset() {
            Rect=new() {Left=100,Top=50,Right=1000,Bottom=650};
            Work=new() {Left=0,Top=0,Right=1920,Bottom=1080};
            Cursor=new() {X=1000,Y=300}; Dpi=96; Calls=0; Zoomed=Iconic=false; WindowPosSucceeds=true;
        }
    }
}
namespace OrielFolder {
    public class Explorer { public List<object> Columns = new() { new(),new(),new() }; }
    public class Vm { public Explorer ActiveExplorer = new(); }
    public sealed partial class MainWindow {
        private bool _isResizingColumn;
        private Grid? _resizingColumnGrid;
        private double _resizeStartX,_resizeStartWidth;
        private readonly IntPtr _hwnd=new(42);
        public Grid RootGrid=new() { ActualWidth=900 };
        public Vm ViewModel=new();
        public ItemsControl Control=new();
        public ScrollViewer Scroller=new();
        public double ViewportGrowthShare=1;
        public bool SimulateOffsetClamp;
        private readonly object _transparentBrush=new();
        private static bool IsZoomed(IntPtr h) => NativeMethods.Zoomed;
        private static bool IsIconic(IntPtr h) => NativeMethods.Iconic;
        private ItemsControl GetActiveMillerColumnsControl() => Control;
        private ScrollViewer GetActiveMillerScrollViewer() => Scroller;
        private static void SetGripCursor(object rect,bool resize) {}
        public void Press(Microsoft.UI.Xaml.Shapes.Rectangle grip) {
            var column=(Grid)grip.Parent!;
            column.Parent ??= Scroller;
            double rootWidth=RootGrid.ActualWidth,viewportWidth=Scroller.ViewportWidth,columnLeft=column.RootX;
            double startColumnWidth=column.Width,startOffset=Scroller.HorizontalOffset,startExtent=Scroller.ExtentWidth;
            double gripLeft=grip.RootX;
            double priorWidths=0;
            foreach (var container in Control.Containers) priorWidths+=container.Child.Width;
            int physicalWidth=NativeMethods.Rect.Right-NativeMethods.Rect.Left;
            double dpi=NativeMethods.Dpi/96.0;
            RootGrid.LayoutAction=() => {
                double logicalDelta=(NativeMethods.Rect.Right-NativeMethods.Rect.Left-physicalWidth)/dpi;
                RootGrid.ActualWidth=rootWidth+logicalDelta;
                Scroller.ViewportWidth=viewportWidth+logicalDelta*ViewportGrowthShare;
                double priorDelta=0;
                foreach (var container in Control.Containers) priorDelta+=container.Child.Width;
                priorDelta-=priorWidths;
                Scroller.ExtentWidth=startExtent+column.Width-startColumnWidth+priorDelta;
                if (SimulateOffsetClamp)
                    Scroller.HorizontalOffset=Math.Clamp(Scroller.HorizontalOffset,0,Math.Max(0,Scroller.ExtentWidth-Scroller.ViewportWidth));
                column.RootX=columnLeft+logicalDelta*(1-ViewportGrowthShare)+priorDelta-(Scroller.HorizontalOffset-startOffset);
                grip.RootX=gripLeft+logicalDelta*(1-ViewportGrowthShare)+column.Width-startColumnWidth+priorDelta-(Scroller.HorizontalOffset-startOffset);
            };
            Scroller.OffsetChanged=() => RootGrid.UpdateLayout();
            OnColumnResizeGripPointerPressed(grip,new() {X=900});
        }
        public void Move(Microsoft.UI.Xaml.Shapes.Rectangle grip,int physicalX,double localX) {
            NativeMethods.Cursor.X=physicalX;
            OnColumnResizeGripPointerMoved(grip,new() {X=localX});
        }
        public void Release(Microsoft.UI.Xaml.Shapes.Rectangle grip) => OnColumnResizeGripPointerReleased(grip,new());
        public static void Require(bool value,string message) { if (!value) throw new Exception(message); }
        public static void Run() {
            NativeMethods.Reset();
            var window=new MainWindow();
            var column=new Grid {Width=300,ActualWidth=300};
            var grip=new Microsoft.UI.Xaml.Shapes.Rectangle {Parent=column,RootX=896,ActualWidth=4};
            window.Press(grip);
            window.Move(grip,1100,1000);
            Require(NativeMethods.Rect.Right==1100,"Rightmost grip must grow the actual window, not hide the extra column width offscreen.");
            Require(column.Width==400,"Outer column must track its visible window growth.");
            Require(NativeMethods.LastHandle==new IntPtr(42),"Only the gesture's own HWND may be resized.");
            Require((NativeMethods.LastFlags & 0x216)==0x216,"Native resizing must preserve position, activation and Z order.");
            Require(NativeMethods.Rect.Left==100 && NativeMethods.Rect.Top==50 && NativeMethods.Rect.Bottom==650,"Outer resize must preserve position and height.");
            window.Move(grip,1150,1050);
            Require(NativeMethods.Rect.Right==1150 && column.Width==450,"Moves must use original window and column dimensions, not accumulate drift.");
            window.Move(grip,1150,700);
            Require(NativeMethods.Rect.Right==1150 && column.Width==450,"Local-coordinate changes while resizing must not alter physical drag delta.");
            window.Move(grip,1800,1700);
            Require(NativeMethods.Rect.Right==1800 && column.Width==600,"Window follows raw pointer delta while column width independently stops at its maximum.");
            window.Release(grip);
            int calls=NativeMethods.Calls;
            window.Move(grip,1900,1800);
            Require(NativeMethods.Calls==calls,"Release must end native resizing.");
            Console.WriteLine("PASS: real outer gesture grows window, keeps original coordinates, preserves HWND/height, clamps column, and releases");

            NativeMethods.Reset(); window=new(); column=new() {Width=300};
            grip=new() {Parent=column,RootX=400,ActualWidth=4};
            Microsoft.UI.Input.InputKeyboardSource.ControlDown=true;
            window.Press(grip); window.Move(grip,1100,1000);
            Require(NativeMethods.Calls==0 && column.Width==400,"Interior pane grips must keep ordinary column resizing without resizing the window.");
            Require(window.Control.Containers[0].Child.Width==400,"Ctrl+drag must still apply width to all columns.");
            Microsoft.UI.Input.InputKeyboardSource.ControlDown=false;
            window.Release(grip);
            Console.WriteLine("PASS: interior grips preserve ordinary resizing and Ctrl+drag");

            NativeMethods.Reset(); NativeMethods.Work.Right=1050;
            window=new(); column=new() {Width=300}; grip=new() {Parent=column,RootX=888,ActualWidth=4};
            window.Press(grip); window.Move(grip,1200,1100);
            Require(NativeMethods.Rect.Right==1050 && column.Width==350,"Work-area cap must also cap the visible column delta.");
            grip.LoseCapture(); calls=NativeMethods.Calls; window.Move(grip,1300,1200);
            Require(NativeMethods.Calls==calls,"Lost pointer capture must cancel resizing.");
            Console.WriteLine("PASS: work-area edge caps column growth and lost capture cancels gesture");

            NativeMethods.Reset(); NativeMethods.Dpi=144;
            window=new(); column=new() {Width=300}; grip=new() {Parent=column,RootX=896,ActualWidth=4};
            window.Press(grip); window.Move(grip,1150,1000);
            Require(NativeMethods.Rect.Right==1150 && column.Width==400,"150% DPI must convert physical pointer movement to 100 logical column pixels once.");
            window.Release(grip);
            Console.WriteLine("PASS: physical window and logical column units stay correct at 150% DPI");

            NativeMethods.Reset(); NativeMethods.Rect=new() {Left=100,Top=50,Right=650,Bottom=650};
            window=new(); column=new() {Width=300}; grip=new() {Parent=column,RootX=896,ActualWidth=4};
            window.Press(grip); window.Move(grip,800,700);
            Require(NativeMethods.Rect.Right==580 && column.Width==230,"Minimum window width must limit column shrink to its actual visible delta.");
            window.Release(grip);
            Console.WriteLine("PASS: minimum window width limits inward dragging");

            foreach (bool zoomed in new[] {true,false}) {
                NativeMethods.Reset(); NativeMethods.Zoomed=zoomed; NativeMethods.Iconic=!zoomed;
                window=new(); column=new() {Width=300}; grip=new() {Parent=column,RootX=896,ActualWidth=4};
                window.Press(grip); window.Move(grip,1100,1000);
                Require(NativeMethods.Calls==0,"Maximized or minimized windows must not enter outer-window resizing.");
                window.Release(grip);
            }
            Console.WriteLine("PASS: maximized and minimized windows do not resize natively");

            NativeMethods.Reset(); NativeMethods.WindowPosSucceeds=false;
            window=new(); column=new() {Width=300}; grip=new() {Parent=column,RootX=896,ActualWidth=4};
            window.Press(grip); window.Move(grip,1100,1000);
            Require(NativeMethods.Rect.Right==1000 && column.Width==300,"A failed native resize must not grow the column offscreen.");
            window.Release(grip);
            Console.WriteLine("PASS: native resize failure preserves visible column width");

            NativeMethods.Reset(); window=new(); column=new() {Width=300};
            grip=new() {Parent=column,RootX=896,ActualWidth=4,CaptureSucceeds=false};
            window.Press(grip); window.Move(grip,1100,1000);
            Require(NativeMethods.Calls==0 && column.Width==300,"Failed pointer capture must cancel the whole gesture.");
            Console.WriteLine("PASS: failed pointer capture does not leave a live resize gesture");

            NativeMethods.Reset(); NativeMethods.Rect=new() {Left=-1800,Top=50,Right=-900,Bottom=650};
            NativeMethods.Work=new() {Left=-1920,Top=0,Right=0,Bottom=1080};
            NativeMethods.Cursor.X=-900;
            window=new(); column=new() {Width=300}; grip=new() {Parent=column,RootX=896,ActualWidth=4};
            window.Press(grip); window.Move(grip,-800,1000);
            Require(NativeMethods.Rect.Left==-1800 && NativeMethods.Rect.Right==-800 && column.Width==400,"Monitors left of the primary display must use physical signed coordinates.");
            window.Release(grip);
            Console.WriteLine("PASS: negative monitor coordinates preserve physical positioning");

            foreach (var share in new[] {0.5,0.25}) {
                NativeMethods.Reset(); window=new() {ViewportGrowthShare=share};
                window.Scroller.ViewportWidth=450;
                column=new() {Width=300,RootX=600};
                grip=new() {Parent=column,RootX=896,ActualWidth=4};
                window.Press(grip); window.Move(grip,1100,1000);
                Require(NativeMethods.Rect.Right==1100,"Split layouts must keep the native window edge at the physical pointer delta.");
                Require(column.Width==(share==0.5 ? 350 : 325),"Split column must grow only by its own viewport's allocated window width.");
                Require(column.RootX+column.Width==1000,"Proportional layout must keep the outer column end at the app boundary without clipping.");
                window.Move(grip,1150,1000);
                Require(column.Width==(share==0.5 ? 375 : 337.5),"Repeated split drags must remain based on the original viewport width.");
                window.Move(grip,950,800);
                Require(column.Width==(share==0.5 ? 275 : 287.5) && column.RootX+column.Width==850,"Inward split dragging must use the viewport shrink and keep the grip visible.");
                window.Release(grip);
            }
            Console.WriteLine("PASS: proportional half and quarter panes resize only their actual viewport allocation without clipping or drift");

            foreach (var initialWidth in new[] {120.0,150.0}) {
                NativeMethods.Reset(); window=new(); column=new() {Width=initialWidth};
                grip=new() {Parent=column,RootX=896,ActualWidth=4};
                window.Press(grip); window.Move(grip,980,880);
                Require(NativeMethods.Rect.Right==980,"A minimum-width or auto-fit column must not block inward window dragging or make the window grow.");
                Require(column.Width==150,"Column width must independently retain its existing 150 minimum.");
                window.Move(grip,940,840);
                Require(NativeMethods.Rect.Right==940,"Further inward dragging must keep following the original screen pointer delta.");
                window.Release(grip);
            }
            Console.WriteLine("PASS: auto-fit 120 and minimum 150 columns can shrink the application without reversing window direction");

            foreach (bool ctrl in new[] {false,true}) {
                NativeMethods.Reset(); window=new() {SimulateOffsetClamp=true};
                window.Scroller.HorizontalOffset=600;
                column=new() {Width=300,RootX=600};
                foreach (var container in window.Control.Containers) container.Child.Width=300;
                grip=new() {Parent=column,RootX=888,ActualWidth=4};
                Microsoft.UI.Input.InputKeyboardSource.ControlDown=ctrl;
                window.Press(grip); window.Move(grip,1100,1000);
                Require(window.Scroller.ViewChanges>0,"Viewport expansion that clamps the old offset must be corrected after changing column widths.");
                Require(Math.Abs(grip.RootX+grip.ActualWidth-(window.RootGrid.ActualWidth-8))<0.01,"The captured outer grip must retain its original inset from the resized window's right edge.");
                window.Move(grip,950,850);
                Require(Math.Abs(grip.RootX+grip.ActualWidth-(window.RootGrid.ActualWidth-8))<0.01,"Inward dragging must retain grip alignment after offset clamping, including Ctrl changes to preceding columns.");
                window.Release(grip);
                Microsoft.UI.Input.InputKeyboardSource.ControlDown=false;
            }
            Console.WriteLine("PASS: layout offset clamping is corrected after all column changes, preserving grip inset with and without Ctrl");

            NativeMethods.Reset(); window=new(); column=new() {Width=300};
            grip=new() {Parent=column,RootX=896,ActualWidth=4};
            window.Press(grip);
            var updateLayout=window.RootGrid.LayoutAction;
            window.RootGrid.LayoutAction=() => { updateLayout!(); grip.LoseCapture(); };
            window.Move(grip,1100,1000);
            Require(column.Width==300,"Capture lost during layout must not apply a stale column resize.");
            calls=NativeMethods.Calls; window.Move(grip,1200,1100);
            Require(NativeMethods.Calls==calls,"Capture lost during layout must end native resizing.");
            Console.WriteLine("PASS: capture cancellation during synchronous layout stops the pending column update");

            NativeMethods.Reset(); window=new() {SimulateOffsetClamp=true};
            window.Scroller.HorizontalOffset=600;
            column=new() {Width=300}; grip=new() {Parent=column,RootX=896,ActualWidth=4};
            window.Press(grip); updateLayout=window.RootGrid.LayoutAction;
            int layouts=0;
            window.RootGrid.LayoutAction=() => { updateLayout!(); if (++layouts==2) grip.LoseCapture(); };
            window.Move(grip,1100,1000);
            Require(window.Scroller.ViewChanges==0,"Capture lost during the final column layout must not apply stale scroll correction.");
            calls=NativeMethods.Calls; window.Move(grip,1200,1100);
            Require(NativeMethods.Calls==calls,"Final-layout capture cancellation must end the native gesture.");
            Console.WriteLine("PASS: capture cancellation during final layout prevents stale grip alignment");
        }
    }
}
'@
$insertAt = $hostSource.IndexOf('        public void Press(')
$hostSource = $hostSource.Insert($insertAt, ($methods -join "`n") + "`n")
$outer = [regex]::Replace($outer, '(?m)^using [^;]+;\r?\n', '')
$geometry = [regex]::Replace($geometry, '(?m)^using [^;]+;\r?\n', '')
if ($geometry -match 'namespace OrielFolder.Models;') {
    $geometry = $geometry.Replace('namespace OrielFolder.Models;', 'namespace OrielFolder.Models {') + "`n}"
}
Add-Type -TypeDefinition ($hostSource + "`n" + $outer + "`n" + $geometry)
[OrielFolder.MainWindow]::Run()
