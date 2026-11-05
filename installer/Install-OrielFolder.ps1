param([switch]$DesktopShortcut)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
try {
    $packagePath = Join-Path $PSScriptRoot 'OrielFolder.msix'
    $certificatePath = Join-Path $PSScriptRoot 'OrielFolder.cer'
    $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2 $certificatePath
    $signature = Get-AuthenticodeSignature -LiteralPath $packagePath
    if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw 'The package signature does not match the included certificate.'
    }
    # Certificate/plugin phase was executed by the signed elevated installer from Program Files.
    # This phase must run as the original interactive user.
    $webViewKeys = @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')
    $hasWebView = @($webViewKeys | ForEach-Object {
        $value = (Get-ItemProperty -LiteralPath $_ -Name pv -ErrorAction SilentlyContinue).pv
        if ($value -and $value -ne '0.0.0.0') { $value }
    }).Count -gt 0
    if (!$hasWebView) {
        $answer = [Windows.Forms.MessageBox]::Show('预览需要 Microsoft WebView2。是否现在联网下载并安装微软运行库？','OrielFolder','YesNo','Information')
        if ($answer -ne 'Yes') { throw '请安装 Microsoft WebView2 Runtime 后重新运行安装程序。' }
        $bootstrapper = Join-Path $PSScriptRoot 'MicrosoftEdgeWebview2Setup.exe'
        $runtimeSignature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
        if ($runtimeSignature.Status -ne 'Valid' -or $runtimeSignature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'WebView2 installer signature validation failed.' }
        $runtime = Start-Process -FilePath $bootstrapper -ArgumentList '/silent','/install' -Wait -PassThru
        if ($runtime.ExitCode -notin @(0,3010)) { throw 'WebView2 安装未完成，请检查网络后重试。' }
    }
    Add-AppxPackage -Path $packagePath -ErrorAction Stop
    $package = Get-AppxPackage -Name 'OrielFolder.Desktop' | Where-Object Publisher -EQ 'CN=OrielFolder' | Select-Object -First 1
    if (!$package) { throw 'OrielFolder package registration could not be verified.' }
    if ($DesktopShortcut) {
        $path = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'OrielFolder.lnk'
        $shell = New-Object -ComObject WScript.Shell
        if (Test-Path -LiteralPath $path) {
            $existing = $shell.CreateShortcut($path)
            if ($existing.Arguments -ne "shell:AppsFolder\$($package.PackageFamilyName)!App") { throw 'An unrelated OrielFolder shortcut already exists; it was not overwritten.' }
        }
        $link = $shell.CreateShortcut($path)
        $link.TargetPath = "$env:SystemRoot\explorer.exe"
        $link.Arguments = "shell:AppsFolder\$($package.PackageFamilyName)!App"
        $link.IconLocation = (Join-Path $PSScriptRoot 'OrielFolder.ico') + ',0'
        $link.Description = 'OrielFolder'
        $link.Save()
    }
    $integrationPath = Join-Path $PSScriptRoot 'integration.json'
    if (Test-Path -LiteralPath $integrationPath) {
        $integration = Get-Content -LiteralPath $integrationPath -Raw | ConvertFrom-Json
        if ($integration.Restart -and $integration.Executable) { Start-Process -FilePath $integration.Executable }
    }
    exit 0
} catch {
    [Windows.Forms.MessageBox]::Show(('安装未完成。现有其他应用不会被卸载。' + [Environment]::NewLine + $_.Exception.Message),'OrielFolder','OK','Error') | Out-Null
    exit 1
}
