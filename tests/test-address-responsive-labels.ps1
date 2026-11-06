$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Controls\AddressBarControl.xaml.cs'))
$method=[regex]::Match($source,'(?sm)^        private void UpdateBreadcrumbLabels\(.*?^        }').Value
$watch=[regex]::Match($source,'(?sm)^        private void WatchPathSegments\(.*?^        }').Value
$changed=[regex]::Match($source,'(?sm)^        private void OnSegmentsCollectionChanged\(.*?^        }').Value
if(-not $method){throw 'Adaptive breadcrumb layout is missing; current binding always truncates names.'}
$formatter=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Helpers\CompactNavigationLabel.cs'))
$formatter=[regex]::Replace($formatter,'(?m)^using .*;\r?\n','') -replace 'namespace OrielFolder.Helpers;', 'namespace OrielFolder.Helpers {'
$driver=@'
#nullable enable annotations
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
namespace Windows.Foundation {public record struct Size(double Width,double Height);}
public class TextBlock {public string Text;public double FontSize;public object FontFamily;public Windows.Foundation.Size DesiredSize;public void Measure(Windows.Foundation.Size size)=>DesiredSize=new(Text.Length*FontSize/2,16);}
public class Button {public object Content;}
public class StackPanel {public List<object> Children=new();}
public class Repeater {public List<StackPanel> Rows=new();public object TryGetElement(int i)=>Rows[i];}
public class Scroller {public double ViewportWidth;}
public class Suggest {public object FontFamily=new();}
namespace Helpers {public class FontScaleService {public static FontScaleService Instance=new();public double AddressBarFontSize=12,AddressBarIconFontSize=14;}}
namespace OrielFolder.Models {public class PathSegment {public string Name;public bool IsLast;public string IconGlyph;public string CompactName=>OrielFolder.Helpers.CompactNavigationLabel.Format(Name);}}
public class AddressFitProbe {
 public IEnumerable PathSegments;
 public bool IsLoaded=true;INotifyCollectionChanged? _segmentChanges;int queued;
 void QueueBreadcrumbLayout()=>queued++;
 double _fullBreadcrumbWidth=double.NaN,_measuredFontSize,_measuredIconSize;
 Scroller BreadcrumbScroller=new();Suggest AutoSuggest=new();Repeater BreadcrumbRepeater=new();
 public static string Run(){
  var segment=new OrielFolder.Models.PathSegment{Name="Adobe Acrobat Pro DC 2019",IsLast=true};
  var p=new AddressFitProbe{PathSegments=new[]{segment}};var button=new Button{Content=segment.Name};var row=new StackPanel();row.Children.Add(button);p.BreadcrumbRepeater.Rows.Add(row);
  p.BreadcrumbScroller.ViewportWidth=800;p.UpdateBreadcrumbLabels();
  if(!Equals(button.Content,segment.Name))throw new Exception("Wide address bar must show full name");
  p.BreadcrumbScroller.ViewportWidth=100;p.UpdateBreadcrumbLabels();
  if(!Equals(button.Content,"Adobe Acro…"))throw new Exception("Overflow must follow the existing Latin10/CJK5 rules");
  p.UpdateBreadcrumbLabels();if(!Equals(button.Content,"Adobe Acro…"))throw new Exception("Compacted width causes truncation oscillation");
  p.BreadcrumbScroller.ViewportWidth=800;p.UpdateBreadcrumbLabels();
  if(!Equals(button.Content,segment.Name))throw new Exception("Growing the address bar must restore full names");
  if(segment.Name!="Adobe Acrobat Pro DC 2019")throw new Exception("Underlying navigation name was changed");
  var collection=new ObservableCollection<OrielFolder.Models.PathSegment>{segment};p.PathSegments=collection;p.WatchPathSegments();
  p.IsLoaded=false;p.WatchPathSegments();
  collection.Clear();var longName=new OrielFolder.Models.PathSegment{Name=new string('W',200),IsLast=true};collection.Add(longName);
  p.IsLoaded=true;p.WatchPathSegments();p.UpdateBreadcrumbLabels();
  if(!Equals(button.Content,new string('W',10)+"…"))throw new Exception("Reload reused width from before hidden path changes");
  p.WatchPathSegments();p.queued=0;collection[0]=segment;
  if(p.queued!=1)throw new Exception("Repeated loading duplicated collection observers");
  p.UpdateBreadcrumbLabels();if(!Equals(button.Content,segment.Name))throw new Exception("Collection update did not restore full label");
  p.BreadcrumbScroller.ViewportWidth=210;p.UpdateBreadcrumbLabels();
  Helpers.FontScaleService.Instance.AddressBarFontSize=24;p.UpdateBreadcrumbLabels();
  if(!Equals(button.Content,"Adobe Acro…"))throw new Exception("Font scaling did not invalidate measured width");
  Helpers.FontScaleService.Instance.AddressBarFontSize=12;p.UpdateBreadcrumbLabels();
  if(!Equals(button.Content,segment.Name))throw new Exception("Reducing font size did not restore full name");
  var other=new AddressFitProbe{PathSegments=collection};var otherButton=new Button{Content=segment.Name};
  var otherRow=new StackPanel();otherRow.Children.Add(otherButton);other.BreadcrumbRepeater.Rows.Add(otherRow);
  other.BreadcrumbScroller.ViewportWidth=100;other.UpdateBreadcrumbLabels();
  if(!Equals(otherButton.Content,"Adobe Acro…") || !Equals(button.Content,segment.Name))throw new Exception("Narrow pane changed the wide pane label");
  return "PASS: full by default, compact only when overflowing, no oscillation, full restored after resize";
 }
'@
Add-Type -TypeDefinition ($driver+$method+$watch+$changed+'}'+$formatter+'}')
[AddressFitProbe]::Run()
