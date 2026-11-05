param([switch]$InstallListary)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
function Assert-NoLinks([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Installer paths cannot contain symbolic links or junctions.' }
        }
        $parent = Split-Path -Path $current -Parent
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (!(New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator permission is required.' }
    $certificatePath = Join-Path $PSScriptRoot 'OrielFolder.cer'
    $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2 $certificatePath
    if ($certificate.Subject -ne 'CN=OrielFolder' -or $certificate.HasPrivateKey) { throw 'Unexpected certificate.' }
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $PSScriptRoot 'OrielFolder.msix')
    if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw 'Package signer mismatch.' }
    $ListaryDirectory = ''
    $restart = $false
    if ($InstallListary) {
        foreach ($candidate in @("$env:ProgramW6432\Listary", "${env:ProgramFiles(x86)}\Listary")) {
            if (Test-Path -LiteralPath (Join-Path $candidate 'Listary.exe')) { $ListaryDirectory = $candidate; break }
        }
        if (!$ListaryDirectory) {
            [Windows.Forms.MessageBox]::Show('未找到 Program Files 中的 Listary 6。本次只安装主程序；请安装 Listary 6 后重新运行安装程序添加集成。','OrielFolder') | Out-Null
        } else {
            while (@(Get-Process Listary -ErrorAction SilentlyContinue).Count -gt 0) {
                $answer = [Windows.Forms.MessageBox]::Show('请从托盘退出 Listary，然后点击重试。取消将跳过插件，继续安装主程序。','OrielFolder','RetryCancel','Information')
                if ($answer -ne 'Retry') { $ListaryDirectory = ''; break }
                $restart = $true
            }
        }
    }
    # Validate the optional adapter destination before making any machine changes.
    if ($ListaryDirectory) {
        $ListaryDirectory = (Resolve-Path -LiteralPath $ListaryDirectory).ProviderPath
        $exe = Join-Path $ListaryDirectory 'Listary.exe'
        if ((Get-Item -LiteralPath $exe).VersionInfo.FileMajorPart -ne 6) { throw 'This adapter requires Listary 6. Listary was not modified.' }
        $api = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $ListaryDirectory 'Listary.FileAppPlugin.dll'))
        if ($api.Version.Major -ne 1) { throw 'Unsupported Listary plugin API.' }
        if (@(Get-Process Listary -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Please exit Listary before installing its adapter.' }
        $source = Join-Path $PSScriptRoot 'Listary'
        $manifest = Get-Content -LiteralPath (Join-Path $source 'ListaryPluginManifest.json') -Raw | ConvertFrom-Json
        if ($manifest.Id -ne 'OrielFolder' -or $manifest.Entry -ne 'Listary.FileAppPlugin.OrielFolder.dll') { throw 'Unexpected adapter identity.' }
        $destination = Join-Path $ListaryDirectory 'FileAppPlugins\OrielFolder'
        Assert-NoLinks $destination
        foreach ($name in @('ListaryPluginManifest.json','Listary.FileAppPlugin.OrielFolder.dll','Icon.png')) {
            Assert-NoLinks (Join-Path $destination $name)
            Assert-NoLinks (Join-Path $source $name)
        }
        if (Test-Path -LiteralPath $destination) {
            if ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Adapter destination cannot be a link.' }
            $old = Get-Content -LiteralPath (Join-Path $destination 'ListaryPluginManifest.json') -Raw | ConvertFrom-Json
            if ($old.Id -ne 'OrielFolder') { throw 'An unrelated adapter occupies the destination.' }
            $backup = Join-Path $PSScriptRoot ('ListaryBackups\' + [Guid]::NewGuid().ToString('N'))
            Assert-NoLinks $backup
            New-Item -ItemType Directory -Path $backup -Force | Out-Null
            foreach ($name in @('ListaryPluginManifest.json','Listary.FileAppPlugin.OrielFolder.dll','Icon.png')) {
                $file = Join-Path $destination $name
                if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination $backup }
            }
        }
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        foreach ($name in @('ListaryPluginManifest.json','Listary.FileAppPlugin.OrielFolder.dll','Icon.png')) {
            Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $destination $name) -Force
        }
    }
    if (!(Test-Path -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)")) {
        Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    }
    @{ Restart=($restart -and [bool]$ListaryDirectory); Executable=$(if ($ListaryDirectory) { Join-Path $ListaryDirectory 'Listary.exe' } else { '' }) } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'integration.json') -Encoding UTF8
    exit 0
} catch {
    [Windows.Forms.MessageBox]::Show($_.Exception.Message,'OrielFolder setup','OK','Error') | Out-Null
    exit 1
}
