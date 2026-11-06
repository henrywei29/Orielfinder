param([string]$PackagePath)
$ErrorActionPreference = 'Stop'
if ($PackagePath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $package = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        foreach ($file in @(
            'StoreLogo.png',
            'Square44x44Logo.png',
            'Square44x44Logo.scale-200.png',
            'Square44x44Logo.targetsize-24_altform-unplated.png',
            'Square44x44Logo.targetsize-32_altform-unplated.png',
            'Square44x44Logo.targetsize-48_altform-unplated.png',
            'Square44x44Logo.targetsize-256_altform-unplated.png',
            'Square150x150Logo.png',
            'Square150x150Logo.scale-200.png',
            'Wide310x150Logo.png',
            'Wide310x150Logo.scale-200.png',
            'SplashScreen.png',
            'SplashScreen.scale-200.png'
        )) {
            $entry = $package.GetEntry("Assets/$file")
            if (!$entry -or $entry.Length -eq 0) { throw "MSIX icon resource missing: Assets/$file" }
        }
    } finally { $package.Dispose() }
    'PASS: MSIX contains its manifest logos and their scale/size variants.'
    return
}
$source = Get-Content (Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Services\ProductIconService.cs') -Raw
# Compile the real selection logic; only windowing/settings dependencies are replaced.
$source = $source.Replace('namespace OrielFolder.Services;', 'namespace OrielFolder.Services {') + "`n}"
$stub = @'
namespace Microsoft.UI.Xaml {
 public class Window { public IconWindow AppWindow = new IconWindow(); }
 public class IconWindow { public string Path; public void SetIcon(string path) { Path = path; } }
}
namespace OrielFolder.Helpers { public static class DebugLogger { public static void Log(string text) {} } }
namespace OrielFolder.Services {
 public class SettingsService { public string Choice; public string Get(string key, string fallback) { return Choice ?? fallback; } }
}
'@
Add-Type -TypeDefinition ($source + $stub) -IgnoreWarnings
foreach ($inputValue in @($null, '', 'blue', 'invalid', '..\other')) {
    if ([OrielFolder.Services.ProductIconService]::Normalize($inputValue) -ne 'blue') { throw 'Invalid selection did not fall back to blue.' }
}
if ([OrielFolder.Services.ProductIconService]::Normalize('classic') -ne 'classic') { throw 'Classic selection lost.' }
$settings = New-Object OrielFolder.Services.SettingsService
$window = New-Object Microsoft.UI.Xaml.Window
$settings.Choice = 'classic'
[OrielFolder.Services.ProductIconService]::Apply($window, $settings)
if ($window.AppWindow.Path -notlike '*Assets/ProductIcons/classic.ico') { throw 'Selected icon was not applied.' }
foreach ($name in @('blue','classic')) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot "..\src\OrielFolder\OrielFolder\Assets\ProductIcons\$name.ico"))
    if ([BitConverter]::ToUInt16($bytes,2) -ne 1 -or [BitConverter]::ToUInt16($bytes,4) -ne 7) { throw 'Invalid multi-resolution ICO.' }
}
'PASS: blue default, classic selection, invalid-value fallback, window application and both seven-size ICO containers.'
