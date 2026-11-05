$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '..\src\Span\Span\Helpers\DevelopmentPackageActivation.cs') -Raw
$source = $source.Replace('namespace Span.Helpers;', 'namespace Span.Helpers {') + "`n}"
$stub = @'
namespace Windows.Management.Deployment {
 public class PackageManager { public Package[] FindPackagesForUser(string user, string name, string publisher) { return new Package[0]; } }
 public class Package { public bool IsFramework; public bool IsResourcePackage; public Identity Id = new Identity(); }
 public class Identity { public string FamilyName; }
}
'@
Add-Type -TypeDefinition ($source + $stub) -IgnoreWarnings
$type = [AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object { $_.GetType('Span.Helpers.DevelopmentPackageActivation') } | Where-Object { $_ } | Select-Object -First 1
$method = $type.GetMethod('ArgumentsAfterExecutable', [Reflection.BindingFlags]'Static,NonPublic')
$cases = @(
    @('"D:\app with spaces\SpanDev.exe"', ''),
    @('"D:\app with spaces\SpanDev.exe" "C:\"', '"C:\"'),
    @('SpanDev.exe --new-window', '--new-window'),
    @('SpanDev.exe --select "D:\test files\中文.txt"', '--select "D:\test files\中文.txt"'),
    @('  SpanDev.exe   "\\server\share\"', '"\\server\share\"'),
    @('SpanDev.exe', '')
)
foreach ($case in $cases) {
    if ($method.Invoke($null, @($case[0])) -cne $case[1]) { throw 'Activation arguments changed.' }
}
'PASS: raw activation arguments preserve drive roots, spaces, Unicode, UNC paths and new-window/select commands.'
