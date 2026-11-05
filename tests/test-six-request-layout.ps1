$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '..\src\Span\Span'
foreach($file in @('MainWindow.xaml','Views\DetailsModeView.xaml','Views\ListModeView.xaml','Views\IconModeView.xaml')){
 $s=Get-Content (Join-Path $root $file) -Raw
 [xml]$xml=$s
 if($s -notmatch 'Tag="ItemColorMarker"'){throw "No left tag marker: $file"}
}
$s=Get-Content "$root\MainWindow.WorkspaceLayout.cs" -Raw
if($s -notmatch 'SizeNorthSouth' -or $s -notmatch '_workspaceDividerState'){throw 'Quad divider cursor/lifetime missing'}
$s=Get-Content "$root\MainWindow.DragDropHandler.cs" -Raw
if($s -notmatch '_resizeStartWidth = parentGrid.ActualWidth' -or $s -notmatch 'RememberMillerColumnWidth'){throw 'Miller measured width/persistence missing'}
'PASS: marker templates parse, divider cursor/lifetime/persistence source checks.'
