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
    $env:NUGET_PACKAGES = Join-Path $localTools 'nuget-packages'
} else {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'

# Build only. Signing, packaging, deployment and cleanup are deliberately absent.
Push-Location -LiteralPath $projectRoot
try {
    & $dotnet build 'src\Span\Span\Span.csproj' -c $Configuration -p:Platform=x64 `
        -p:AppxPackageSigningEnabled=false -p:GenerateAppxPackageOnBuild=false `
        --source 'https://api.nuget.org/v3/index.json'
    if ($LASTEXITCODE -ne 0) { throw "SPAN Dev build failed (exit $LASTEXITCODE)." }
} finally {
    Pop-Location
}
