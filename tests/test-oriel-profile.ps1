$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '..\src\Span\Span'
[xml]$manifest=Get-Content (Join-Path $root 'Package.appxmanifest')
if($manifest.Package.Identity.Name -ne 'OrielFolder.Desktop' -or $manifest.Package.Identity.Publisher -ne 'CN=OrielFolder') { throw 'Distribution must have its own OrielFolder package identity.' }
$identity=Get-Content (Join-Path $root 'Helpers\AppIdentity.cs') -Raw
if($identity -notmatch 'DisplayName = "OrielFolder"' -or $identity -notmatch 'MainExecutableName = "OrielFolder.exe"') { throw 'Product process/data identity is not isolated.' }
$settings=Get-Content (Join-Path $root 'Views\SettingsModeView.xaml') -Raw
if($settings -match 'NavSupport|SupportSection|lumibear|SPAN Dev|Buy me a coffee') {throw 'Upstream promotional UI remains.'}
$plugin=Get-Content (Join-Path $root '..\Listary.FileAppPlugin.SpanDev\ListaryPluginManifest.json') -Raw | ConvertFrom-Json
if($plugin.Id -ne 'OrielFolder' -or $plugin.Entry -ne 'Listary.FileAppPlugin.OrielFolder.dll') {throw 'Adapter would collide with existing development plugin.'}
'PASS: independent OrielFolder app/package/plugin identity and removed upstream promotional UI.'
