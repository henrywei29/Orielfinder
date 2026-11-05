$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\ViewModels\FolderViewModel.cs'))
$method=[regex]::Match($source,'(?sm)^        public void RequestFolderSizeCalculation\(.*?^        }').Value
$cancel=[regex]::Match($source,'(?sm)^        public void CancelLoading\(.*?^        }').Value
$unsubscribe=[regex]::Match($source,'(?sm)^        private void UnsubscribeFolderSizeEvent\(.*?^        }').Value
$driver=@'
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using FolderViewModel=SubscriptionProbe;
namespace Helpers {public static class DebugLogger {public static void Log(string s){}} public static class UncPathHelper {public static bool IsVirtualRoot(string p)=>false;}}
namespace Microsoft.UI.Dispatching {public class DispatcherQueue {public static DispatcherQueue GetForCurrentThread()=>new();}}
public class FolderSizeService {
 public event Action<string,long> SizeCalculated; public int Subscribers=>SizeCalculated?.GetInvocationList().Length??0;
 public long? TryGetCachedSize(string p)=>null; public void RequestCalculation(string p){}
}
public class Resolver {public FolderSizeService Service=new(); public object GetService(Type t)=>Service;}
public class App {public static App Current=new();public Resolver Services=new();}
public class SubscriptionProbe {
 List<object> _allChildren=null; public List<object> Children=new();
 CancellationTokenSource _cts=new();public bool IsLoading {get;set;}
 void CancelChildCount(){}
 string _calculatedSize=null; string Path="D:/test";
 Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcher;
 public string Size {get;set;} public long SizeValue {get;set;}
 string FormatFolderSize(long v)=>v.ToString();void OnPropertyChanged(string name){}
 void OnFolderSizeCalculated(string p,long b){}
 public static string Run(){var p=new SubscriptionProbe();for(int i=0;i<100;i++)p.RequestFolderSizeCalculation();
 if(App.Current.Services.Service.Subscribers!=1) throw new Exception("Repeated visible-row requests accumulated "+App.Current.Services.Service.Subscribers+" subscriptions");
 var child=new SubscriptionProbe();p.Children.Add(child);child.RequestFolderSizeCalculation();
 p.CancelLoading();if(App.Current.Services.Service.Subscribers!=0) throw new Exception("Navigation retained old child size subscriptions");
 return "PASS: 100 real VM requests retain one subscription and navigation releases parent/child handlers";}
'@
Add-Type -TypeDefinition ($driver+$method+$cancel+$unsubscribe+"`n}") -WarningAction SilentlyContinue
[SubscriptionProbe]::Run()
