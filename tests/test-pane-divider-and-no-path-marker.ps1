$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src\Span\Span'
$main = Get-Content "$root\MainWindow.xaml.cs" -Raw
if ($main -match 'GetOrCreateIndicator|ApplyPathIndicators|OnPathHighlightsUpdated') { throw 'Floating file path marker is still created or scheduled.' }
$xaml = [xml](Get-Content "$root\MainWindow.xaml" -Raw)
$ns = [Xml.XmlNamespaceManager]::new($xaml.NameTable)
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$divider = $xaml.SelectSingleNode('//*[@x:Name="PaneDivider"]',$ns)
if ($divider.GetAttribute('Grid.Column') -ne '1' -or $divider.GetAttribute('Canvas.ZIndex') -ne '1') { throw 'Divider must own the splitter column, not overlap the right pane.' }
$sizing=Get-Content "$root\MainWindow.PaneSizing.cs" -Raw
if ($sizing -notmatch 'SplitterCol.Width = new GridLength\(1\)' -or $sizing -notmatch 'SizeWestEast') { throw 'Visible split line needs nonzero layout space and resize cursor.' }
'PASS: independent one-pixel split column with drag cursor; no floating path marker.'
