$ErrorActionPreference = 'Stop'
$views = Join-Path $PSScriptRoot '..\src\Span\Span\Views'
$failures = [Collections.Generic.List[string]]::new()
foreach ($case in @(@('DetailsModeView','DetailsListView','OnDetailsKeyDown'), @('IconModeView','IconGridView','OnIconKeyDown'), @('ListModeView','ListGridView','OnListKeyDown'))) {
    $name, $hostName, $handlerName = $case
    $source = [IO.File]::ReadAllText((Join-Path $views ($name+'.xaml.cs')))
    $xaml = [IO.File]::ReadAllText((Join-Path $views ($name+'.xaml')))
    $tag = [regex]::Match($xaml, ('(?s)<(?:ListView|GridView)\b[^>]*x:Name="'+$hostName+'"[^>]*>')).Value
    if (!$tag) { throw "Cannot find $name's actual items host." }
    $registrations = [regex]::Matches($source, ($hostName+'\.AddHandler\(UIElement\.KeyDownEvent,\s*new Microsoft\.UI\.Xaml\.Input\.KeyEventHandler\('+$handlerName+'\),\s*(?:true|false)\);'))
    $wiring = ($registrations | ForEach-Object {$_.Value}) -join "`n"
    if ($tag -match ('KeyDown="'+$handlerName+'"')) {
        $wiring += "`n$hostName.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler($handlerName), false);"
    }
    # Exercise production registration statements with both routed-event states.
    # WinUI list items consume Space before its bubbling KeyDown reaches their host.
    $className = 'Opening'+$name
    $prefix = @'
using System;
using System.Collections.Generic;
namespace Microsoft.UI.Xaml.Input {
    public class KeyRoutedEventArgs : EventArgs { public bool Handled; }
    public delegate void KeyEventHandler(object sender, KeyRoutedEventArgs args);
}
'@
    $model = @"
public class $className {
    private static class UIElement { public static readonly object KeyDownEvent = new object(); }
    private class Host {
        private readonly List<(Microsoft.UI.Xaml.Input.KeyEventHandler handler, bool too)> handlers = new();
        public void AddHandler(object routedEvent, Microsoft.UI.Xaml.Input.KeyEventHandler handler, bool handledEventsToo) => handlers.Add((handler, handledEventsToo));
        public void Raise(bool consumedByList) {
            var args = new Microsoft.UI.Xaml.Input.KeyRoutedEventArgs { Handled = consumedByList };
            foreach(var item in handlers) if (!args.Handled || item.too) item.handler(this, args);
        }
    }
    private readonly Host $hostName = new Host();
    public int Opened;
    public $className() { $wiring }
    private void $handlerName(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args) { Opened++; args.Handled = true; }
    public void Space(bool consumedByList) => $hostName.Raise(consumedByList);
}
"@
    Add-Type -TypeDefinition (($prefix + $model).Replace('Microsoft.UI.Xaml.Input', ($className+'Input')))
    foreach($consumed in @($true,$false)) {
        $harness = New-Object $className
        $harness.Space($consumed)
        if($harness.Opened -ne 1) { $failures.Add("$name consumed=${consumed}: expected one preview toggle; actual=$($harness.Opened)") }
    }
}
if($failures.Count) { throw ($failures -join "`n") }
'PASS: Details/Icon/List Space reaches Quick Look exactly once even when the native list consumes the routed event'
