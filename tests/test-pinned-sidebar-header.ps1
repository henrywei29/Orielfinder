$ErrorActionPreference='Stop'
$xml=[Xml.XmlDocument]::new()
$xml.Load((Join-Path $PSScriptRoot '..\src\Span\Span\MainWindow.xaml'))
$ns=[Xml.XmlNamespaceManager]::new($xml.NameTable)
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
function Named($name){$xml.SelectSingleNode("//*[@x:Name='$name']",$ns)}
$homeSection=Named 'SidebarHomeSection'
$scroll=Named 'SidebarNavigationScroll'
if(!$scroll){throw 'The sidebar navigation needs its own scroll region.'}
foreach($name in @('SidebarHomeSection','ToolbarFolderTags')){
 $node=Named $name
 while($node){if($node.LocalName -eq 'ScrollViewer'){throw "$name still scrolls with navigation"};$node=$node.ParentNode}
}
if((Named 'ToolbarFolderTags').ParentNode -ne $homeSection){throw 'Color tags must be grouped immediately below Home.'}
foreach($name in @('SidebarFavoritesSection','SidebarLocalDrivesSection')){
 $node=Named $name
 if(!$node){throw "$name not found"}
 while($node -and $node -ne $scroll){$node=$node.ParentNode}
 if(!$node){throw "$name must remain inside the scroll region"}
}
if($scroll.GetAttribute('Grid.Row') -ne '1'){throw 'Navigation must occupy the middle row'}
$rows=$scroll.ParentNode.SelectSingleNode("*[local-name()='Grid.RowDefinitions']")
if(($rows.ChildNodes | ForEach-Object {$_.GetAttribute('Height')}) -join ',' -ne 'Auto,*,Auto'){throw 'Expected fixed header, flexible list and fixed footer'}
if((Named 'ToolbarFolderTags').LocalName -ne 'Grid'){throw 'Eight dots need width-sharing columns to fit a narrow sidebar'}
$cols=(Named 'ToolbarFolderTags').SelectSingleNode("*[local-name()='Grid.ColumnDefinitions']")
if($cols.ChildNodes.Count -ne 8){throw 'Every dot needs a width-sharing column'}
$layout=Named 'ToolbarLayout'
if((Named 'FileActionsPanel').GetAttribute('Grid.Column') -ne '2' -or $layout.GetAttribute('MinWidth') -notmatch 'ActualWidth.*ToolbarViewport'){throw 'File actions lost their right alignment'}
'PASS: Home and tags fixed above independently scrolling navigation; fixed footer, eight adaptive dots and right-aligned actions retained.'
