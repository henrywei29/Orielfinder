$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$listary = Get-Process -Name Listary -ErrorAction SilentlyContinue | Select-Object -First 1
$directory = if ($listary) { Split-Path -Parent $listary.Path } else { 'C:\Program Files\Listary' }
if (-not (Test-Path -LiteralPath (Join-Path $directory 'Listary.FileAppPlugin.Host.dll'))) {
    'SKIP: installed Listary 6 host is unavailable'
    exit 0
}
& (Join-Path $repo 'build-listary-plugin.ps1')
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = 'D:\codex\Orielfolder-development\ListaryHostLoadTest.exe'
& $compiler /nologo /target:exe /platform:x64 ('/out:' + $output) `
    ('/reference:' + (Join-Path $directory 'Listary.FileAppPlugin.Host.dll')) `
    ('/reference:' + (Join-Path $directory 'Listary.FileAppPlugin.dll')) `
    ('/reference:' + (Join-Path $directory 'Microsoft.Extensions.Logging.Abstractions.dll')) `
    (Join-Path $PSScriptRoot 'integration\ListaryHostLoadTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Official-host test compilation failed.' }
& $output (Join-Path $repo 'src\OrielFolder\Listary.FileAppPlugin.OrielFolder\bin\Debug\net48') $directory
if ($LASTEXITCODE -ne 0) { throw 'Installed Listary host rejected the plugin.' }
