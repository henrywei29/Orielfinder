$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Services\ShellContextMenu.cs'))
$struct = [regex]::Match($source, '(?sm)^        \[StructLayout\(LayoutKind.Sequential\)\]\r?\n        private struct CMINVOKECOMMANDINFO\r?\n.*?^        }').Value
$constants = [regex]::Matches($source, '(?m)^        private const (?:uint|int) (?:CMIC_MASK_\w+|FIRST_CMD|SW_SHOWNORMAL) = .*?;') | ForEach-Object Value
$initializers = [regex]::Matches($source, '(?s)var invokeInfo = new CMINVOKECOMMANDINFO\s*\{.*?\};')
if ($initializers.Count -ne 2 -or -not $struct) { throw 'FAIL: full-menu/session native command invocation structures were not found.' }
$builders = for ($index = 0; $index -lt $initializers.Count; $index++) {
    'private static CMINVOKECOMMANDINFO Build'+$index+'(){int cmd=1,commandId=1;IntPtr hwnd=(IntPtr)7;'+$initializers[$index].Value+'return invokeInfo;}'
}
$header = @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
#pragma warning disable CS0219
public static class ShellCommandLifetimeTests {
 private static IntPtr _hwnd=(IntPtr)7;
'@
$driver = @'
 public static async Task<string> Run(){
  foreach(var info in new[]{Build0(),Build1()}){
   using var closeDialog=new ManualResetEventSlim();
   var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   // Model the documented shell behavior: without NOASYNC, InvokeCommand can return
   // while a property sheet still depends on the caller's HWND and STA.
   var invocation=Task.Run(()=>{entered.SetResult();if((info.fMask&0x100)!=0)closeDialog.Wait();});
   try {
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await Task.Delay(30);
    if(invocation.IsCompleted)throw new Exception("Native command permits return before its property dialog closes, so owner HWND/STA can be destroyed early.");
   }finally{closeDialog.Set();}
   await invocation.WaitAsync(TimeSpan.FromSeconds(2));
  }
  return "PASS: both native command paths request completion before their owner HWND/STA can be released (documented NOASYNC shell simulation)";
 }
}
'@
Add-Type -TypeDefinition ($header+$struct+($constants -join "`n")+($builders -join "`n")+$driver)
[ShellCommandLifetimeTests]::Run().GetAwaiter().GetResult()
