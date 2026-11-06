$ErrorActionPreference = 'Stop'
$tools = Join-Path (Split-Path $PSScriptRoot -Parent) '.tools'
$dotnet = Join-Path $tools 'dotnet\dotnet.exe'
if (Test-Path -LiteralPath $dotnet) {
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:DOTNET_CLI_HOME = Join-Path $tools 'cli-home'
    $env:APPDATA = Join-Path $tools 'cli-home\AppData\Roaming'
    $env:LOCALAPPDATA = Join-Path $tools 'cli-home\AppData\Local'
    $env:NUGET_PACKAGES = Join-Path $tools 'nuget-packages'
    $nugetSource = $env:NUGET_PACKAGES
} else {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $nugetSource = 'https://api.nuget.org/v3/index.json'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:MSBUILDNOINPROCNODE = '0'
Push-Location -LiteralPath $PSScriptRoot
try {
    # Build only; this script never writes to Listary's installation or configuration.
    & $dotnet build 'src\OrielFolder\Listary.FileAppPlugin.OrielFolder\Listary.FileAppPlugin.OrielFolder.csproj' -m:1 -nr:false -c Debug `
        --source $nugetSource
    if ($LASTEXITCODE -ne 0) { throw 'Listary plugin build failed.' }
} finally { Pop-Location }
