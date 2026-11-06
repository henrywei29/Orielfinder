param([string]$ArtifactsDirectory = 'D:\codex\Orielfolder-dev\installer-security-tests')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bootstrap = Join-Path $root 'installer\Initialize-InstallerPowerShell.ps1'
if (!(Test-Path -LiteralPath $bootstrap)) { throw 'Installer module initialization is missing.' }
$initialization = [IO.File]::ReadAllText($bootstrap)
$originalModulePath = $env:PSModulePath
[IO.Directory]::CreateDirectory($ArtifactsDirectory) | Out-Null
$unsigned = Join-Path $ArtifactsDirectory 'unsigned.ps1'
[IO.File]::WriteAllText($unsigned, '# Disposable unsigned signature test; never executed.')
$foreignModules = Join-Path $PSHOME 'Modules'
if ($PSVersionTable.PSEdition -ne 'Core') { throw 'Run this regression from PowerShell 7 to reproduce inherited module paths.' }

foreach ($systemDirectory in @('System32','SysWOW64')) {
    $exe = Join-Path $env:SystemRoot "$systemDirectory\WindowsPowerShell\v1.0\powershell.exe"
    foreach ($mode in @('normal','inherited-core','missing-system-path')) {
        $body = @'
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$policyPreferenceBefore = [Environment]::GetEnvironmentVariable('PSExecutionPolicyPreference')
__INITIALIZATION__
Add-Type -AssemblyName System.Windows.Forms
$signature = Get-AuthenticodeSignature -LiteralPath "$PSHOME\powershell.exe"
if ($signature.Status -ne 'Valid') { throw 'Signed Windows binary was not verified.' }
$unsigned = Get-AuthenticodeSignature -LiteralPath '__UNSIGNED__'
if ($unsigned.Status -ne 'NotSigned') { throw 'Unsigned input must not be trusted.' }
if ((Get-Module Microsoft.PowerShell.Security).Path -notlike "$PSHOME\Modules\*") { throw 'Non-native Security module loaded.' }
if ([Environment]::GetEnvironmentVariable('PSExecutionPolicyPreference') -cne $policyPreferenceBefore) { throw 'Execution policy preference changed.' }
'PASS'
'@
        $body = $body.Replace('__INITIALIZATION__', $initialization).Replace('__UNSIGNED__', $unsigned.Replace("'", "''"))
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $exe
        $start.Arguments = '-NoProfile -NonInteractive -EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($body))
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        switch ($mode) {
            normal { $start.EnvironmentVariables.Remove('PSModulePath') }
            inherited-core { $start.EnvironmentVariables['PSModulePath'] = $foreignModules + ';' + (Split-Path $exe) + '\Modules' }
            missing-system-path { $start.EnvironmentVariables['PSModulePath'] = Join-Path $ArtifactsDirectory 'not-a-module-directory' }
        }
        $process = [Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0 -or $stdout.Trim() -ne 'PASS') { throw "$systemDirectory / $mode failed: $stderr $stdout" }
        "$systemDirectory / $mode : PASS (Valid signed input, NotSigned unsigned input, unchanged policy preference)"
        $process.Dispose()
    }
}
if ($env:PSModulePath -cne $originalModulePath) { throw 'Parent module environment changed.' }

foreach ($name in @('Install-OrielFolder.ps1','Install-OrielFolder-Machine.ps1')) {
    $source = [IO.File]::ReadAllText((Join-Path $root "installer\$name"))
    if ($source.IndexOf('Initialize-InstallerPowerShell.ps1') -lt 0 -or $source.IndexOf('Initialize-InstallerPowerShell.ps1') -gt $source.IndexOf('Add-Type')) { throw "$name must initialize before module-dependent commands." }
    if ($source -notmatch 'Get-AuthenticodeSignature' -or $source -notmatch '\$signature.SignerCertificate.Thumbprint -ne \$certificate.Thumbprint') { throw "$name lost its signer validation." }
    $tokens = $null; $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors) { throw "$name parse failed: $parseErrors" }
}
$packaging = [IO.File]::ReadAllText((Join-Path $root 'package-oriel.ps1'))
if ($packaging -notmatch "'Initialize-InstallerPowerShell.ps1'") { throw 'Initializer missing from package payload.' }
'PASS: both installer phases wired, signer checks retained, packaging includes initializer, parent environment unchanged.'
