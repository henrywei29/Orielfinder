param([string]$WorkerPath = (Join-Path $PSScriptRoot '..\..\src\OrielFolder\OrielFolder.DocumentPreview.Worker\bin\x64\Debug\net48\OrielFolder.DocumentPreview.Worker.exe'))
$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path 'D:\codex\Orielfolder-development\document-preview-office-tests' ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DocumentPreviewFixtureWindow {
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
'@

function New-OwnedOfficeFixture([string]$Name, [string]$ProgId) {
    $previous = @(Get-Process -Name $Name -ErrorAction SilentlyContinue | ForEach-Object Id)
    if ($Name -eq 'POWERPNT' -and $previous.Count) { throw 'PowerPoint already running; fixture test skipped to preserve the user instance.' }
    $app = $null; $document = $null; $ownedProcess = $null; $owned = $false
    try {
        $started = [DateTime]::UtcNow
        $app = New-Object -ComObject $ProgId
        $officeProcessId = [uint32]0
        [DocumentPreviewFixtureWindow]::GetWindowThreadProcessId([IntPtr]$app.Hwnd, [ref]$officeProcessId) | Out-Null
        if (!$officeProcessId -or $previous -contains [int]$officeProcessId) { throw 'Fixture COM instance was not isolated.' }
        $ownedProcess = Get-Process -Id $officeProcessId
        if ($ownedProcess.StartTime.ToUniversalTime() -lt $started.AddSeconds(-1)) { throw 'Fixture instance ownership could not be proven.' }
        $owned = $true
        $app.AutomationSecurity = 3
        if ($Name -eq 'WINWORD') {
            $app.Visible = $false; $app.DisplayAlerts = 0
            $document = $app.Documents.Add()
            $document.Content.Text = "SPAN preview synthetic Word`r`nSelectable text 123`r`nSecond paragraph"
            $document.SaveAs2((Join-Path $fixtureRoot 'word.docx'), 16)
            $document.SaveAs2((Join-Path $fixtureRoot 'word.doc'), 0)
        } elseif ($Name -eq 'EXCEL') {
            $app.Visible = $false; $app.DisplayAlerts = $false
            $document = $app.Workbooks.Add()
            $sheet = $document.Worksheets.Item(1)
            $sheet.Cells.Item(1,1).Value2 = 'SPAN preview synthetic Excel'
            $sheet.Cells.Item(2,1).Value2 = 'Selectable text'
            $sheet.Cells.Item(2,2).Value2 = 123
            $document.SaveAs((Join-Path $fixtureRoot 'excel.xlsx'), 51)
            $document.SaveAs((Join-Path $fixtureRoot 'excel.xls'), 56)
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($sheet) | Out-Null
        } else {
            $app.DisplayAlerts = 1
            $document = $app.Presentations.Add(0)
            $slide = $document.Slides.Add(1,12)
            $shape = $slide.Shapes.AddTextbox(1, 50, 60, 500, 200)
            $shape.TextFrame.TextRange.Text = 'SPAN preview synthetic PowerPoint selectable text 123'
            $document.SaveAs((Join-Path $fixtureRoot 'powerpoint.pptx'), 24)
            $document.SaveAs((Join-Path $fixtureRoot 'powerpoint.ppt'), 1)
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shape) | Out-Null
            [Runtime.InteropServices.Marshal]::FinalReleaseComObject($slide) | Out-Null
        }
    } finally {
        if ($owned) {
            try { if ($document) { if ($Name -eq 'POWERPNT') { $document.Close() } else { $document.Close($false) } } } catch { }
            try { if ($app) { if ($Name -eq 'WINWORD') { $app.Quit(0) } else { $app.Quit() } } } catch { }
        }
        if ($document -and [Runtime.InteropServices.Marshal]::IsComObject($document)) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($document) | Out-Null }
        if ($app -and [Runtime.InteropServices.Marshal]::IsComObject($app)) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($app) | Out-Null }
        if ($owned -and $ownedProcess) {
            if (!$ownedProcess.WaitForExit(5000)) { $ownedProcess.Kill() }
            $ownedProcess.Dispose()
        }
    }
}

New-OwnedOfficeFixture WINWORD Word.Application
New-OwnedOfficeFixture EXCEL Excel.Application
New-OwnedOfficeFixture POWERPNT PowerPoint.Application
foreach ($source in Get-ChildItem -LiteralPath $fixtureRoot -File) {
    $cacheFolder = Join-Path $fixtureRoot ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $cacheFolder -Force | Out-Null
    $staged = Join-Path $cacheFolder ('source' + $source.Extension)
    Copy-Item -LiteralPath $source.FullName -Destination $staged
    $originalBytes = [IO.File]::ReadAllBytes($source.FullName)
    $originalTime = $source.LastWriteTimeUtc
    $output = Join-Path $cacheFolder 'preview.pdf'
    $marker = Join-Path $cacheFolder 'office-owner.json'
    $startInfo = [Diagnostics.ProcessStartInfo]::new((Resolve-Path $WorkerPath).Path)
    $startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true
    $startInfo.ArgumentList.Add($staged); $startInfo.ArgumentList.Add($output); $startInfo.ArgumentList.Add($marker)
    $worker = [Diagnostics.Process]::Start($startInfo)
    try {
        if (!$worker.WaitForExit(45000)) { $worker.Kill(); throw ('Conversion timed out: ' + $source.Name) }
        if ($worker.ExitCode -ne 0) { throw ('Conversion failed: ' + $source.Name + ', code ' + $worker.ExitCode) }
        $pdf = [IO.File]::ReadAllBytes($output)
        if ([Text.Encoding]::ASCII.GetString($pdf,0,5) -ne '%PDF-') { throw 'Invalid conversion output.' }
        if (![Linq.Enumerable]::SequenceEqual[byte]($originalBytes,[IO.File]::ReadAllBytes($source.FullName))) { throw 'Source file changed.' }
        if ((Get-Item $source.FullName).LastWriteTimeUtc -ne $originalTime) { throw 'Source timestamp changed.' }
        if (Test-Path $marker) {
            $owner = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
            $remaining = Get-Process -Id $owner.Pid -ErrorAction SilentlyContinue
            if ($remaining -and $remaining.StartTime.ToUniversalTime().Ticks -eq $owner.StartTicks) { throw 'Owned Office process did not exit.' }
        }
        Write-Output ('PASS ' + $source.Name + ': PDF bytes=' + $pdf.Length + '; original unchanged; owned Office exited')
    } finally { $worker.Dispose() }
}
Write-Output ('Synthetic fixtures retained: ' + $fixtureRoot)
