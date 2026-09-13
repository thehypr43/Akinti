<#
  Eklenti PNG ikonlarini ve uygulama .ico dosyasini uretir.
  Gecici bir ikon seti: kullanicinin tasarimi gelince degistirilecek.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root     = Split-Path -Parent $PSScriptRoot
$iconsDir = Join-Path $root 'extension\chrome\icons'
$appDir   = Join-Path $root 'src\HafifDM'
New-Item -ItemType Directory -Force -Path $iconsDir | Out-Null

function New-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Yuvarlak kose arka plan
    $r    = [Math]::Max(2, [int]($size * 0.22))
    $rect = New-Object System.Drawing.Rectangle(0, 0, ($size - 1), ($size - 1))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d    = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc(($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
    $path.AddArc(($rect.Right - $d), ($rect.Bottom - $d), $d, $d, 0, 90)
    $path.AddArc($rect.X, ($rect.Bottom - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 28, 31, 37))
    $g.FillPath($bg, $path)

    # Asagi ok
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 76, 141, 255))
    $cx     = $size / 2.0
    $shaftW = [Math]::Max(2.0, $size * 0.14)
    $g.FillRectangle($accent, ($cx - $shaftW / 2), ($size * 0.20), $shaftW, ($size * 0.34))

    $head = New-Object 'System.Drawing.PointF[]' 3
    $head[0] = New-Object System.Drawing.PointF(($cx - $size * 0.24), ($size * 0.46))
    $head[1] = New-Object System.Drawing.PointF(($cx + $size * 0.24), ($size * 0.46))
    $head[2] = New-Object System.Drawing.PointF($cx, ($size * 0.74))
    $g.FillPolygon($accent, $head)

    # Taban cizgisi
    $g.FillRectangle($accent, ($size * 0.22), ($size * 0.80), ($size * 0.56),
                     [Math]::Max(2.0, $size * 0.09))

    $g.Dispose(); $bg.Dispose(); $accent.Dispose(); $path.Dispose()
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

$icoPath = Join-Path $appDir 'hafifdm.ico'
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
