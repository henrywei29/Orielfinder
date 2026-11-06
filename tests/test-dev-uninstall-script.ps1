$ErrorActionPreference = 'Stop'
# Static test for the Dev uninstall helper: no registry or file writes. Runs on Windows PowerShell 5.1.
. (Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Assets\DevUninstall\Uninstall-OrielFolderDev.ps1')

$alias = 'C:\Users\test\AppData\Local\Microsoft\WindowsApps\spanfinder.exe'
$command = '"' + $alias + '" "%1"'
$missing = { param($p) $false }
$exists = { param($p) $true }
function Assert-Stale($snapshot, $fileExists, [bool]$expected, [string]$name) {
    if ((Test-StaleOpenCommand $snapshot $fileExists) -ne $expected) { throw "FAIL: $name" }
    "PASS: $name"
}
$names = @('', 'DelegateExecute')
Assert-Stale @{Command=$command;Delegate='';Names=$names;Subkeys=@()} $missing $true 'stale command to a missing exe is eligible'
Assert-Stale @{Command=$command;Delegate=$null;Names=@('');Subkeys=@()} $missing $true 'missing DelegateExecute value is eligible'
Assert-Stale @{Command=$command.ToUpperInvariant();Delegate='';Names=$names;Subkeys=@()} $missing $true 'case is ignored'
Assert-Stale @{Command=$command;Delegate='';Names=$names;Subkeys=@()} $exists $false 'installed file manager is preserved'
Assert-Stale @{Command=$command;Delegate='{11dbb47c-a525-400b-9e80-a54615a090c0}';Names=$names;Subkeys=@()} $missing $false 'delegate handler is preserved'
Assert-Stale @{Command=$command;Delegate='';Names=@('','DelegateExecute','Extra');Subkeys=@()} $missing $false 'extra values are preserved'
Assert-Stale @{Command=$command;Delegate='';Names=$names;Subkeys=@('Child')} $missing $false 'subkeys are preserved'
Assert-Stale @{Command=($alias + ' %1');Delegate='';Names=$names;Subkeys=@()} $missing $false 'unquoted command is preserved'
Assert-Stale @{Command='"relative.exe" "%1"';Delegate='';Names=$names;Subkeys=@()} $missing $false 'relative path is preserved'
Assert-Stale @{Command=$null;Delegate=$null;Names=@();Subkeys=@()} $missing $false 'missing command is a no-op'

# The helper derives its own layout (never trusts plan paths); same values as DevUninstallPlan / AppIdentity.
$testLayout = Get-ExpectedLayout 'C:\Users\test\AppData\Local' 'C:\Users\test\Documents' $true
if ((@($testLayout.FoldersToDelete) -join '|') -ne 'C:\Users\test\AppData\Local\OrielFolderDev|D:\codex\OrielFolderDev\preview-cache') { throw 'FAIL: codex layout delete list' }
if ($testLayout.BackupRoot -ne 'D:\codex\OrielFolderDev\backups') { throw 'FAIL: codex backup root' }
'PASS: layout with D:\codex'
$docLayout = Get-ExpectedLayout 'C:\Users\test\AppData\Local' 'C:\Users\test\Documents' $false
if ((@($docLayout.FoldersToDelete) -join '|') -ne 'C:\Users\test\AppData\Local\OrielFolderDev') { throw 'FAIL: documents layout delete list' }
if ($docLayout.BackupRoot -ne 'C:\Users\test\Documents\OrielFolderDev\backups') { throw 'FAIL: documents backup root' }
'PASS: layout without D:\codex'

function Assert-Inside([string]$path, [bool]$expected) {
    if ((Test-InsideBackupRoot $path $testLayout) -ne $expected) { throw "FAIL: backup-root $path -> expected $expected" }
    "PASS: backup-root $path -> $expected"
}
Assert-Inside 'D:\codex\OrielFolderDev\backups\uninstall-20261106-123045\result.json' $true
Assert-Inside 'D:\codex\OrielFolderDev\backups\uninstall-20261106-123045\registry' $true
Assert-Inside 'D:\codex\OrielFolderDev\backups' $false
Assert-Inside 'D:\codex\OrielFolderDev\backups\..\..\..\Windows\x.json' $false
Assert-Inside 'C:\Windows\System32\drivers\etc\hosts' $false
Assert-Inside '' $false

function Assert-Allowed([string]$path, [bool]$expected) {
    if ((Test-AllowedDeletion $path $testLayout) -ne $expected) { throw "FAIL: allow-list $path -> expected $expected" }
    "PASS: allow-list $path -> $expected"
}
Assert-Allowed 'C:\Users\test\AppData\Local\OrielFolderDev' $true
Assert-Allowed 'D:\CODEX\OrielFolderDev\preview-cache\' $true
Assert-Allowed 'C:\Program Files\Listary\FileAppPlugins\OrielFolderDev' $true
Assert-Allowed 'D:\codex\OrielFolderDev\marked-files' $false
Assert-Allowed 'D:\codex\OrielFolderDev\marked-files\a.pdf' $false
Assert-Allowed 'D:\codex\OrielFolderDev\backups' $false
Assert-Allowed 'D:\codex\OrielFolderDev' $false
Assert-Allowed 'C:\Users\test\AppData\Local' $false
Assert-Allowed 'C:\' $false
Assert-Allowed 'C:\Program Files\Listary\FileAppPlugins' $false
Assert-Allowed 'C:\Program Files\Listary\FileAppPlugins\OrielFolder' $false
Assert-Allowed 'relative\OrielFolderDev' $false
'PASS: Dev uninstall helper rules match StaleFolderOpenRule / DevUninstallPlan'
