$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\MainWindow.PaneSizing.cs'))
$property=[regex]::Match($source,'(?sm)^        private bool IsClassicSplitVisible =>.*?;').Value
$stub=@'
public enum ViewMode {Home,Settings,ActionLog,RecycleBin}
public class Model {public bool IsSplitViewEnabled;public ViewMode CurrentViewMode;}
public class StartupProbe {public Model ViewModel;
public static string Run(){var p=new StartupProbe();if(p.IsClassicSplitVisible)throw new System.Exception("Uninitialized split visible");return "PASS: early layout before ViewModel initialization is safe";}
'@
Add-Type -TypeDefinition ($stub+$property+'}')
[StartupProbe]::Run()
