$ErrorActionPreference = 'Stop'
$tools = Join-Path (Split-Path $PSScriptRoot -Parent) '.tools'
$dotnet = Join-Path $tools 'dotnet\dotnet.exe'
if (Test-Path -LiteralPath $dotnet) {
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:DOTNET_CLI_HOME = Join-Path $tools 'cli-home'
    $env:NUGET_PACKAGES = Join-Path $tools 'nuget-packages'
} else { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location -LiteralPath $PSScriptRoot
try {
    # Build only; this script never writes to Listary's installation or configuration.
    & $dotnet build 'src\Span\Listary.FileAppPlugin.SpanDev\Listary.FileAppPlugin.SpanDev.csproj' -c Debug `
        --source 'https://api.nuget.org/v3/index.json'
    if ($LASTEXITCODE -ne 0) { throw 'Listary plugin build failed.' }
} finally { Pop-Location }
