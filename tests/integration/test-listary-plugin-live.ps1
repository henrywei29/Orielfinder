param([Parameter(Mandatory=$true)][string]$ExpectedPath)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixtureRoot = 'D:\codex\Orielfolder-development\manual-data\'
if (-not $ExpectedPath.StartsWith($fixtureRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Live validation accepts disposable development directories only.'
}
$listary = Get-Process -Name Listary -ErrorAction Stop | Select-Object -First 1
$listaryDirectory = Split-Path -Parent $listary.Path
[void][Reflection.Assembly]::LoadFrom((Join-Path $listaryDirectory 'Listary.FileAppPlugin.dll'))
$dll = Join-Path $repo 'src\Span\Listary.FileAppPlugin.SpanDev\bin\Debug\net48\Listary.FileAppPlugin.SpanDev.dll'
[void][Reflection.Assembly]::LoadFrom($dll)
$plugin = New-Object Listary.FileAppPlugin.SpanDev.SpanDevPlugin
if (-not $plugin.IsOpenedFolderProvider -or $plugin.IsQuickSwitchTarget) { throw 'Unexpected plugin role.' }
$expectedExe = Join-Path $repo 'src\Span\Span\bin\x64\Debug\net8.0-windows10.0.19041.0\SpanDev.exe'
$matched = $false
foreach ($process in @(Get-Process -Name SpanDev -ErrorAction Stop)) {
    if (-not [string]::Equals($process.Path, $expectedExe, [StringComparison]::OrdinalIgnoreCase)) { continue }
    $bound = $plugin.BindFileWindow($process.MainWindowHandle)
    if ($null -eq $bound) { continue }
    $tab = $bound.GetCurrentTab().GetAwaiter().GetResult()
    $folder = ([Listary.FileAppPlugin.IGetFolder]$tab).GetCurrentFolder().GetAwaiter().GetResult()
    if ([string]::Equals($folder.TrimEnd('\'), $ExpectedPath.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        $matched = $true
    }
}
if (-not $matched) { throw 'The real plugin did not read the expected disposable folder from SPAN Dev.' }
foreach ($official in @(Get-Process -Name Span -ErrorAction SilentlyContinue)) {
    if ($null -ne $plugin.BindFileWindow($official.MainWindowHandle)) { throw 'Plugin unexpectedly bound the official application.' }
}
'PASS: real .NET Framework plugin using installed Listary interfaces reads the expected folder from the isolated SPAN Dev process'
'PASS: adapter never binds the official SPAN process'
