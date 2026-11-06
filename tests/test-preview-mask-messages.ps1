$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Views\DocumentPreviewView.Interactions.cs'))
$method = [regex]::Match($source, '(?sm)^    private bool TryReadAnnotations\(.*?^    }').Value
if (!$method) { throw 'Cannot locate production preview message validator.' }
$copy = [regex]::Match($source, '(?sm)^    private List<PreviewAnnotation> GetAnnotations\(.*?^        }\)\.ToList\(\);').Value
if (!$copy) { throw 'Cannot locate production annotation snapshot method.' }
$models = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Models\PreviewAnnotation.cs'))
$models = $models.Substring($models.IndexOf('public sealed class'))
$hostSource = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using OrielFolder.Models;
namespace OrielFolder.Models {
'@
$hostSource += $models + "`n}`n"
$hostSource += @'
public class PreviewMaskMessageHarness {
    private bool _isImage;
    private readonly Dictionary<string,List<PreviewAnnotation>> _annotationDrafts = new();
    private static readonly JsonSerializerOptions MessageJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public bool Accept(string json, bool image) {
        _isImage = image;
        using var doc = JsonDocument.Parse(json);
        var accepted = TryReadAnnotations(doc.RootElement, out var marks);
        if (accepted) _annotationDrafts["fixture"] = marks;
        return accepted;
    }
    public string Snapshot() => JsonSerializer.Serialize(GetAnnotations("fixture"), MessageJson);
'@
Add-Type -TypeDefinition ($hostSource + $method + "`n" + $copy + "`n}")
$harness = [PreviewMaskMessageHarness]::new()
foreach ($value in @(-0.1,1.1)) {
    $invalidOpacity = @{annotations=@(@{tool='pen';color='#111111';width=0.004;opacity=$value;text='';pageIndex=0;points=@(@{x=0.2;y=0.3})})} | ConvertTo-Json -Depth 8 -Compress
    if ($harness.Accept($invalidOpacity,$true)) { throw "Invalid opacity $value was accepted." }
}
foreach ($value in @(0,0.25,1)) {
    $withOpacity = @{annotations=@(@{tool='pen';color='#111111';width=0.004;opacity=$value;text='';pageIndex=0;points=@(@{x=0.2;y=0.3})})} | ConvertTo-Json -Depth 8 -Compress
    if (!$harness.Accept($withOpacity,$true)) { throw "Valid opacity $value was rejected." }
    $snapshot = $harness.Snapshot() | ConvertFrom-Json
    if ($null -eq $snapshot[0].opacity -or $snapshot[0].opacity -ne $value) { throw 'Opacity was lost during the production annotation snapshot.' }
}
foreach ($tool in @('mosaicRect','mosaicPen','redactRect','redactPen')) {
    $mark = @{ tool=$tool; color='#e53e3e'; width=0.004; text=''; pageIndex=0; points=@(@{x=0.1;y=0.2},@{x=0.8;y=0.7}) }
    $json = @{annotations=@($mark)} | ConvertTo-Json -Depth 8 -Compress
    if (!$harness.Accept($json,$true) -or !$harness.Accept($json,$false)) { throw "$tool was rejected by production validation." }
    $mark.points = @(@{x=0.1;y=0.2})
    $onePoint = @{annotations=@($mark)} | ConvertTo-Json -Depth 8 -Compress
    if ($harness.Accept($onePoint,$true) -ne $tool.EndsWith('Pen')) { throw "$tool point-count validation failed." }
    $mark.pageIndex=1
    if ($harness.Accept((@{annotations=@($mark)} | ConvertTo-Json -Depth 8 -Compress),$true)) { throw 'An image accepted a mask on a different page.' }
    $mark.pageIndex=0; $mark.points=@(@{x=-0.1;y=0.2},@{x=0.8;y=0.7})
    if ($harness.Accept((@{annotations=@($mark)} | ConvertTo-Json -Depth 8 -Compress),$false)) { throw 'Out-of-page mask coordinates were accepted.' }
}
$bad = '{"annotations":[{"tool":"unknown","color":"#111111","width":0.02,"text":"","pageIndex":0,"points":[{"x":0.1,"y":0.1}]}]}'
if ($harness.Accept($bad,$false)) { throw 'An unsupported tool was accepted.' }
'PASS: production native message validation accepts all four masking tools and rejects invalid shapes, pages and coordinates'
