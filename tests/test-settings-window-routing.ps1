$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '..\src\Span\Span'
$source=Get-Content "$root\MainWindow.SettingsHandler.cs" -Raw
$method=[regex]::Match($source,'(?sm)private void OpenSettingsTab\(\).*?^        }').Value
if($method -notmatch 'SettingsWindow.Show' -or $method -match 'OpenOrSwitchToSettingsTab'){throw 'Settings still navigates the browsing tab'}
$window=Get-Content "$root\Views\SettingsWindow.cs" -Raw
foreach($required in @('static SettingsWindow?','_current.Activate()','SettingChanged -=','UnregisterWindow','OwnerWindow = this')){if(-not $window.Contains($required)){throw "Missing settings lifecycle: $required"}}
'PASS: independent singleton settings route and event cleanup are wired (source check).'
