$ErrorActionPreference = 'Stop'
$views = Join-Path $PSScriptRoot '..\src\Span\Span\Views'
$failures = [Collections.Generic.List[string]]::new()
foreach($case in @(@('DetailsModeView','DetailsListView','OnDetailsKeyDown','OnDetailsPreviewKeyDown'), @('IconModeView','IconGridView','OnIconKeyDown','OnIconPreviewKeyDown'), @('ListModeView','ListGridView','OnListKeyDown','OnListPreviewKeyDown'))) {
    $name,$hostName,$handlerName,$previewName=$case
    $source=[IO.File]::ReadAllText((Join-Path $views ($name+'.xaml.cs')))
    $space=[regex]::Match($source,'(?s)case Windows\.System\.VirtualKey\.Space:.*?(?=case Windows\.System\.VirtualKey\.Home:)').Value
    if(!$space){throw "Cannot extract $name Space handling."}
    $registration=([regex]::Matches($source,($hostName+'\.AddHandler\(UIElement\.KeyDownEvent,\s*new Microsoft\.UI\.Xaml\.Input\.KeyEventHandler\('+$handlerName+'\),\s*true\);')) | ForEach-Object {$_.Value}) -join "`n"
    $previewRegistration=if($source.Contains("$hostName.PreviewKeyDown += $previewName;")){"$hostName.PreviewKeyDown += $previewName;"}else{''}
    $previewMethod=[regex]::Match($source,('(?sm)^        private void '+$previewName+'\(.*?^        }')).Value
    $handledGuard=[regex]::Match($source,'if \(e\.Key == Windows\.System\.VirtualKey\.Space && e\.Handled\) return;').Value
    $className='Selection'+$name
    $model=@"
using System;
using System.Collections.Generic;
namespace Windows.System { public enum VirtualKey { Space,Home } }
namespace Microsoft.UI.Xaml.Input {
    public class KeyRoutedEventArgs : EventArgs { public Windows.System.VirtualKey Key=Windows.System.VirtualKey.Space; public bool Handled; }
    public delegate void KeyEventHandler(object sender,KeyRoutedEventArgs e);
}
public class MainWindow {
    public string Opened;
    public int Toggles;
    public void HandleViewQuickLook(Item item){ Opened=item?.Name; Toggles++; }
    public static char KeyToChar(Windows.System.VirtualKey key)=>'\0';
    public void HandleViewTypeAhead(char ch,Vm vm){}
}
public class Item { public string Name; }
public class Folder { public Item SelectedChild; }
public class Vm { public Folder CurrentFolder=new Folder(); }
public class Settings { public bool EnableQuickLook=true; }
public class $className {
    private static class UIElement { public static readonly object KeyDownEvent=new object(); }
    private class Host {
        public event Microsoft.UI.Xaml.Input.KeyEventHandler PreviewKeyDown;
        private readonly List<(Microsoft.UI.Xaml.Input.KeyEventHandler handler,bool too)> handlers=new();
        public void AddHandler(object routedEvent,Microsoft.UI.Xaml.Input.KeyEventHandler handler,bool too)=>handlers.Add((handler,too));
        public void Space(Action nativeSelection){
            var e=new Microsoft.UI.Xaml.Input.KeyRoutedEventArgs();
            PreviewKeyDown?.Invoke(this,e);
            if(!e.Handled){nativeSelection();e.Handled=true;}
            foreach(var item in handlers)if(!e.Handled||item.too)item.handler(this,e);
        }
    }
    private Host $hostName=new Host();
    private Settings _settings=new Settings();
    private Vm ViewModel=new Vm();
    private MainWindow ContextMenuHost=new MainWindow();
    public $className(){ $registration $previewRegistration }
    $previewMethod
    private void $handlerName(object sender,Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e){
        $handledGuard
        switch(e.Key){$space}
    }
    public string OpenB(){
        ViewModel.CurrentFolder.SelectedChild=new Item{Name="B"};
        $hostName.Space(()=>ViewModel.CurrentFolder.SelectedChild=new Item{Name="A"});
        if(ContextMenuHost.Toggles!=1)throw new Exception("Expected exactly one preview toggle.");
        return ContextMenuHost.Opened;
    }
}
"@
    # Each generated assembly has isolated stub types; production statements remain intact.
    $model=$model.Replace('Microsoft.UI.Xaml.Input',$className+'Input').Replace('Windows.System',$className+'Windows')
    foreach($type in @('MainWindow','Item','Folder','Vm','Settings')){$model=[regex]::Replace($model,('\b'+$type+'\b'),($className+$type))}
    Add-Type -TypeDefinition $model
    $harness=New-Object $className
    if($harness.OpenB() -ne 'B'){$failures.Add("$name previewed previous A instead of selected B after native Space selection handling.")}
}
if($failures.Count){throw ($failures -join "`n")}
'PASS: Details/Icon/List preview current B before native Space can restore old focused A; one toggle only'
