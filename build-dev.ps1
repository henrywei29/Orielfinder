param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$localTools = Join-Path (Split-Path $projectRoot -Parent) '.tools'
$localDotnet = Join-Path $localTools 'dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
    $env:DOTNET_ROOT = Split-Path $dotnet -Parent
    $env:DOTNET_CLI_HOME = Join-Path $localTools 'cli-home'
    $env:APPDATA = Join-Path $localTools 'cli-home\AppData\Roaming'
    $env:LOCALAPPDATA = Join-Path $localTools 'cli-home\AppData\Local'
    $env:NUGET_PACKAGES = Join-Path $localTools 'nuget-packages'
    $nugetSource = $env:NUGET_PACKAGES
} else {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $nugetSource = 'https://api.nuget.org/v3/index.json'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:MSBUILDNOINPROCNODE = '0'

# Build only. Signing, packaging, deployment and cleanup are deliberately absent.
Get-Process -Name 'OrielFolderDev','OrielFolderDev.Thumbs','OrielFolderDev.DocumentPreview.Worker' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Push-Location -LiteralPath $projectRoot
try {
    & $dotnet build 'src\OrielFolder\OrielFolder\OrielFolder.csproj' -m:1 -nr:false -c $Configuration -p:Platform=x64 `
        -p:AppxPackageSigningEnabled=false -p:GenerateAppxPackageOnBuild=false `
        --source $nugetSource
    if ($LASTEXITCODE -ne 0) { throw "OrielFolder build failed (exit $LASTEXITCODE)." }
} finally {
    Pop-Location
}
