# An intermediate launcher can pass PowerShell 7 module paths to Windows
# PowerShell 5.1. The installer uses only Windows inbox modules. Limit discovery
# to this host's modules for this process only; never change user/machine settings.
$env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')
Import-Module ([IO.Path]::Combine($PSHOME, 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1')) -ErrorAction Stop
