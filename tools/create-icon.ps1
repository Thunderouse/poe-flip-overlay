#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src/FlipOverlay.App/Assets'
$source = [Drawing.Image]::FromFile((Join-Path $root 'design/app-icon-source.png'))
try {
    $images = foreach ($size in @(16,24,32,48,64,128,256)) {
        $bitmap = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source,[Drawing.Rectangle]::new(0,0,$size,$size))
            $stream = [IO.MemoryStream]::new()
            $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
            if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $assets 'app.png'),$stream.ToArray()) }
            [pscustomobject]@{Size=$size; Bytes=$stream.ToArray()}
            $stream.Dispose()
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
    $file = [IO.File]::Create((Join-Path $assets 'app.ico'))
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
        $offset = 6 + 16 * $images.Count
        foreach ($entry in $images) {
            $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $entry.Bytes.Length
        }
        foreach ($entry in $images) { $writer.Write([byte[]]$entry.Bytes) }
    } finally { $writer.Dispose(); $file.Dispose() }
    Write-Output 'Created transparent PNG and ICO: 16, 24, 32, 48, 64, 128, 256 px.'
} finally { $source.Dispose() }
