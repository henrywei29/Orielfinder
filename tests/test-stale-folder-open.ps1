$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\tools\Repair-StaleFolderOpen.ps1')
$alias = 'C:\Users\test\AppData\Local\Microsoft\WindowsApps\spanfinder.exe'
$command = '"' + $alias + '" "%1"'
function Assert-Repair($snapshot, [bool]$expected, [string]$name) {
    if ((Test-StaleSpanCommand $snapshot $alias) -ne $expected) { throw $name }
    "PASS: $name"
}
$good = @{ Command=$command; Delegate=''; Names=@('','DelegateExecute'); Subkeys=@() }
Assert-Repair $good $true 'exact obsolete SPAN command is eligible'
Assert-Repair (@{Command=$command.ToUpperInvariant();Delegate='';Names=@('','DelegateExecute');Subkeys=@()}) $true 'Windows path case is ignored'
Assert-Repair (@{Command='"C:\app\other.exe" "%1"';Delegate='';Names=@('','DelegateExecute');Subkeys=@()}) $false 'another file manager is preserved'
Assert-Repair (@{Command=$command;Delegate='{other-handler}';Names=@('','DelegateExecute');Subkeys=@()}) $false 'another delegate is preserved'
Assert-Repair (@{Command=$command;Delegate='';Names=@('','DelegateExecute','Extra');Subkeys=@()}) $false 'additional command values require manual review'
Assert-Repair (@{Command=$command;Delegate='';Names=@('','DelegateExecute');Subkeys=@('Child')}) $false 'nested command configuration is preserved'
Assert-Repair (@{Command=$null;Delegate=$null;Names=@();Subkeys=@()}) $false 'missing registration is a safe no-op'
