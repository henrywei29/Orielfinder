$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\App.xaml.cs'))
$apply = [regex]::Match($source, '(?sm)^        private void ApplySystemAccentColor\(\).*?^        }').Value
$color = [regex]::Match($source, '(?sm)^        private static Windows.UI.Color ColorFromHex\(.*?^        }').Value
if (-not $apply -or -not $color) { throw 'Accent implementation not found.' }
$header = @'
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
namespace Windows.UI {
    public struct Color { public byte A,R,G,B; public static Color FromArgb(byte a,byte r,byte g,byte b)=>new(){A=a,R=r,G=g,B=b}; }
}
namespace Windows.UI.ViewManagement {
    public enum UIColorType { Accent }
    public class UISettings { public Windows.UI.Color GetColorValue(UIColorType type)=>Windows.UI.Color.FromArgb(255,20,120,220); }
}
namespace Microsoft.UI { public static class Colors { public static Windows.UI.Color Transparent=>default; } }
namespace Microsoft.UI.Xaml {
    public class ResourceDictionary:Dictionary<string,object> { public Dictionary<object,object> ThemeDictionaries=new(); }
}
namespace Microsoft.UI.Xaml.Media {
    public class SolidColorBrush { public Windows.UI.Color Color; public double Opacity=1; public SolidColorBrush(Windows.UI.Color value){Color=value;} }
}
public class AccentProbe {
    public Microsoft.UI.Xaml.ResourceDictionary Resources=new();
    public void Apply()=>ApplySystemAccentColor();
'@
$driver = @'
    public static string Run() {
        var app=new AccentProbe();
        foreach(var name in new[]{"Light","Dark","HighContrast","HighContrastBlack"}) {
            var dictionary=new Microsoft.UI.Xaml.ResourceDictionary();
            dictionary["SpanAccentBrush"]=new object();
            dictionary["SpanBgSelectedBrush"]=new object();
            app.Resources.ThemeDictionaries[name]=dictionary;
        }
        var before=new Dictionary<string,object>();
        foreach(var entry in app.Resources.ThemeDictionaries)
            before[(string)entry.Key]=((Microsoft.UI.Xaml.ResourceDictionary)entry.Value)["SpanAccentBrush"];
        app.Apply();
        foreach(var name in new[]{"Light","Dark"}) {
            var dictionary=(Microsoft.UI.Xaml.ResourceDictionary)app.Resources.ThemeDictionaries[name];
            if(ReferenceEquals(before[name],dictionary["SpanAccentBrush"])) throw new Exception(name+" lost system accent updates.");
            if(!dictionary.ContainsKey("ListViewItemBackgroundSelected")) throw new Exception(name+" lacks scoped selection accent.");
        }
        foreach(var name in new[]{"HighContrast","HighContrastBlack"}) {
            var dictionary=(Microsoft.UI.Xaml.ResourceDictionary)app.Resources.ThemeDictionaries[name];
            if(!ReferenceEquals(before[name],dictionary["SpanAccentBrush"]) || dictionary.Count!=2)
                throw new Exception(name+" system contrast resources were overwritten at startup.");
        }
        if(app.Resources.ContainsKey("ListViewItemBackgroundSelected")) throw new Exception("Global selection masks contrast dictionaries.");
        return "PASS: actual startup accent code updates ordinary themes and leaves contrast resources and selection lookup intact";
    }
}
'@
Add-Type -TypeDefinition ($header+$apply+$color+$driver)
[AccentProbe]::Run()
