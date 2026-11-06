param([switch]$Apply, [string]$BackupDirectory)
$ErrorActionPreference = 'Stop'

function Test-StaleSpanCommand($Snapshot, [string]$AliasPath) {
    $expected = '"' + $AliasPath + '" "%1"'
    return $Snapshot.Command -ieq $expected -and
        $Snapshot.Delegate -ceq '' -and
        @($Snapshot.Names).Count -eq 2 -and
        @($Snapshot.Names | Where-Object { $_ -notin @('', 'DelegateExecute') }).Count -eq 0 -and
        @($Snapshot.Subkeys).Count -eq 0
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

if (!$Apply) { return }
if (!$BackupDirectory -or ![IO.Path]::GetFullPath($BackupDirectory).StartsWith('D:\codex\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Select a new backup directory under D:\codex.'
}
if (Test-Path -LiteralPath $BackupDirectory) { throw 'Use a new backup directory to preserve earlier backups.' }
$alias = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Microsoft\WindowsApps\spanfinder.exe'
if (Test-Path -LiteralPath $alias) { throw 'The old application alias still exists. No repair was applied.' }
New-Item -ItemType Directory -Path $BackupDirectory | Out-Null
$results = @()
foreach ($class in @('Folder','Directory','Drive')) {
    $path = "Software\Classes\$class\shell\open"
    $open = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path, $true)
    if (!$open) { $results += "$class already uses inherited Windows settings"; continue }
    try {
        $command = $open.OpenSubKey('command')
        try { $snapshot = Read-CommandSnapshot $command } finally { if ($command) { $command.Dispose() } }
        if (!(Test-StaleSpanCommand $snapshot $alias)) {
            $results += "$class skipped: not the exact obsolete SPAN override"
            continue
        }
        $backup = Join-Path $BackupDirectory "$class-open.reg"
        & "$env:SystemRoot\System32\reg.exe" export "HKCU\$path" $backup /y | Out-Null
        if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $backup)) { throw "Backup failed for $class." }
        $command = $open.OpenSubKey('command')
        try { $current = Read-CommandSnapshot $command } finally { if ($command) { $command.Dispose() } }
        if (!(Test-StaleSpanCommand $current $alias)) { throw "$class registration changed during backup." }
        $open.DeleteSubKey('command', $false)
        $empty = $open.ValueCount -eq 0 -and $open.SubKeyCount -eq 0
    } finally { $open.Dispose() }
    if ($empty) {
        $shell = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Software\Classes\$class\shell", $true)
        try {
            $check = $shell.OpenSubKey('open')
            try { $stillEmpty = $check -and $check.ValueCount -eq 0 -and $check.SubKeyCount -eq 0 }
            finally { if ($check) { $check.Dispose() } }
            if ($stillEmpty) { $shell.DeleteSubKey('open', $false) }
        } finally { if ($shell) { $shell.Dispose() } }
    }
    $results += "$class obsolete override removed after backup"
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OrielfolderAssociationRefresh {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@
[OrielfolderAssociationRefresh]::SHChangeNotify(0x08000000, 0x1000, [IntPtr]::Zero, [IntPtr]::Zero)
$results
"Backup directory: $BackupDirectory"
