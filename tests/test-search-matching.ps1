$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src\Span\Span'
$header = @'
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Span.Models;
using Span.ViewModels;
using Span.Helpers;
namespace Span.Helpers { public static class DebugLogger { public static void Log(string text) {} } }
namespace Span.Models {
    public class FolderItem { public string Name = "", Path = ""; public DateTime DateModified; }
}
namespace Span.ViewModels {
    public class FileSystemViewModel { public string Name = "", Path = ""; public long SizeValue; public DateTime DateModifiedValue = DateTime.Today; }
    public class FolderViewModel : FileSystemViewModel {}
}
'@
$sources = foreach($file in @('Models\SearchQuery.cs','Helpers\SearchQueryParser.cs','Helpers\WildcardRegex.cs','Helpers\SearchFilter.cs','Helpers\SearchNameMatcher.cs')) {
    $path = Join-Path $root $file
    if (Test-Path -LiteralPath $path) {
        $code = [IO.File]::ReadAllText($path)
        $code = [regex]::Replace($code, '(?m)^using .*;\r?\n', '')
        if ($code -match '(?m)^namespace ([\w.]+);') { $code = [regex]::Replace($code, '(?m)^namespace ([\w.]+);', 'namespace $1 {') + "`n}" }
        $code
    }
}
$handler = [IO.File]::ReadAllText((Join-Path $root 'MainWindow.FileOperationHandler.cs'))
$rootChoice = [regex]::Match($handler, '(?s)string rootPath(?:;| =).*?(?=\s*if \(string.IsNullOrEmpty\(rootPath\))').Value
if (-not $rootChoice) { throw 'Cannot locate actual search root selection.' }
$rootHost = @'
namespace Span {
    public class Explorer {
        public string CurrentPath = @"D:\parent";
        public List<FolderViewModel> Columns = new();
        public FolderViewModel? CurrentFolder => Columns.LastOrDefault(c => c.Path == CurrentPath);
    }
    public static class RootTest {
        public static string Resolve(Explorer explorer, bool isGlobalSearch) {
'@
Add-Type -TypeDefinition ($header + ($sources -join "`n") + $rootHost + $rootChoice + "`nreturn rootPath; } } }")
$failures = [Collections.Generic.List[string]]::new()
$cases = @(
    @('财务','年度财务报告.pdf',$true),
    @('年度 报告','年度财务报告.pdf',$true),
    @('project budget','budget_for_project_2026.xlsx',$true),
    @('comfyui shared','ComfyUI-Shared',$true),
    @('ｒｅｐｏｒｔ','Report.pdf',$true),
    @('repotr','report.pdf',$true),
    @('budjet','budget.pdf',$true),
    @('reort','report.pdf',$true),
    @('reeport','report.pdf',$true),
    @('ax','abc.txt',$false),
    @('报告','报表.docx',$false),
    @('invoice','landscape.png',$false),
    @('budjet ext:pdf','budget.xlsx',$false),
    @('budjet ext:pdf','budget.pdf',$true),
    @('budjet kind:image','budget.pdf',$false),
    @('*.txt','report.txt',$true),
    @('*.txt','report.txt.bak',$false),
    @('report*','my-report.pdf',$false),
    @('file?.txt','file12.txt',$false),
    @('预算 报告','项目预算-年度报告.pdf',$true),
    @('𠮷野家','野家预算.xlsx',$false),
    @('𠮷 野家','𠮷-野家.txt',$true),
    @('野𠮷家店','野家店.txt',$true)
)
foreach($case in $cases) {
    $query = [Span.Helpers.SearchQueryParser]::Parse($case[0])
    $item = [Span.ViewModels.FileSystemViewModel]::new()
    $item.Name = $case[1]
    $result = [Span.Helpers.SearchFilter]::Matches($query,$item)
    if ($result -ne $case[2]) { $failures.Add("query='$($case[0])', name='$($case[1])', expected=$($case[2]), actual=$result") }
    else { "PASS: name query '$($case[0])' against '$($case[1])'" }
}
$explorer = [Span.Explorer]::new()
foreach($path in @('D:\parent','D:\parent\retained-child')) { $column=[Span.ViewModels.FolderViewModel]::new(); $column.Path=$path; $explorer.Columns.Add($column) }
if ([Span.RootTest]::Resolve($explorer,$false) -ne 'D:\parent') { $failures.Add('Ordinary search searched the retained child instead of the current folder.') }
else { 'PASS: ordinary search respects current folder with retained child columns' }
if ([Span.RootTest]::Resolve($explorer,$true) -ne 'D:\parent') { throw 'Ctrl+Enter root search behavior changed.' }
'PASS: Ctrl+Enter still searches from the first column root'
if ($failures.Count) { throw ($failures -join "`n") }
