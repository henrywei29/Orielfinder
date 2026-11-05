$ErrorActionPreference = 'Stop'

$layout = Join-Path $PSScriptRoot 'src\Span\Span\bin\x64\Debug\net8.0-windows10.0.19041.0'
$manifestPath = Join-Path $layout 'AppxManifest.xml'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw 'Build SPAN Dev first by running build-dev.ps1.'
}

# Only this development identity may be registered by this script.
[xml]$manifest = [System.IO.File]::ReadAllText($manifestPath)
if ($manifest.Package.Identity.Name -ne 'LocalDevelopment.SpanDev' -or
    $manifest.Package.Identity.Publisher -ne 'CN=SPAN Dev' -or
    $manifest.Package.Applications.Application.Executable -ne 'SpanDev.exe') {
    throw 'Unexpected application identity. SPAN Dev registration stopped.'
}

$package = Get-AppxPackage -Name 'LocalDevelopment.SpanDev'
if ($package -and -not [string]::Equals($package.InstallLocation.TrimEnd('\'), $layout.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'SPAN Dev is already registered from another folder. Registration stopped.'
}
if (-not $package) {
    $developerMode = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
    if ($developerMode.AllowDevelopmentWithoutDevLicense -ne 1) {
        throw 'Enable Windows Developer Mode in Settings, then run start-dev.ps1 again.'
    }
    Add-AppxPackage -Register $manifestPath
    $package = Get-AppxPackage -Name 'LocalDevelopment.SpanDev'
}

# No signing, installer generation, default-association changes or official-app operations.
if (-not $package) { throw 'SPAN Dev registration could not be verified.' }
# Package activation is required for this app's isolated LocalSettings storage.
$applicationId = $package.PackageFamilyName + '!' + $manifest.Package.Applications.Application.Id
Start-Process -FilePath explorer.exe -ArgumentList ('shell:AppsFolder\' + $applicationId)
