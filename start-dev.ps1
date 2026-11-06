$ErrorActionPreference = 'Stop'

$layout = Join-Path $PSScriptRoot 'src\OrielFolder\OrielFolder\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64'
if (-not (Test-Path -LiteralPath (Join-Path $layout 'AppxManifest.xml'))) {
    $layout = Join-Path $PSScriptRoot 'src\OrielFolder\OrielFolder\bin\x64\Debug\net8.0-windows10.0.19041.0'
}
$manifestPath = Join-Path $layout 'AppxManifest.xml'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw 'Build OrielFolder Dev first by running build-dev.ps1.'
}

# Only this isolated development identity may be registered by this script.
[xml]$manifest = [System.IO.File]::ReadAllText($manifestPath)
if ($manifest.Package.Identity.Name -ne 'LocalDevelopment.OrielFolderDev' -or
    $manifest.Package.Identity.Publisher -ne 'CN=OrielFolder Dev' -or
    $manifest.Package.Applications.Application.Executable -ne 'OrielFolderDev.exe') {
    throw 'Unexpected application identity. OrielFolder Dev registration stopped.'
}

$staleDev = Get-AppxPackage -Name 'LocalDevelopment.OrielFolder' -ErrorAction SilentlyContinue
foreach ($oldPkg in $staleDev) {
    Remove-AppxPackage -Package $oldPkg.PackageFullName -ErrorAction SilentlyContinue
}

# Stop any headless/stuck OrielFolderDev instances and sync newer compiled DLL from obj if needed.
Get-Process -Name 'OrielFolderDev' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -eq 0 } | Stop-Process -Force -ErrorAction SilentlyContinue
$objDll = Join-Path $PSScriptRoot 'src\OrielFolder\OrielFolder\obj\x64\Debug\net8.0-windows10.0.19041.0\win-x64\OrielFolderDev.dll'
$binDll = Join-Path $layout 'OrielFolderDev.dll'
if ((Test-Path -LiteralPath $objDll) -and (Test-Path -LiteralPath $binDll) -and ((Get-Item -LiteralPath $objDll).LastWriteTimeUtc -gt (Get-Item -LiteralPath $binDll).LastWriteTimeUtc)) {
    Get-Process -Name 'OrielFolderDev','OrielFolderDev.Thumbs','OrielFolderDev.DocumentPreview.Worker' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    Copy-Item -LiteralPath $objDll -Destination $binDll -Force
}

$package = Get-AppxPackage -Name 'LocalDevelopment.OrielFolderDev' | Select-Object -First 1
if ($package -and -not [string]::Equals([string]$package.InstallLocation.TrimEnd('\'), $layout.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    Remove-AppxPackage -Package $package.PackageFullName
    $package = $null
}
if (-not $package) {
    $developerMode = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
    if ($developerMode.AllowDevelopmentWithoutDevLicense -ne 1) {
        throw 'Enable Windows Developer Mode in Settings, then run start-dev.ps1 again.'
    }
    Add-AppxPackage -Register $manifestPath
    $package = Get-AppxPackage -Name 'LocalDevelopment.OrielFolderDev' | Select-Object -First 1
}

# No signing, installer generation, default-association changes or official-app operations.
if (-not $package) { throw 'OrielFolder Dev registration could not be verified.' }
# Package activation is required for this app's isolated LocalSettings storage.
$applicationId = $package.PackageFamilyName + '!' + $manifest.Package.Applications.Application.Id
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DevAppActivator {
    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager { }
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    }
    public static uint Activate(string aumid) {
        var mgr = (IApplicationActivationManager)new ApplicationActivationManager();
        try {
            uint pid;
            int hr = mgr.ActivateApplication(aumid, "", 0, out pid);
            Marshal.ThrowExceptionForHR(hr);
            return pid;
        } finally {
            Marshal.ReleaseComObject(mgr);
        }
    }
}
'@
$launchedPid = [DevAppActivator]::Activate($applicationId)
Write-Output "Started OrielFolder Dev (PID: $launchedPid, Package: $($package.PackageFullName))"
