$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\ViewModels\FolderViewModel.cs'))
$method=[regex]::Match($source,'(?sm)^        private async Task ReadChildCountAsync\(.*?^        }').Value
$driver=@'
using System;
using System.Threading;
using System.Threading.Tasks;
namespace Microsoft.UI.Dispatching {public class DispatcherQueue {public void TryEnqueue(Action a)=>a();}}
namespace Helpers {public static class DebugLogger {public static void Log(string s){}}}
public static class FolderChildCountService {
 public record Snapshot(int Count);public static int Count;
 public static Task<Snapshot> ReadAsync(string p,bool h,Snapshot old,CancellationToken t)=>Task.FromResult(new Snapshot(Count));
}
public class CountProbe {
 public class Model {public bool HasChildEntries=true;}
 Model _folderModel=new();public bool _isLoaded {get;set;}
 FolderChildCountService.Snapshot _childCount;
 CancellationTokenSource _childCountCts=new();string Path="D:/disposable";
 public bool HasChildren=>_folderModel.HasChildEntries;public string ChildCountText=>_childCount?.Count.ToString();
 void OnPropertyChanged(string s){}
 public static async Task<string> Run(){
  var p=new CountProbe();FolderChildCountService.Count=0;
  await p.ReadChildCountAsync(p.Path,false,p._childCountCts,new());
  if(p.HasChildren || p.ChildCountText!="0")throw new Exception("Visible empty-folder count must update its expansion arrow without probing every child directory during parent load");
  return "PASS: actual asynchronous visible-row count updates empty-folder expansion state";
 }
'@
Add-Type -TypeDefinition ($driver+$method+"`n}")
[CountProbe]::Run().GetAwaiter().GetResult()
