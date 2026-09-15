<#
  Eklenti PNG ikonlarini ve uygulama .ico dosyasini uretir.
  Amblem: vurgu mavisi squircle uzerinde tek beyaz asagi ok.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root     = Split-Path -Parent $PSScriptRoot
$iconsDir = Join-Path $root 'extension\chrome\icons'
$appDir   = Join-Path $root 'src\Akinti'
New-Item -ItemType Directory -Force -Path $iconsDir | Out-Null

function New-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Amblem: yuvarlak kose kare (Windows 11 squircle orani) uzerinde tek bir
    # asagi ok. Ok govdesi ile ucu ayni kalinlikta, uclari yuvarlatilmis; baska
    # hicbir sey yok. Minimal olsun diye taban cizgisi ve golge kaldirildi.
    $pad  = [double]$size * 0.06
    $side = [double]$size - 2 * $pad
    $r    = $side * 0.235
    $d    = $r * 2

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($pad, $pad, $d, $d, 180, 90)
    $path.AddArc(($pad + $side - $d), $pad, $d, $d, 270, 90)
    $path.AddArc(($pad + $side - $d), ($pad + $side - $d), $d, $d, 0, 90)
    $path.AddArc($pad, ($pad + $side - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    # Zemin: vurgu mavisinin kosegen degradesi (acik ust sol -> koyu alt sag).
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, $side, $side)
    $bg   = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 76, 194, 255),
        [System.Drawing.Color]::FromArgb(255, 15, 108, 189),
        45.0)
    $g.FillPath($bg, $path)

    # Ok: tek renk beyaz, kalem ucu yuvarlak.
    $w   = [Math]::Max(1.6, $size * 0.105)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $w
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $cx   = $size / 2.0
    $top  = $size * 0.30
    $tip  = $size * 0.70
    $arm  = $size * 0.155
    $g.DrawLine($pen, $cx, $top, $cx, $tip)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(($cx - $arm), ($tip - $arm))),
        (New-Object System.Drawing.PointF($cx, $tip)),
        (New-Object System.Drawing.PointF(($cx + $arm), ($tip - $arm)))
    ))

    $pen.Dispose(); $bg.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

foreach ($s in 16, 32, 48, 128) {
    $bmp = New-Icon $s
    $out = Join-Path $iconsDir "icon$s.png"
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "yazildi: $out"
}

# --- .ico (16/32/48/64/128/256 iceren gercek ICO) ---
$sizes  = 16, 32, 48, 64, 128, 256
$blobs  = @()
foreach ($s in $sizes) {
    $bmp = New-Icon $s
    $ms  = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $blobs += , $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
}

$icoPath = Join-Path $appDir 'akinti.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$blobs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $blobs[$i].Length
}
foreach ($b in $blobs) { $bw.Write($b) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Host "yazildi: $icoPath"
