$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
[xml]$manifest=Get-Content (Join-Path $root 'Package.appxmanifest')
if($manifest.Package.Identity.Name -ne 'LocalDevelopment.OrielFolderDev' -or $manifest.Package.Identity.Publisher -ne 'CN=OrielFolder Dev') { throw 'Development build must have its own isolated LocalDevelopment.OrielFolderDev package identity.' }
$identity=Get-Content (Join-Path $root 'Helpers\AppIdentity.cs') -Raw
if($identity -notmatch 'DisplayName = "OrielFolder Dev"' -or $identity -notmatch 'MainExecutableName = "OrielFolderDev.exe"' -or $identity -notmatch 'SingleInstanceKey = "ORIEL_FOLDER_DEV_MAIN"') { throw 'Development process/data identity is not isolated from installed OrielFolder.' }
$settings=Get-Content (Join-Path $root 'Views\SettingsModeView.xaml') -Raw
if($settings -match 'NavSupport|SupportSection|lumibear|SPAN Dev|Buy me a coffee') {throw 'Upstream promotional UI remains.'}
$plugin=Get-Content (Join-Path $root '..\Listary.FileAppPlugin.OrielFolder\ListaryPluginManifest.json') -Raw | ConvertFrom-Json
if($plugin.Id -ne 'OrielFolderDev' -or $plugin.Entry -ne 'Listary.FileAppPlugin.OrielFolderDev.dll') {throw 'Adapter would collide with installed OrielFolder plugin.'}
$appSettings=Get-Content (Join-Path $root 'Helpers\AppSettings.cs') -Raw
if($appSettings -match 'OrielFolder\.Desktop') {throw 'AppSettings still references installed OrielFolder.Desktop package family.'}
$shellIdentity=Get-Content (Join-Path $root 'Helpers\DesktopShellIdentity.cs') -Raw
if($shellIdentity -notmatch 'OrielFolderDev\.Desktop\.Win32') {throw 'DesktopShellIdentity is not isolated.'}
$workerProc=Get-Content (Join-Path $root 'Services\Thumbnails\WorkerProcess.cs') -Raw
if($workerProc -notmatch 'OrielFolderDev\.Thumbs\.') {throw 'WorkerProcess pipe prefix is not isolated.'}
'PASS: independent OrielFolder Dev app/package/plugin/data identity isolated from installed OrielFolder.'
