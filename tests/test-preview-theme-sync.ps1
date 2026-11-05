$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\MainWindow.SettingsHandler.cs'))
$themeCase = [regex]::Match($source, '(?s)case "Theme":.*?break;').Value
if (!$themeCase) { throw 'Cannot locate actual theme setting handler.' }
$harness = @'
using System;
namespace Helpers { public static class DispatcherHelper { public static void SafeEnqueue(object queue, Action action) => action(); } }
public class Preview { public Action Sync; public void SyncTheme() => Sync(); }
public class Harness {
    public object DispatcherQueue = new object();
    public string Applied;
    public int Synced;
    public string Seen;
    public Preview _quickLookWindow;
    public void Open() { _quickLookWindow = new Preview { Sync = () => { Synced++; Seen = Applied; } }; }
    private void ApplyTheme(string theme) { Applied = theme; }
    public void Change(string key, object value) { switch(key) {
'@
Add-Type -TypeDefinition ($harness + $themeCase + ' } } }')
$window = [Harness]::new()
$window.Change('Theme', 'dark')
if ($window.Applied -ne 'dark') { throw 'Application theme no longer applies without a preview.' }
$window.Open()
foreach ($theme in @('light','dark','system')) {
    $previous = $window.Synced
    $window.Change('Theme', $theme)
    if ($window.Synced -ne $previous + 1 -or $window.Seen -ne $theme) {
        throw 'An already-open independent preview must synchronize after the application theme changes.'
    }
}
'PASS: actual settings handler updates both application and already-open preview themes, in order'
