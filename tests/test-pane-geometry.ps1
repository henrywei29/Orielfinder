$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repo 'src\OrielFolder\OrielFolder\Models\PaneLayoutGeometry.cs'
if (-not (Test-Path -LiteralPath $source)) { throw 'Pane layout geometry is not implemented.' }
Add-Type -TypeDefinition ([IO.File]::ReadAllText($source))

function Assert-Near([double] $Actual, [double] $Expected, [string] $Message) {
    if (-not [double]::IsFinite($Actual) -or [Math]::Abs($Actual - $Expected) -gt 0.000001) {
        throw "$Message Expected $Expected, received $Actual."
    }
}

foreach ($case in @(
    @(1000, 0.3, 0.3), @(1000, -1, 0.15), @(1000, 2, 0.85),
    @(200, 0.8, 0.5), @(300, 0, 0.5), @(0, 0.2, 0.5),
    @(-10, 0.2, 0.5), @([double]::NaN, 0.2, 0.5),
    @([double]::PositiveInfinity, 0.2, 0.5), @(1000, [double]::NaN, 0.5),
    @(1000, [double]::PositiveInfinity, 0.5)
)) {
    Assert-Near ([OrielFolder.Models.PaneLayoutGeometry]::ClampSplitRatio($case[0], $case[1])) $case[2] 'Split ratio must preserve visible panes.'
}
Write-Output 'PASS: split ratios preserve minimum sizes and reject non-finite inputs'

foreach ($case in @(
    @(900, 100, 100, 1920, 480, 1000), @(1800, 300, 100, 1920, 480, 1820),
    @(900, -200, 100, 1920, 480, 700), @(500, -400, 100, 1920, 480, 480),
    @(900, 100, -1800, 0, 480, 1000), @(1600, 400, -1800, 0, 480, 1800),
    @(900, 0, 1700, 1920, 480, 220), @(900, 0, 2000, 1920, 480, 0),
    @([int]::MaxValue, [int]::MaxValue, 0, 1920, 480, 1920),
    @(900, [int]::MinValue, 0, 1920, 480, 480),
    @(900, 0, [int]::MinValue, [int]::MaxValue, 480, 900)
)) {
    $actual = [OrielFolder.Models.PaneLayoutGeometry]::WindowWidthForColumnDrag($case[0], $case[1], $case[2], $case[3], $case[4])
    if ($actual -ne $case[5]) { throw "Window bounds expected $($case[5]), received $actual." }
}
Write-Output 'PASS: window growth and shrink stay in positive and negative monitor work areas'

foreach ($case in @(@(2, 2, 1000, 600), @(3, 2, 900, 600), @(5, 3, 1200, 800), @(3, 0, 900, 600), @(2, 8, 1000, 600))) {
    $count, $requestedColumns, $width, $height = $case
    $columns = [Math]::Clamp($requestedColumns, 1, $count)
    $rows = [Math]::Ceiling($count / $columns)
    $tiles = [OrielFolder.Models.PaneLayoutGeometry]::Tile($count, $requestedColumns, $width, $height)
    if ($tiles.Length -ne $count) { throw 'Grid must retain every pane.' }
    for ($index = 0; $index -lt $count; $index++) {
        Assert-Near $tiles[$index].X (($index % $columns) * $width / $columns) 'Grid column position.'
        Assert-Near $tiles[$index].Y ([Math]::Floor($index / $columns) * $height / $rows) 'Grid row position.'
        Assert-Near $tiles[$index].Width ($width / $columns) 'Grid cell width.'
        Assert-Near $tiles[$index].Height ($height / $rows) 'Grid cell height.'
    }
}
foreach ($hostSize in @(@(0, 0), @(-20, -30), @([double]::NaN, [double]::PositiveInfinity))) {
    foreach ($tile in [OrielFolder.Models.PaneLayoutGeometry]::Tile(3, 2, $hostSize[0], $hostSize[1])) {
        Assert-Near $tile.X 0 'Invalid host X.'
        Assert-Near $tile.Y 0 'Invalid host Y.'
        Assert-Near $tile.Width 0 'Invalid host width.'
        Assert-Near $tile.Height 0 'Invalid host height.'
    }
}
if ([OrielFolder.Models.PaneLayoutGeometry]::Tile(0, 2, 1000, 600).Length -ne 0 -or [OrielFolder.Models.PaneLayoutGeometry]::Tile(-1, 2, 1000, 600).Length -ne 0) {
    throw 'An empty pane collection must produce an empty layout.'
}
Write-Output 'PASS: regular grids cover 2, 3, and 5 panes and handle empty or invalid hosts'

$weighted = [OrielFolder.Models.PaneLayoutGeometry]::Tile(4, 2, 1000, 800, [double[]]@(1, 3), [double[]]@(3, 1))
foreach ($case in @(@(0, 0, 0, 250, 600), @(1, 250, 0, 750, 600), @(2, 0, 600, 250, 200), @(3, 250, 600, 750, 200))) {
    $tile = $weighted[$case[0]]
    Assert-Near $tile.X $case[1] 'Weighted grid X.'
    Assert-Near $tile.Y $case[2] 'Weighted grid Y.'
    Assert-Near $tile.Width $case[3] 'Weighted grid width.'
    Assert-Near $tile.Height $case[4] 'Weighted grid height.'
}
$partial = [OrielFolder.Models.PaneLayoutGeometry]::Tile(3, 2, 1000, 800, [double[]]@(1, 3), [double[]]@(3, 1))
if ($partial.Length -ne 3) { throw 'Weighted layout must not create an unused cell as a pane.' }
Assert-Near $partial[2].Y 600 'Partial final weighted row.'
foreach ($invalidWeights in @(@(0, 3), @(-1, 3), @([double]::NaN, 3), @([double]::PositiveInfinity, 3), @(1), @())) {
    $tiles = [OrielFolder.Models.PaneLayoutGeometry]::Tile(4, 2, 1000, 800, [double[]]$invalidWeights, [double[]]$invalidWeights)
    Assert-Near $tiles[0].Width 500 'Invalid column weights must use equal sizes.'
    Assert-Near $tiles[0].Height 400 'Invalid row weights must use equal sizes.'
    Assert-Near $tiles[3].X 500 'Equal fallback column position.'
    Assert-Near $tiles[3].Y 400 'Equal fallback row position.'
}
$unweighted = [OrielFolder.Models.PaneLayoutGeometry]::Tile(4, 2, 1000, 800, $null, $null)
Assert-Near $unweighted[0].Width 500 'Missing column weights must use equal sizes.'
Assert-Near $unweighted[0].Height 400 'Missing row weights must use equal sizes.'
$largeWeights = [OrielFolder.Models.PaneLayoutGeometry]::Tile(4, 2, 1000, 800, [double[]]@([double]::MaxValue, [double]::MaxValue), [double[]]@(3, 1))
Assert-Near $largeWeights[0].Width 500 'Finite weight totals must not overflow.'
Assert-Near $largeWeights[0].Height 600 'Each axis must retain its own proportions.'
Write-Output 'PASS: weighted grid dividers size rows and columns independently with equal fallback for invalid weights'

foreach ($case in @(
    @(50, 60, 300, 200, 1000, 600, 50, 60, 300, 200),
    @(-50, -60, 300, 200, 1000, 600, 0, 0, 300, 200),
    @(950, 550, 300, 200, 1000, 600, 700, 400, 300, 200),
    @(20, 30, 20, 30, 1000, 600, 20, 30, 150, 120),
    @(500, 300, 1500, 1000, 800, 500, 0, 0, 800, 500),
    @(700, 400, 300, 200, 100, 80, 0, 0, 100, 80),
    @(700, 400, 300, 200, 1400, 900, 700, 400, 300, 200),
    @([double]::NaN, [double]::NegativeInfinity, [double]::NaN, [double]::PositiveInfinity, 1000, 600, 0, 0, 150, 120),
    @(10, 20, 300, 200, 0, -5, 0, 0, 0, 0),
    @(10, 20, 300, 200, [double]::NaN, [double]::PositiveInfinity, 0, 0, 0, 0)
)) {
    $bounds = [OrielFolder.Models.PaneBounds]::new($case[0], $case[1], $case[2], $case[3])
    $actual = [OrielFolder.Models.PaneLayoutGeometry]::ConstrainFloating($bounds, $case[4], $case[5])
    Assert-Near $actual.X $case[6] 'Floating pane X.'
    Assert-Near $actual.Y $case[7] 'Floating pane Y.'
    Assert-Near $actual.Width $case[8] 'Floating pane width.'
    Assert-Near $actual.Height $case[9] 'Floating pane height.'
}
Write-Output 'PASS: floating drag and resize keep complete panes visible after host shrink and growth'
