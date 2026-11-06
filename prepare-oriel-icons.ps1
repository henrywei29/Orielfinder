$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot 'src\OrielFolder\OrielFolder\Assets'
function Write-Png([Drawing.Image]$Source, [string]$Path, [int]$Width, [int]$Height) {
    $bitmap = New-Object Drawing.Bitmap $Width, $Height
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $size = [Math]::Min($Width, $Height)
        $graphics.DrawImage($Source, [int](($Width-$size)/2), [int](($Height-$size)/2), $size, $size)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
foreach ($style in @('blue', 'classic')) {
    $source = [Drawing.Image]::FromFile((Join-Path $assets "ProductIcons\$style.png"))
    try {
        $sizes = @(16,24,32,48,64,128,256)
        $payloads = @()
        foreach ($size in $sizes) {
            $bitmap = New-Object Drawing.Bitmap $size, $size
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $stream = New-Object IO.MemoryStream
            try {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($source,0,0,$size,$size)
                $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
                $payloads += ,$stream.ToArray()
            } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
        }
        $stream = [IO.File]::Create((Join-Path $assets "ProductIcons\$style.ico"))
        $writer = New-Object IO.BinaryWriter $stream
        try {
            $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
            $offset = 6 + 16 * $sizes.Count
            for ($i=0; $i -lt $sizes.Count; $i++) {
                $side = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
                $writer.Write([byte]$side); $writer.Write([byte]$side)
                $writer.Write([byte]0); $writer.Write([byte]0)
                $writer.Write([UInt16]1); $writer.Write([UInt16]32)
                $writer.Write([UInt32]$payloads[$i].Length); $writer.Write([UInt32]$offset)
                $offset += $payloads[$i].Length
            }
            foreach ($bytes in $payloads) { $writer.Write([byte[]]$bytes) }
        } finally { $writer.Dispose(); $stream.Dispose() }
        if ($style -eq 'blue') {
            Write-Png $source (Join-Path $assets 'Square44x44Logo.png') 44 44
            Write-Png $source (Join-Path $assets 'Square150x150Logo.png') 150 150
            Write-Png $source (Join-Path $assets 'Wide310x150Logo.png') 310 150
            Write-Png $source (Join-Path $assets 'SplashScreen.png') 620 300
            foreach ($name in @('SplashScreen.scale-200.png','LockScreenLogo.scale-200.png','Square150x150Logo.scale-200.png','Square44x44Logo.scale-200.png','StoreLogo.png','Wide310x150Logo.scale-200.png')) {
                $path = Join-Path $assets $name
                $old = [Drawing.Image]::FromFile($path)
                $width = $old.Width; $height = $old.Height; $old.Dispose()
                Write-Png $source $path $width $height
            }
            foreach ($size in @(24,32,48,256)) { Write-Png $source (Join-Path $assets "Square44x44Logo.targetsize-${size}_altform-unplated.png") $size $size }
            Write-Png $source (Join-Path $assets 'Onboarding\app-icon.png') 256 256
        }
    } finally { $source.Dispose() }
}
Copy-Item -LiteralPath (Join-Path $assets 'ProductIcons\blue.ico') -Destination (Join-Path $assets 'app.ico') -Force
Write-Output 'Generated both multi-resolution icons and blue package assets.'
