param([Parameter(Mandatory=$true)][string]$ResultPath)
$ErrorActionPreference = 'Stop'
$resultFile = [IO.Path]::GetFullPath($ResultPath)
if (-not $resultFile.StartsWith('D:\codex\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Connection results must be saved under D:\codex.'
}
try {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Administrator confirmation is required to add a plugin under Program Files.'
    }
    $listary = Get-Process -Name Listary -ErrorAction SilentlyContinue | Select-Object -First 1
    $directory = if ($listary) { Split-Path -Parent $listary.Path } else { 'C:\Program Files\Listary' }
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $directory 'Listary.exe')).FileMajorPart -ne 6) {
        throw 'This adapter requires Listary 6.'
    }
    if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $directory 'Listary.FileAppPlugin.dll')).Version.Major -ne 1) {
        throw 'Unexpected Listary plugin API version.'
    }
    $source = Join-Path $PSScriptRoot 'src\OrielFolder\Listary.FileAppPlugin.OrielFolder\bin\Debug\net48'
    $manifest = Get-Content -LiteralPath (Join-Path $source 'ListaryPluginManifest.json') -Raw | ConvertFrom-Json
    if ($manifest.Id -ne 'OrielFolderDev' -or $manifest.Entry -ne 'Listary.FileAppPlugin.OrielFolderDev.dll' -or $manifest.ApiVersion -ne 1) {
        throw 'Unexpected development plugin manifest.'
    }
    $files = @('ListaryPluginManifest.json', 'Listary.FileAppPlugin.OrielFolderDev.dll', 'Icon.png')
    foreach ($file in $files) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $file) -PathType Leaf)) { throw 'Build the plugin first.' }
    }
    $target = Join-Path $directory 'FileAppPlugins\OrielFolderDev'
    if (Test-Path -LiteralPath $target) {
        $oldManifest = Get-Content -LiteralPath (Join-Path $target 'ListaryPluginManifest.json') -Raw | ConvertFrom-Json
        if ($oldManifest.Id -ne 'OrielFolderDev' -or $oldManifest.Entry -ne $manifest.Entry) {
            throw 'Existing plugin is not this OrielFolder adapter; update stopped.'
        }
        $backup = Join-Path 'D:\codex\Orielfolder-development\listary-plugin-backups' ([guid]::NewGuid().ToString('N'))
        [void][IO.Directory]::CreateDirectory($backup)
        foreach ($file in $files) {
            if (Test-Path -LiteralPath (Join-Path $target $file) -PathType Leaf) {
                [IO.File]::Copy((Join-Path $target $file), (Join-Path $backup $file), $false)
            }
        }
    }
    [void][IO.Directory]::CreateDirectory($target)
    foreach ($file in $files) { [IO.File]::Copy((Join-Path $source $file), (Join-Path $target $file), $true) }
    [IO.File]::WriteAllText($resultFile, 'SUCCESS: Connected only the OrielFolder adapter. Any previous adapter files were backed up under D:\codex. Restart Listary and check Third-party Integration.')
} catch {
    [IO.File]::WriteAllText($resultFile, ('FAILED: ' + $_.Exception.Message))
    exit 1
}
