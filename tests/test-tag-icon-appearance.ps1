$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\ViewModels'
$folder=[IO.File]::ReadAllText((Join-Path $root 'FolderViewModel.cs'))
$item=[IO.File]::ReadAllText((Join-Path $root 'FileSystemViewModel.cs'))
$brush=[regex]::Match($folder,'(?s)public override Microsoft.UI.Xaml.Media.Brush IconBrush\s*=>\s*([^;]+);').Groups[1].Value
$dot=[regex]::Match($item,'public bool ShowTagDot\s*=>\s*([^;]+);').Groups[1].Value
if(!$brush -or !$dot){throw 'Icon properties missing'}
$driver=@'
using System;
public class Brush{}
namespace Services {public class IconService {public static IconService Current=new();public Brush FolderBrush=new();}}
public class Probe {
 public bool HasTag=true,HasCustomIcon=false;public Brush TagBrush=new();
 public Brush IconBrush=>__BRUSH__;
 public bool ShowTagDot=>__DOT__;
 public static void Run(){var p=new Probe();if(!ReferenceEquals(p.IconBrush,Services.IconService.Current.FolderBrush))throw new Exception("Tag recolors the folder icon");
 p.HasCustomIcon=true;if(p.ShowTagDot)throw new Exception("Tag overlaps the custom icon");}
}
'@
Add-Type -TypeDefinition $driver.Replace('__BRUSH__',$brush).Replace('__DOT__',$dot)
[Probe]::Run()
'PASS: actual icon properties preserve the original folder color and do not overlay custom icons.'
