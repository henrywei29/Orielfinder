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
$dll = Join-Path $repo 'src\OrielFolder\Listary.FileAppPlugin.OrielFolder\bin\Debug\net48\Listary.FileAppPlugin.OrielFolder.dll'
[void][Reflection.Assembly]::LoadFrom($dll)
$plugin = New-Object Listary.FileAppPlugin.OrielFolder.OrielFolderPlugin
if (-not $plugin.IsOpenedFolderProvider -or $plugin.IsQuickSwitchTarget) { throw 'Unexpected plugin role.' }
$expectedExe = Join-Path $repo 'src\OrielFolder\OrielFolder\bin\x64\Debug\net8.0-windows10.0.19041.0\OrielFolderDev.exe'
$matched = $false
foreach ($process in @(Get-Process -Name OrielFolderDev -ErrorAction Stop)) {
    if (-not [string]::Equals($process.Path, $expectedExe, [StringComparison]::OrdinalIgnoreCase)) { continue }
    $bound = $plugin.BindFileWindow($process.MainWindowHandle)
    if ($null -eq $bound) { continue }
    $tab = $bound.GetCurrentTab().GetAwaiter().GetResult()
    $folder = ([Listary.FileAppPlugin.IGetFolder]$tab).GetCurrentFolder().GetAwaiter().GetResult()
    if ([string]::Equals($folder.TrimEnd('\'), $ExpectedPath.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        $matched = $true
    }
}
if (-not $matched) { throw 'The real plugin did not read the expected disposable folder from OrielFolder Dev.' }
foreach ($official in @(Get-Process -Name Span, OrielFolder -ErrorAction SilentlyContinue)) {
    if ($null -ne $plugin.BindFileWindow($official.MainWindowHandle)) { throw 'Plugin unexpectedly bound the official application.' }
}
'PASS: real .NET Framework plugin using installed Listary interfaces reads the expected folder from the isolated OrielFolder Dev process'
'PASS: adapter never binds the official OrielFolder / legacy SPAN process'
