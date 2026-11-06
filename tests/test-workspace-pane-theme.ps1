$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Views\WorkspaceFolderPane.cs'))
$method = [regex]::Match($source, '(?sm)^        private (?:static )?Brush\? ResourceBrush\(string key\).*?(?=^        private void AddMode)').Value
if (-not $method) { throw 'Pane brush resolver not found.' }
$probe = @'
#nullable enable annotations
using System;
using System.Collections.Generic;
public class Brush { public string Name; public Brush(string name) { Name=name; } }
public enum ElementTheme { Light, Dark }
public class ResourceDictionary : Dictionary<string,object> {
    public Dictionary<object,object> ThemeDictionaries=new();
}
public class FrameworkElement { public ElementTheme ActualTheme; public ResourceDictionary Resources=new(); }
public class MainWindow { public object Content; }
public class App { public static App Current=new(); public ResourceDictionary Resources=new(); }
namespace Windows.UI.ViewManagement { public class AccessibilitySettings { public static bool Enabled; public bool HighContrast=>Enabled; } }
public class PaneThemeProbe {
    public object _contextHost;
    public static string Run() {
        var root=new FrameworkElement { ActualTheme=ElementTheme.Dark };
        var pane=new PaneThemeProbe { _contextHost=new MainWindow { Content=root } };
        var light=new ResourceDictionary { ["surface"]=new Brush("light") };
        var dark=new ResourceDictionary { ["surface"]=new Brush("dark"), ["fallback"]=new Brush("dark-fallback") };
        var custom=new ResourceDictionary { ["surface"]=new Brush("custom-dark") };
        var contrast=new ResourceDictionary { ["surface"]=new Brush("contrast") };
        App.Current.Resources["surface"]=light["surface"];
        App.Current.Resources["fallback"]=light["surface"];
        App.Current.Resources.ThemeDictionaries["Light"]=light;
        App.Current.Resources.ThemeDictionaries["Dark"]=dark;
        App.Current.Resources.ThemeDictionaries["HighContrast"]=contrast;
        Expect(pane.Read("surface"),"dark","System Light with application Dark");
        root.Resources.ThemeDictionaries["Dark"]=custom;
        Expect(pane.Read("surface"),"custom-dark","Custom window theme");
        Expect(pane.Read("fallback"),"dark-fallback","Partial custom palette fallback");
        root.ActualTheme=ElementTheme.Light;
        Expect(pane.Read("surface"),"light","Return to Light");
        Windows.UI.ViewManagement.AccessibilitySettings.Enabled=true;
        Expect(pane.Read("surface"),"contrast","System contrast overrides ordinary palettes");
        if(pane.Read("missing")!=null) throw new Exception("Missing key should remain unresolved");
        return "PASS: pane colors follow actual window theme, custom palette, fallback and system contrast";
    }
    Brush Read(string key) => (Brush)typeof(PaneThemeProbe).GetMethod("ResourceBrush",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance).Invoke(this,new object[]{key});
    static void Expect(Brush actual,string expected,string scenario) {
        if(actual?.Name!=expected) throw new Exception(scenario+": expected "+expected+", got "+actual?.Name);
    }
'@
Add-Type -TypeDefinition ($probe + $method + "`n}")
[PaneThemeProbe]::Run()
