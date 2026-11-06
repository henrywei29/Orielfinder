$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Views\DetailsModeView.xaml.cs') -Raw
$methods = @('SaveSortSettings','RestoreSortSettings') | ForEach-Object {
    $m = [regex]::Match($source, "(?sm)^        private void $_\(\).*?^        }").Value
    if (!$m) { throw "Missing production method $_" }
    $m
}
$fixture = @'
using System;
using System.Collections.Generic;
public class ApplicationDataCompositeValue : Dictionary<string,object> {}
public class Values : Dictionary<string,object> { public new object this[string key] {get=>TryGetValue(key,out var v)?v:null;set=>base[key]=value;} }
public class Settings { public Values Values=new(); }
namespace Helpers {public static class DebugLogger {public static void Log(string s) {if(s.Contains("Error"))throw new Exception(s);}}}
public class GroupingProbe {
 Settings _localSettings=new(); string _currentGroupBy="None",_currentSortBy="Name"; bool _isAscending=true;
 bool _dateColumnVisible=true,_typeColumnVisible=true,_sizeColumnVisible=true,_gitColumnVisible=false;
 int SortCalls; void SortItems(string column,bool ascending){SortCalls++;}
 public static void Run(){
  var p=new GroupingProbe();
  p._localSettings.Values["DetailsViewSort"]=new ApplicationDataCompositeValue {
   ["SortColumn"]="DateModified",["SortAscending"]=false,["GroupBy"]="DateModified",["ColumnOrder"]="Name,Size,Date"
  };
  p.RestoreSortSettings();
  if(p._currentGroupBy!="None")throw new Exception("Legacy automatic group restored");
  if(p._currentSortBy!="DateModified" || p._isAscending || p.SortCalls!=1)throw new Exception("Existing sorting lost");
  p._currentGroupBy="DateModified";p.SaveSortSettings();
  var saved=(ApplicationDataCompositeValue)p._localSettings.Values["DetailsViewSort"];
  if((string)saved["ExplicitGroupBy"]!="DateModified" || (string)saved["ColumnOrder"]!="Name,Size,Date")throw new Exception("Manual preference or column layout lost");
  var reopened=new GroupingProbe{_localSettings=p._localSettings};reopened.RestoreSortSettings();
  if(reopened._currentGroupBy!="DateModified")throw new Exception("Explicit date choice not remembered");
  reopened._currentGroupBy="None";reopened.SaveSortSettings();p.RestoreSortSettings();
  if(p._currentGroupBy!="None")throw new Exception("None not remembered");
  var fresh=new GroupingProbe();fresh.RestoreSortSettings();
  if(fresh._currentGroupBy!="None" || !fresh._isAscending)throw new Exception("New user default changed");
 }
__METHODS__
}
'@
Add-Type -TypeDefinition $fixture.Replace('__METHODS__',($methods -join "`n")) -WarningAction SilentlyContinue
[GroupingProbe]::Run()
'PASS: legacy automatic grouping ignored, sorting/order preserved, explicit grouping and None remembered, new user flat default.'
