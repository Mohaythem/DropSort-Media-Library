# Generates the TBD Movies application icon (multi-size .ico) from code, so the icon is reproducible
# and versioned as source rather than a checked-in binary of unknown origin.
#
# Design: a Windows 11 style rounded-square tile in the TBD Movies blue accent, with three white
# left-aligned rounded bars of decreasing width - a "sorted list" mark that reads as a media library
# and matches the product name (sort). Minimal, geometric, legible at 16 px, and it works on both
# light and dark shells because the mark sits on its own opaque accent tile.
#
# Output: a single .ico containing 16, 24, 32, 48, 64, 128 and 256 px frames (PNG-compressed, which
# Windows 10/11 - the app's target - render for the exe, shortcuts and Add/Remove Programs).

param(
    [string]$OutPath = "$PSScriptRoot/../src/TBDMovies.UI/Assets/TBD-Movies.ico"
)

Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded-square tile with a blue accent gradient (matches the app's accent direction).
    $pad = [Math]::Max(1, [int]($s * 0.06))
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($s - 2*$pad), ($s - 2*$pad))
    $radius = $rect.Width * 0.22
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $c1 = [System.Drawing.Color]::FromArgb(255, 59, 147, 255)   # #3B93FF
    $c2 = [System.Drawing.Color]::FromArgb(255, 10, 91, 208)    # #0A5BD0
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 55.0)
    $g.FillPath($brush, $path)

    # Three left-aligned rounded bars of decreasing width = a sorted media list.
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $barH = $rect.Height * 0.135
    $gap = $rect.Height * 0.095
    $left = $rect.X + $rect.Width * 0.22
    $totalH = 3 * $barH + 2 * $gap
    $top = $rect.Y + ($rect.Height - $totalH) / 2
    $widths = 0.56, 0.42, 0.28
    for ($i = 0; $i -lt 3; $i++) {
        $w = $rect.Width * $widths[$i]
        $y = $top + $i * ($barH + $gap)
        $r = $barH / 2
        $bp = New-Object System.Drawing.Drawing2D.GraphicsPath
        $bd = $r * 2
        $bp.AddArc($left, $y, $bd, $bd, 90, 180)
        $bp.AddArc($left + $w - $bd, $y, $bd, $bd, 270, 180)
        $bp.CloseFigure()
        $g.FillPath($white, $bp)
        $bp.Dispose()
    }

    $g.Dispose()
    return $bmp
}

# PLACEHOLDER_PACK
# Encode each frame as PNG, then assemble a standard multi-frame .ico (ICONDIR + entries + PNG blobs).
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Frame $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,($ms.ToArray())
    $ms.Dispose(); $bmp.Dispose()
}

$dir = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($OutPath))
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath $dir).Path + "\" + [System.IO.Path]::GetFileName($OutPath))
$bw = New-Object System.IO.BinaryWriter($fs)
$count = $sizes.Count
$bw.Write([UInt16]0)      # reserved
$bw.Write([UInt16]1)      # type = icon
$bw.Write([UInt16]$count) # image count

$offset = 6 + 16 * $count
for ($i = 0; $i -lt $count; $i++) {
    $s = $sizes[$i]
    $len = $pngs[$i].Length
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # width  (0 => 256)
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # height (0 => 256)
    $bw.Write([Byte]0)         # palette count
    $bw.Write([Byte]0)         # reserved
    $bw.Write([UInt16]1)       # color planes
    $bw.Write([UInt16]32)      # bits per pixel
    $bw.Write([UInt32]$len)    # bytes in resource
    $bw.Write([UInt32]$offset) # offset
    $offset += $len
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Host ("Wrote {0} ({1} frames)" -f $OutPath, $count)

