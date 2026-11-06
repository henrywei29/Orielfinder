$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
$failures = [System.Collections.Generic.List[string]]::new()
$main = Get-Content "$root\MainWindow.xaml.cs" -Raw
$details = Get-Content "$root\Views\DetailsModeView.xaml.cs" -Raw
if ($main -match 'ScheduleDownloadsGroupingIfNeeded') { $failures.Add('Navigation still forces date grouping in Downloads.') }
if ($main -notmatch 'TryGetValue\("ExplicitViewGroupBy"' -or $details -notmatch 'TryGetValue\("ExplicitGroupBy"') {
    $failures.Add('Old automatically persisted date groups still override the default flat list.')
}
foreach ($file in @('MainWindow.KeyboardHandler.cs','MainWindow.CommandPaletteHandler.cs','Views\DetailsModeView.xaml.cs','Views\ListModeView.xaml.cs','Views\IconModeView.xaml.cs','Views\SettingsModeView.xaml','Views\SettingsModeView.xaml.cs')) {
    if ((Get-Content "$root\$file" -Raw) -match 'EnableWasdNavigation|HandleViewWasd|WasdNavToggle|SettingsToggleWasd') { $failures.Add("WASD navigation entry remains: $file") }
}
if ($main -notmatch '_currentGroupBy = "None"' -or $details -notmatch '_currentGroupBy = "None"') { $failures.Add('Flat default missing.') }
$menu = Get-Content "$root\Services\ContextMenuService.cs" -Raw
if ($menu -notmatch 'host.ApplyGroupBy\("DateModified"\)' -or $menu -notmatch 'host.ApplyGroupBy\("None"\)') { $failures.Add('Manual grouping choices removed.') }
$keyboard = Get-Content "$root\MainWindow.KeyboardHandler.cs" -Raw
if ($keyboard -match 'HandleUpArrow|HandleDownArrow|// Shift\+[WASD]:') { $failures.Add('Orphaned WASD key dispatch remains.') }
foreach ($command in @('HandleSelectAll','HandleLeftArrow','HandleRightArrow','HandleEnter')) {
    if ($keyboard -notmatch $command) { $failures.Add("Standard keyboard command missing: $command") }
}
if ($failures.Count) { throw ($failures -join "`n") }
'PASS: no automatic date grouping, flat defaults, manual grouping retained, WASD removed, standard navigation retained.'
