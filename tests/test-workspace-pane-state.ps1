$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$modelPath = Join-Path $repo 'src\Span\Span\Models\WorkspacePaneState.cs'
if (-not (Test-Path -LiteralPath $modelPath)) { throw 'Independent workspace panes are not implemented.' }
$geometry = [IO.File]::ReadAllText((Join-Path $repo 'src\Span\Span\Models\PaneLayoutGeometry.cs'))
$geometry = $geometry.Replace('namespace Span.Models;', 'namespace Span.Models {') + "`n}"
$model = [IO.File]::ReadAllText($modelPath)
$stubs = @'
namespace Span.ViewModels { public class ExplorerViewModel {} }
namespace Span.Models { public enum ViewMode { MillerColumns, Details, IconMedium, List, Home } }
'@
Add-Type -TypeDefinition ("#nullable enable`n" + $geometry + "`n" + $stubs + "`n" + $model)
$first = [Span.Models.WorkspaceLayoutState]::new()
$second = [Span.Models.WorkspaceLayoutState]::new()
$a = $first.Add([Span.ViewModels.ExplorerViewModel]::new(), [Span.Models.ViewMode]::MillerColumns)
$b = $first.Add([Span.ViewModels.ExplorerViewModel]::new(), [Span.Models.ViewMode]::Details)
$c = $second.Add([Span.ViewModels.ExplorerViewModel]::new(), [Span.Models.ViewMode]::List)
$first.Activate($a)
if ($first.ActivePane -ne $a -or $second.ActivePane -ne $c) { throw 'Activating a pane leaked across tab groups.' }
$first.MoveBefore($b, $a)
if ($first.Panes[0] -ne $b -or $first.ActivePane -ne $a) { throw 'Reordering lost the active pane.' }
if (-not $first.Remove($a) -or $first.ActivePane -ne $b) { throw 'Closing the active pane did not select the retained pane.' }
if ($first.Remove($b)) { throw 'Closing the final pane would discard the whole folder group.' }
if ($second.Panes.Count -ne 1 -or $second.ActivePane -ne $c) { throw 'Closing a pane changed another tab group.' }
Write-Output 'PASS: independent pane collections, selection, ordering and closing'
$first.Layout = [Span.Models.PaneLayoutKind]::Floating
$b.Bounds = [Span.Models.PaneBounds]::new(40, 50, 300, 220)
$first.Layout = [Span.Models.PaneLayoutKind]::Tiled
$first.Layout = [Span.Models.PaneLayoutKind]::Floating
if ($b.Bounds.X -ne 40 -or $first.Panes[0].Explorer -ne $b.Explorer) { throw 'Changing layout discarded folder or floating position.' }
Write-Output 'PASS: layout changes retain folder identity and floating positions'
