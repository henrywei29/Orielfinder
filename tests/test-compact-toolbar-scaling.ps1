$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\MainWindow.SettingsHandler.cs'))
$method=[regex]::Match($source,'(?sm)^        private void ApplyIconFontScaleToGlobalUI\(.*?^        }').Value
if (-not $method) { throw 'Toolbar scaling method not found' }
$driver=@'
using System;
using System.Collections.Generic;
public class UIElement { }
public class FrameworkElement:UIElement { public int Row; }
public class Grid:FrameworkElement {public List<UIElement> Children=new(); public static int GetRow(FrameworkElement e)=>e.Row;}
public class FontIcon:UIElement {public double FontSize;}
public class TextBlock:UIElement {public double FontSize;}
public class Vm {public List<object> Tabs=new();}
public class Repeater {public object TryGetElement(int i)=>null;}
namespace Helpers {public static class VisualTreeHelpers {public static T FindChild<T>(object e) where T:class=>null;}}
public class ToolbarScaleProbe {
 bool _isClosed=false;
 UIElement AppTitleBar=null,NewTabButton=new();
 Vm ViewModel=new(); Repeater TabRepeater=new();
 FrameworkElement MainCommandBar=new(); object Content;
 List<UIElement> targets=new();
 void ApplyScaleToTabElement(UIElement e,int level){}
 void ApplyAbsoluteScaleToTree(UIElement e,int level,int a,int b,int c,int d)=>targets.Add(e);
 public static string Run(){
  var p=new ToolbarScaleProbe(); var files=new Grid{Row=1};var status=new Grid{Row=3};var root=new Grid();root.Children.Add(files);root.Children.Add(status);p.Content=root;
  p.ApplyIconFontScaleToGlobalUI(2);
  if(p.targets.Count!=2 || !p.targets.Contains(p.MainCommandBar) || !p.targets.Contains(status) || p.targets.Contains(files)) throw new Exception("Toolbar font scaling reached file content and overrides file font bindings");
  return "PASS: actual toolbar scaling targets toolbar/status only and preserves file-content bindings";
 }
'@
Add-Type -TypeDefinition ($driver+$method+"`n}")
[ToolbarScaleProbe]::Run()
