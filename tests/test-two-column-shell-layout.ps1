$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.xaml'
$xml=[Xml.XmlDocument]::new()
$xml.Load((Resolve-Path $path).Path)
$ns=[Xml.XmlNamespaceManager]::new($xml.NameTable)
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
function Named($name){$xml.SelectSingleNode("//*[@x:Name='$name']",$ns)}
$root=Named 'RootGrid'
foreach($name in @('SidebarBorder','SidebarSplitter','AppTitleBar','MainArea')){
 if((Named $name).ParentNode -ne $root){throw "$name must share the root shell grid"}
}
if((Named 'SidebarBorder').GetAttribute('Grid.RowSpan') -ne '4'){throw 'Sidebar does not span the full window height'}
foreach($name in @('AppTitleBar','MainArea')){
 if((Named $name).GetAttribute('Grid.Column') -ne '2'){throw "$name does not align to the file workspace"}
}
if(Named 'AppTitleText'){throw 'OrielFolder Dev text still occupies the tab row'}
foreach($pair in @(@('ViewControlsPanel','0'),@('FileActionsPanel','2'))){
 if((Named $pair[0]).GetAttribute('Grid.Column') -ne $pair[1]){throw 'Toolbar must keep views at left and file actions at right'}
}
if((Named 'ToolbarLayout').GetAttribute('MinWidth') -notmatch 'ActualWidth.*ToolbarViewport'){throw 'Toolbar cannot fill available width while retaining narrow-window scroll'}
foreach($name in @('ToolbarCutButton','ToolbarCopyButton','ToolbarPasteButton','SearchBox')){
 $node=Named $name;while($node -and $node -ne (Named 'FileActionsPanel')){$node=$node.ParentNode}
 if(!$node){throw "$name is not in the right action area"}
}
'PASS: full-height sidebar, aligned file workspace, no brand text, right actions and narrow-window scrolling.'
