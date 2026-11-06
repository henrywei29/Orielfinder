$ErrorActionPreference = 'Stop'
$targets = @(
    'Views\DetailsModeView.xaml.cs',
    'Views\ListModeView.xaml.cs',
    'Views\IconModeView.xaml.cs',
    'MainWindow.KeyboardHandler.cs'
)
$failures = @()
$checked = 0
foreach ($target in $targets) {
    $path = Join-Path $PSScriptRoot ("..\src\OrielFolder\OrielFolder\" + $target)
    $source = [IO.File]::ReadAllText($path)
    $calls = [regex]::Matches($source, '(?m)^\s*.*?await (?:ContextMenuService|_contextMenuService)\.Build(?<kind>File|Folder|EmptyArea)MenuAsync\((?<target>\w+),[^\r\n]*')
    if (-not $calls.Count) { throw "FAIL: no item/background menu paths were found in $target" }
    foreach ($call in $calls) {
        $before = $source.Substring(0, $call.Index).TrimEnd()
        $lastLine = ($before -split "`n")[-1].Trim()
        $context = if ($call.Groups['kind'].Value -eq 'EmptyArea') { 'SetLastEmptyAreaContext' } else { 'SetLastMenuContext' }
        $selected = $call.Groups['target'].Value
        if ($lastLine -notmatch ('(?:ContextMenuService|_contextMenuService)\.' + $context + '\(' + [regex]::Escape($selected) + ',\s*(?:ContextMenuHost|this),')) {
            $failures += "${target}: $($call.Groups['kind'].Value) path '$selected' has no matching target/host registration before building its popup"
        }
        $checked++
    }
}
if ($failures.Count) { $failures | ForEach-Object { Write-Output "FAIL: $_" }; throw 'Manual shell loading can use missing or stale view context.' }
"PASS: $checked Details/List/Icon/keyboard item and background menu paths register their current target and host before building"
