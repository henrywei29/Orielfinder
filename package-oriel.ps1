param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
[xml]$sourceManifest = Get-Content -LiteralPath (Join-Path $root 'src\OrielFolder\OrielFolder\Package.appxmanifest')
if ($sourceManifest.Package.Identity.Name -ne 'OrielFolder.Desktop' -or $sourceManifest.Package.Identity.Publisher -ne 'CN=OrielFolder') { throw 'This checkout is configured as isolated OrielFolder Dev. Use build-dev.ps1 and start-dev.ps1.' }
$tools = Join-Path (Split-Path $root -Parent) '.tools'
$env:DOTNET_ROOT = Join-Path $tools 'dotnet'
$env:DOTNET_CLI_HOME = Join-Path $tools 'cli-home'
$env:NUGET_PACKAGES = Join-Path $tools 'nuget-packages'
$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$output = 'D:\codex\OrielFolder'
$build = Join-Path $output 'build\net8.0-windows10.0.19041.0\win-x64'
New-Item -ItemType Directory -Path $output -Force | Out-Null
if (!$SkipBuild) {
    & $dotnet build (Join-Path $root 'src\OrielFolder\OrielFolder\OrielFolder.csproj') -c Debug -p:Platform=x64 "-p:OrielOutputPath=$output\build\" -p:AppxPackageSigningEnabled=false -p:GenerateAppxPackageOnBuild=false
    if ($LASTEXITCODE) { throw 'Application build failed.' }
    & $dotnet build (Join-Path $root 'src\OrielFolder\Listary.FileAppPlugin.OrielFolder\Listary.FileAppPlugin.OrielFolder.csproj') -c Release
    if ($LASTEXITCODE) { throw 'Listary adapter build failed.' }
}
$run = Join-Path $output ('distribution-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$stage = Join-Path $run 'package'
$payload = Join-Path $run 'payload'
New-Item -ItemType Directory -Path $stage,$payload -Force | Out-Null
# Fresh staging avoids removing or overwriting any existing build or installed files.
Get-ChildItem -LiteralPath $build -Recurse -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
    $relative = $_.FullName.Substring($build.Length).TrimStart('\')
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $stage 'AppxManifest.xml')
if ($manifest.Package.Identity.Name -ne 'OrielFolder.Desktop' -or $manifest.Package.Identity.Publisher -ne 'CN=OrielFolder') { throw 'Refusing to package another application identity.' }
$runtime = Get-Content -LiteralPath (Join-Path $stage 'OrielFolder.runtimeconfig.json') -Raw | ConvertFrom-Json
if (!$runtime.runtimeOptions.includedFrameworks -or !(Test-Path -LiteralPath (Join-Path $stage 'coreclr.dll'))) { throw 'Self-contained runtime is missing.' }
foreach ($file in @('LICENSE','LICENSE.md','OpenSourceLicenses.md')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination (Join-Path $stage "Licenses\$file") -Force }
$sdk = Join-Path $env:NUGET_PACKAGES 'microsoft.windows.sdk.buildtools\10.0.26100.4654\bin\10.0.26100.0\x64'
$certificates = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=OrielFolder' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddMonths(6) -and $_.FriendlyName -eq 'OrielFolder local distribution signing' })
if ($certificates.Count -gt 1) { throw 'Multiple signing identities found. Select the existing distribution certificate explicitly before packaging.' }
if ($certificates.Count -eq 0) {
    $certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=OrielFolder' -FriendlyName 'OrielFolder local distribution signing' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage DigitalSignature -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(3) -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
} else { $certificate = $certificates[0] }
Export-Certificate -Cert $certificate -FilePath (Join-Path $payload 'OrielFolder.cer') -Type CERT | Out-Null
& (Join-Path $sdk 'makeappx.exe') pack /d $stage /p (Join-Path $payload 'OrielFolder.msix') /o
if ($LASTEXITCODE) { throw 'MSIX packing failed.' }
& (Join-Path $sdk 'signtool.exe') sign /fd SHA256 /sha1 $certificate.Thumbprint /s My (Join-Path $payload 'OrielFolder.msix')
if ($LASTEXITCODE) { throw 'MSIX signing failed.' }
Copy-Item -LiteralPath (Join-Path $root 'src\OrielFolder\OrielFolder\Assets\ProductIcons\blue.ico') -Destination (Join-Path $payload 'OrielFolder.ico')
Copy-Item -LiteralPath (Join-Path $tools 'MicrosoftEdgeWebview2Setup.exe') -Destination $payload
foreach ($file in @('Install-OrielFolder.ps1','Install-OrielFolder-Machine.ps1','Initialize-InstallerPowerShell.ps1')) { Copy-Item -LiteralPath (Join-Path $root "installer\$file") -Destination $payload }
$adapter = Join-Path $payload 'Listary'
New-Item -ItemType Directory -Path $adapter | Out-Null
foreach ($file in @('ListaryPluginManifest.json','Listary.FileAppPlugin.OrielFolder.dll','Icon.png')) {
    Copy-Item -LiteralPath (Join-Path $root "src\OrielFolder\Listary.FileAppPlugin.OrielFolder\bin\Release\net48\$file") -Destination $adapter
}
@'
OrielFolder 1.0.2 — 首次安装说明

支持 Windows 10 2004 或更新版本的 x64 电脑。普通双击运行，请勿使用其他管理员账户启动此向导。
本软件是独立的开源文件管理器分支，不是原项目官方发行版。

本测试发行版使用免费自签名证书。安装向导需要管理员权限。只有勾选同意后，才会将此应用公钥证书加入 Trusted People。不会安装根证书，也不会关闭 SmartScreen、防病毒软件或脚本安全策略。仅本次安装脚本使用进程级 ExecutionPolicy Bypass。
自签名不会获得微软信誉背书：首次下载仍可能出现“未知发布者”或 SmartScreen 提示。请仅安装你信任的来源。

主程序内含 .NET 与 Windows App SDK 运行文件。Word/Excel/PowerPoint 预览需要本机已安装相应 Microsoft Office；WebView2 预览需要 Microsoft Edge WebView2 Runtime；Office 转换与 Listary 插件需要 .NET Framework 4.8。
Listary 6 是可选组件，不包含 Listary 本身。勾选集成后，如 Listary 正在运行，请按提示从托盘退出，再点击重试。其他 Listary 插件会保留。

安装不会卸载官方 Orielfolder 或开发版，不修改 Windows 默认文件关联。确认 OrielFolder 可正常使用后，你可以自行在 Windows 设置中卸载旧应用。原开发版配置可以通过“配置导出/导入”迁移。
应用图标默认为蓝白款；设置中可切换窗口和托盘图标。Windows 开始菜单和安装列表保留蓝白主图标。
卸载主应用：Windows 设置 > 应用 > OrielFolder > 卸载。安装支持文件保留在 Program Files\OrielFolder Setup，证书和可选 Listary 插件不会自动删除，避免影响后续重装。
许可证、原版权声明随应用保存在 Licenses 中。分发时请同时提供对应源码压缩包。
'@ | Set-Content -LiteralPath (Join-Path $payload 'install-notice.txt') -Encoding UTF8
$compiler = Join-Path $tools 'InnoSetup\ISCC.exe'
& $compiler "/DPayload=$payload" "/DOutput=$run" (Join-Path $root 'installer\OrielFolder.iss')
if ($LASTEXITCODE) { throw 'EXE installer compilation failed.' }
$installer = Join-Path $run 'OrielFolder-1.0.2-x64-Setup.exe'
& (Join-Path $sdk 'signtool.exe') sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $installer
if ($LASTEXITCODE) { throw 'Installer signing failed.' }
$sourceArchive = Join-Path $run 'OrielFolder-1.0.2-source.zip'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($sourceArchive, [IO.Compression.ZipArchiveMode]::Create)
try {
    $sourceFiles = @()
    foreach ($folder in @('src','installer','tests','third_party')) {
        $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|\.git|node_modules|TestResults)[\\/]' -and
            $_.Name -notmatch '\.(pfx|p12|key|cer|log|user)$|^\.env' -and
            !($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
        }
    }
    foreach ($name in @('LICENSE','LICENSE.md','OpenSourceLicenses.md','global.json','prepare-oriel-icons.ps1','package-oriel.ps1')) { $sourceFiles += Get-Item -LiteralPath (Join-Path $root $name) }
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($root.Length).TrimStart('\').Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
[pscustomobject]@{ Installer=$installer; Source=$sourceArchive; Package=(Join-Path $payload 'OrielFolder.msix'); Certificate=(Join-Path $payload 'OrielFolder.cer'); Staging=$stage } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'artifacts.json') -Encoding UTF8
Write-Output "Installer built: $installer"
