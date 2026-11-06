$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Views\DetailsModeView.Columns.cs'))
$method=[regex]::Match($source,'(?sm)^    private void OnHeaderLayoutUpdated.*?(?=^    private void )').Value
$stub=@'
#nullable enable annotations
public class HeaderCostProbe {
 public bool _headerWidthsDirty=true;int reads;
 void RefreshColumnWidths()=>reads++;
 public static string Run(){var p=new HeaderCostProbe();for(int i=0;i<10000;i++)p.OnHeaderLayoutUpdated(null,null);
 if(p.reads!=1)throw new System.Exception("10000 unrelated layouts queried column widths "+p.reads+" times");
 p._headerWidthsDirty=true;p.OnHeaderLayoutUpdated(null,null);if(p.reads!=2)throw new System.Exception("Actual resize did not refresh widths");
 return "PASS: 10000 unrelated layouts query widths once; actual width changes refresh again";}
'@
Add-Type -TypeDefinition ($stub+$method+'}')
[HeaderCostProbe]::Run()
