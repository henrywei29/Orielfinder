param([string]$Plan, [string]$ClassesSubKey = 'Software\Classes')
# OrielFolder Dev uninstall helper. Started ELEVATED by DevUninstallService (outside the MSIX container, so it
# sees the real HKCU and %LOCALAPPDATA%). It never removes the app package; the app does that last.
# Rules mirror Services/DevUninstall/StaleFolderOpenRule.cs and DevUninstallPlan.IsAllowedDeletion.
$ErrorActionPreference = 'Stop'

function Test-StaleOpenCommand($Snapshot, [scriptblock]$FileExists) {
    if (!$Snapshot.Command) { return $false }
    $m = [regex]::Match([string]$Snapshot.Command, '^"(?<path>[^"]+)" "%1"$', 'IgnoreCase')
    if (!$m.Success) { return $false }
    $target = $m.Groups['path'].Value
    if (![IO.Path]::IsPathRooted($target)) { return $false }
    if ($Snapshot.Delegate) { return $false }
    if (@($Snapshot.Subkeys).Count -ne 0) { return $false }
    if (@($Snapshot.Names) -notcontains '') { return $false }
    if (@($Snapshot.Names | Where-Object { $_ -ne '' -and $_ -ne 'DelegateExecute' }).Count -ne 0) { return $false }
    return -not (& $FileExists $target)
}

function Get-NormalizedPath([string]$Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Test-SameOrUnder([string]$Path, [string]$Root) {
    return $Path -ieq $Root -or $Path.StartsWith($Root + '\', [StringComparison]::OrdinalIgnoreCase)
}

# The plan file lives in a user-writable folder, so this elevated script never trusts paths from it.
# Everything it may delete or write is derived here, from the same rules as AppIdentity / DevUninstallPlan.
$script:DataFolderName = 'OrielFolderDev'

function Get-ExpectedLayout([string]$LocalAppData, [string]$Documents, [bool]$HasCodex) {
    $base = if ($HasCodex) { "D:\codex\$($script:DataFolderName)" } else { Join-Path $Documents $script:DataFolderName }
    $delete = @(Join-Path $LocalAppData $script:DataFolderName)
    if ($HasCodex) { $delete += "D:\codex\$($script:DataFolderName)\preview-cache" }   # else it lives under LocalData
    return [pscustomobject]@{
        DataFolderName = $script:DataFolderName
        FoldersToDelete = $delete
        ProtectedFolders = @((Join-Path $base 'marked-files'), (Join-Path $base 'backups'))
        BackupRoot = Join-Path $base 'backups'
    }
}

function Test-AllowedDeletion([string]$Path, $Layout) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -notmatch '^[A-Za-z]:\\') { return $false }
    $p = Get-NormalizedPath $Path
    foreach ($k in @($Layout.ProtectedFolders)) {
        $kp = Get-NormalizedPath $k
        if ((Test-SameOrUnder $p $kp) -or (Test-SameOrUnder $kp $p)) { return $false }
    }
    foreach ($d in @($Layout.FoldersToDelete)) { if ($p -ieq (Get-NormalizedPath $d)) { return $true } }
    if ($p.Length -le 3) { return $false }   # drive root
    $parent = [IO.Path]::GetDirectoryName($p)
    return [bool]$parent -and [IO.Path]::GetFileName($p) -ieq $Layout.DataFolderName -and [IO.Path]::GetFileName($parent) -ieq 'FileAppPlugins'
}

function Test-InsideBackupRoot([string]$Path, $Layout) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -notmatch '^[A-Za-z]:\\') { return $false }
    $p = Get-NormalizedPath $Path
    $root = Get-NormalizedPath $Layout.BackupRoot
    return $p -ne $root -and (Test-SameOrUnder $p $root)
}

function Read-CommandSnapshot($Key) {
    if (!$Key) { return @{ Command=$null; Delegate=$null; Names=@(); Subkeys=@() } }
    return @{
        Command=$Key.GetValue('', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        Delegate=$Key.GetValue('DelegateExecute', $null)
        Names=@($Key.GetValueNames())
        Subkeys=@($Key.GetSubKeyNames())
    }
}

if (!$Plan) { return }   # dot-sourced by tests: define functions only

$steps = New-Object System.Collections.ArrayList
function Add-Step([string]$Name, [string]$Status, [string]$Detail) {
    [void]$steps.Add([pscustomobject]@{ Name=$Name; Status=$Status; Detail=$Detail })
}

$planData = Get-Content -LiteralPath $Plan -Raw -Encoding UTF8 | ConvertFrom-Json
$layout = Get-ExpectedLayout ([Environment]::GetFolderPath('LocalApplicationData')) `
    ([Environment]::GetFolderPath('MyDocuments')) (Test-Path -LiteralPath 'D:\codex')
$resultPath = [string]$planData.ResultPath
if (!(Test-InsideBackupRoot $resultPath $layout) -or !(Test-InsideBackupRoot $planData.RegistryBackupDirectory $layout)) {
    exit 2   # refuse to write anywhere else; the app reports "no result"
}
if ($planData.DataFolderName -ne $layout.DataFolderName) { exit 2 }
try {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($sid -ne $planData.ExpectedUserSid) {
        throw "The administrator account differs from the signed-in user ($sid). Nothing was changed."
    }
    $fileExists = { param($p) Test-Path -LiteralPath ([Environment]::ExpandEnvironmentVariables($p)) }

    # 1. Stale folder-open overrides (real HKCU only; HKLM never touched)
    New-Item -ItemType Directory -Path $planData.RegistryBackupDirectory -Force | Out-Null
    $notify = $false
    foreach ($class in @('Folder','Directory','Drive')) {
        $path = "$ClassesSubKey\$class\shell\open"
        $open = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path, $true)
        if (!$open) { Add-Step "folder-open:$class" 'skipped' 'no override'; continue }
        try {
            $command = $open.OpenSubKey('command')
            try { $snap = Read-CommandSnapshot $command } finally { if ($command) { $command.Dispose() } }
            if (!(Test-StaleOpenCommand $snap $fileExists)) { Add-Step "folder-open:$class" 'skipped' 'not a stale override'; continue }
            $backup = Join-Path $planData.RegistryBackupDirectory "$class-open.reg"
            & "$env:SystemRoot\System32\reg.exe" export "HKCU\$path" $backup /y | Out-Null
            if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $backup)) { throw "Registry backup failed for $class." }
            $command = $open.OpenSubKey('command')
            try { $again = Read-CommandSnapshot $command } finally { if ($command) { $command.Dispose() } }
            if (!(Test-StaleOpenCommand $again $fileExists)) { throw "$class changed during backup." }
            $open.DeleteSubKey('command', $false)
            $empty = $open.ValueCount -eq 0 -and $open.SubKeyCount -eq 0
        } finally { $open.Dispose() }
        if ($empty) {
            $shell = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$ClassesSubKey\$class\shell", $true)
            try { if ($shell) { $shell.DeleteSubKey('open', $false) } } finally { if ($shell) { $shell.Dispose() } }
        }
        $notify = $true
        Add-Step "folder-open:$class" 'done' "removed stale command $($snap.Command); backup $backup"
    }
    if ($notify) {
        Add-Type -Namespace OrielFolderDevUninstall -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int e, uint f, System.IntPtr a, System.IntPtr b);'
        [OrielFolderDevUninstall.Shell]::SHChangeNotify(0x08000000, 0x1000, [IntPtr]::Zero, [IntPtr]::Zero)
    }

    # 2. Listary plugin folder (Dev footprint of connect-listary.ps1)
    $listary = Get-Process -Name Listary -ErrorAction SilentlyContinue | Select-Object -First 1
    $listaryDir = if ($listary -and $listary.Path) { Split-Path -Parent $listary.Path } else { Join-Path $env:ProgramFiles 'Listary' }
    $plugin = Join-Path (Join-Path $listaryDir 'FileAppPlugins') $layout.DataFolderName
    if (Test-Path -LiteralPath $plugin) {
        if (!(Test-AllowedDeletion $plugin $layout)) { throw "Refusing to delete $plugin" }
        if ($listary) { throw 'Listary is running and has the OrielFolder Dev plugin loaded. Quit Listary, then retry.' }
        Remove-Item -LiteralPath $plugin -Recurse -Force
        Add-Step 'listary-plugin' 'done' $plugin
    } else { Add-Step 'listary-plugin' 'skipped' 'not installed' }

    # 3. Out-of-package data folders
    foreach ($folder in @($layout.FoldersToDelete)) {
        if (!(Test-AllowedDeletion $folder $layout)) { throw "Refusing to delete $folder" }
        if (Test-Path -LiteralPath $folder) {
            Remove-Item -LiteralPath $folder -Recurse -Force
            if (Test-Path -LiteralPath $folder) { throw "Could not delete $folder" }
            Add-Step "delete:$folder" 'done' ''
        } else { Add-Step "delete:$folder" 'skipped' 'not present' }
    }
    $ok = $true
} catch {
    Add-Step 'error' 'failed' $_.Exception.Message
    $ok = $false
} finally {
    [pscustomobject]@{ Ok=$ok; Steps=$steps } | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $resultPath -Encoding UTF8
}
if (!$ok) { exit 1 }
