$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\Helpers\InlineTreeViewHelper.cs'))
$source=[regex]::Replace($source,'(?m)^using .*;\r?\n','') -replace 'namespace Span.Helpers;', 'namespace Span.Helpers {'
$stubs=@'
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
namespace Microsoft.UI {public static class Colors {public static object Transparent=>null;}}
namespace Windows.UI.ViewManagement {
 public class AccessibilitySettings {
  public static int Created,Reads;public static bool Contrast;
  public AccessibilitySettings(){Created++;} public bool HighContrast {get {Reads++;return Contrast;}}
 }
}
namespace Microsoft.UI.Xaml {
 public enum ElementTheme {Light,Dark} public enum TextWrapping {Wrap}
 public class DependencyObject {public List<DependencyObject> VisualChildren=new();}
 public class FrameworkElement:DependencyObject {public XamlRoot XamlRoot;public ElementTheme ActualTheme;public ResourceDictionary Resources=new();}
 public class XamlRoot {public object Content;}
 public class ResourceDictionary:Dictionary<object,object> {public Dictionary<object,object> ThemeDictionaries=new();}
 public class Application {public static Application Current=new();public ResourceDictionary Resources=new();}
}
namespace Microsoft.UI.Xaml.Media {
 public class Brush{}
 public class SolidColorBrush:Brush {public SolidColorBrush(object color){}}
 public static class VisualTreeHelper {public static int GetChildrenCount(DependencyObject x)=>x.VisualChildren.Count;public static DependencyObject GetChild(DependencyObject x,int i)=>x.VisualChildren[i];}
}
namespace Microsoft.UI.Xaml.Controls.Primitives {
 public class SelectorItem:FrameworkElement {Brush _background;public int Writes;public Brush Background {get=>_background;set{Writes++;_background=value;}}}
}
namespace Microsoft.UI.Xaml.Controls {
 public class Panel:FrameworkElement {public List<DependencyObject> Children=>VisualChildren;}
 public class ListViewBase:FrameworkElement {public Panel ItemsPanelRoot;public int IndexFromContainer(SelectorItem c)=>ItemsPanelRoot.Children.IndexOf(c);}
 public class TextBlock {public string Text;public TextWrapping TextWrapping;public double MaxWidth;}
 public class Flyout {public object Content;public void ShowAt(FrameworkElement a){}}
}
public class StripeScrollProbe {
 public static string Run(){
  var root=new FrameworkElement{ActualTheme=ElementTheme.Light};var xaml=new XamlRoot{Content=root};root.XamlRoot=xaml;
  var light=new SolidColorBrush(null);var dark=new SolidColorBrush(null);
  root.Resources.ThemeDictionaries["Light"]=new ResourceDictionary{{"SpanBgLayer2Brush",light}};
  root.Resources.ThemeDictionaries["Dark"]=new ResourceDictionary{{"SpanBgLayer2Brush",dark}};
  var row=new SelectorItem();var list=new ListViewBase{XamlRoot=xaml};
  for(int i=0;i<10000;i++)Span.Helpers.InlineTreeViewHelper.ApplyStripe(list,row,1);
  if(Windows.UI.ViewManagement.AccessibilitySettings.Reads>1)throw new Exception("10000 row callbacks performed "+Windows.UI.ViewManagement.AccessibilitySettings.Reads+" system contrast reads");
  if(row.Writes!=1)throw new Exception("Unchanged stripe invalidated row "+row.Writes+" times");
  if(!ReferenceEquals(row.Background,light))throw new Exception("Light stripe missing");
  var panel=new Panel();panel.Children.Add(new SelectorItem());panel.Children.Add(row);list.ItemsPanelRoot=panel;
  root.ActualTheme=ElementTheme.Dark;Span.Helpers.InlineTreeViewHelper.RefreshStripes(list);
  if(!ReferenceEquals(row.Background,dark))throw new Exception("Cached palette was not refreshed for dark theme");
  Windows.UI.ViewManagement.AccessibilitySettings.Contrast=true;root.VisualChildren.Add(list);
  Span.Helpers.InlineTreeViewHelper.RefreshStripeTree(root);
  if(ReferenceEquals(row.Background,dark))throw new Exception("Contrast refresh retained colored stripes");
  return "PASS: 10000 row callbacks use one system lookup/one unchanged background write; theme/contrast refresh preserved";
 }
}
'@
Add-Type -TypeDefinition ($stubs+$source+"`n}")
[StripeScrollProbe]::Run()
