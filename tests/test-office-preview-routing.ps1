$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
$source = [IO.File]::ReadAllText((Join-Path $root 'Services\PreviewService.cs'))
$enum = [IO.File]::ReadAllText((Join-Path $root 'Models\PreviewType.cs'))
$method = [regex]::Match($source,'(?sm)^        public PreviewType GetPreviewType\(.*?^        }').Value
$sets = [regex]::Matches($source,'(?sm)^        private static readonly HashSet<string> .*?^        };') | ForEach-Object {$_.Value}
if (-not $method -or -not $sets) { throw 'Cannot locate actual preview classifier.' }
$hostSource = @'
#nullable enable
using System;
using System.IO;
using System.Collections.Generic;
using OrielFolder.Models;
'@
$tail = @'
namespace OrielFolder.Services {
    public class PreviewService {
        private static bool LooksLikeText(string path) => false;
'@
Add-Type -TypeDefinition ($hostSource + $enum + $tail + ($sets -join "`n") + $method + "`n} }")
$service = [OrielFolder.Services.PreviewService]::new()
foreach($extension in @('.doc','.DOCX','.xls','.XLSX','.ppt','.PPTX')) {
    $kind=$service.GetPreviewType(('D:\disposable\document'+$extension),$false).ToString()
    if($kind -ne 'Office') { throw "$extension did not route to a copyable Office document preview (got $kind)." }
}
'PASS: all six Word/Excel/PowerPoint extensions route to the document viewer'
foreach($case in @(@('.pdf','Pdf'),@('.png','Image'),@('.txt','Text'),@('.mp4','Media'))) {
    if($service.GetPreviewType(('D:\disposable\file'+$case[0]),$false).ToString() -ne $case[1]) {throw 'Existing preview classification changed.'}
}
'PASS: PDF/image/text/media routes remain compatible'
