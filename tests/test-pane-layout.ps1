$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$xaml = [xml][IO.File]::ReadAllText((Join-Path $repo 'src\Span\Span\MainWindow.xaml'))
$names = [Xml.XmlNamespaceManager]::new($xaml.NameTable)
$names.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
$divider = $xaml.SelectSingleNode('//*[@x:Name="PaneDivider"]', $names)
if (-not $divider -or $divider.GetAttribute('PointerMoved') -ne 'OnPaneDividerDrag' -or
    $divider.GetAttribute('PointerPressed') -ne 'OnPaneDividerDragStarted' -or
    $divider.GetAttribute('PointerReleased') -ne 'OnPaneDividerDragCompleted' -or
    $divider.GetAttribute('PointerCaptureLost') -ne 'OnPaneDividerCaptureLost') {
    throw 'The existing thin pane divider must support dragging.'
}
if ($divider.LocalName -ne 'Border' -or $divider.GetAttribute('Width') -ne '1' -or
    $divider.GetAttribute('IsHitTestVisible') -ne 'True' -or $divider.HasChildNodes) {
    throw 'Only the existing one-pixel divider may be the drag target.'
}
Write-Output 'PASS: existing one-pixel divider is the drag target'

$geometryPath = Join-Path $repo 'src\Span\Span\Models\PaneLayoutGeometry.cs'
Add-Type -TypeDefinition ([IO.File]::ReadAllText($geometryPath))
foreach ($case in @(
    @{ Available = 1000; Desired = 0.3; Expected = 0.3 },
    @{ Available = 1000; Desired = 0.01; Expected = 0.15 },
    @{ Available = 1000; Desired = 0.99; Expected = 0.85 },
    @{ Available = 200; Desired = 0.8; Expected = 0.5 }
)) {
    $actual = [Span.Models.PaneLayoutGeometry]::ClampSplitRatio($case.Available, $case.Desired)
    if ([Math]::Abs($actual - $case.Expected) -gt 0.001) { throw 'Pane ratio lost its minimum visible size.' }
    Write-Output "PASS: split ratio $($case.Desired) at width $($case.Available)"
}
foreach ($case in @(
    @{ Width = 900; Delta = 100; Left = 100; Right = 1920; Expected = 1000 },
    @{ Width = 1800; Delta = 300; Left = 100; Right = 1920; Expected = 1820 },
    @{ Width = 900; Delta = -200; Left = 100; Right = 1920; Expected = 700 },
    @{ Width = 500; Delta = -400; Left = 100; Right = 1920; Expected = 480 },
    @{ Width = 900; Delta = 100; Left = -1800; Right = 0; Expected = 1000 }
)) {
    $actual = [Span.Models.PaneLayoutGeometry]::WindowWidthForColumnDrag($case.Width, $case.Delta, $case.Left, $case.Right, 480)
    if ($actual -ne $case.Expected) { throw 'Column drag failed to resize the window within its display work area.' }
    Write-Output "PASS: window width $($case.Width) delta $($case.Delta)"
}
