# Draws the Bananarama banana as vector shapes and writes multi-size .ico files
# (32-bit BMP frames for 16-48 px, PNG frame for 256 px) plus a PNG for the web page.
#   tray\banana.ico      normal (middleware running)
#   tray\banana-off.ico  greyed (middleware stopped / starting)
#   docs\banana.png      256 px
param([string] $Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-Banana([int] $size, [bool] $off) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $g.Clear([System.Drawing.Color]::Transparent)
  $k = $size / 256.0
  function P([double] $x, [double] $y) { New-Object System.Drawing.PointF ([float]($x * $k)), ([float]($y * $k)) }

  if ($off) { $light = '#D9D9D9'; $dark = '#A6A6A6'; $line = '#5C5C5C'; $stem = '#6E6E6E' }
  else      { $light = '#FFE45C'; $dark = '#F2B705'; $line = '#5A4100'; $stem = '#6B4A1F' }
  $c = { param($h) [System.Drawing.ColorTranslator]::FromHtml($h) }

  # crescent body: outer belly curve, then the inner curve back to the stem end
  $body = New-Object System.Drawing.Drawing2D.GraphicsPath
  $body.AddBezier((P 58 44), (P 6 150), (P 92 246), (P 236 196))
  $body.AddBezier((P 236 196), (P 196 176), (P 116 196), (P 96 56))
  $body.CloseFigure()

  $stroke = [Math]::Max(1.25, $size * 0.055)
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 40 60), (P 200 230), (& $c $light), (& $c $dark)
  $g.FillPath($brush, $body)

  # highlight streak along the belly (skip at tiny sizes)
  if ($size -ge 32) {
    $hl = New-Object System.Drawing.Drawing2D.GraphicsPath
    $hl.AddBezier((P 62 92), (P 46 160), (P 96 208), (P 170 214))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150, 255, 255, 255)), ([float]($size * 0.04))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawPath($pen, $hl)
  }

  $outline = New-Object System.Drawing.Pen (& $c $line), ([float]$stroke)
  $outline.LineJoin = 'Round'
  $g.DrawPath($outline, $body)

  # stem and tip
  $stemPen = New-Object System.Drawing.Pen (& $c $stem), ([float]([Math]::Max(2, $size * 0.11)))
  $stemPen.StartCap = 'Round'; $stemPen.EndCap = 'Flat'
  $g.DrawLine($stemPen, (P 64 14), (P 76 52))
  $tip = New-Object System.Drawing.SolidBrush (& $c $stem)
  $r = [Math]::Max(1.5, $size * 0.035)
  $t = P 233 196
  $g.FillEllipse($tip, [float]($t.X - $r), [float]($t.Y - $r), [float](2 * $r), [float](2 * $r))
  $g.Dispose()
  return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap] $b) {
  $w = $b.Width; $h = $b.Height
  $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $ms
  $maskRow = [int]([Math]::Ceiling($w / 32.0) * 4)
  $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
  $bw.Write([int]0); $bw.Write([int]($w * $h * 4 + $maskRow * $h)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
  for ($y = $h - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $w; $x++) {
    $p = $b.GetPixel($x, $y); $bw.Write([byte]$p.B); $bw.Write([byte]$p.G); $bw.Write([byte]$p.R); $bw.Write([byte]$p.A) } }
  for ($y = $h - 1; $y -ge 0; $y--) {
    $row = New-Object byte[] $maskRow
    for ($x = 0; $x -lt $w; $x++) { if ($b.GetPixel($x, $y).A -eq 0) { $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) } }
    $bw.Write($row)
  }
  $bw.Flush(); return $ms.ToArray()
}

function Write-Ico([string] $path, [bool] $off) {
  $sizes = 16, 20, 24, 32, 40, 48, 64, 256
  $frames = foreach ($s in $sizes) {
    $b = New-Banana $s $off
    if ($s -ge 256) { $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); ,$ms.ToArray() }
    else { ,(Get-DibBytes $b) }
    $b.Dispose()
  }
  $fs = [System.IO.File]::Create($path); $bw = New-Object System.IO.BinaryWriter $fs
  $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = [byte]($(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write($d); $bw.Write($d); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]$frames[$i].Length); $bw.Write([int]$offset); $offset += $frames[$i].Length
  }
  foreach ($f in $frames) { $bw.Write([byte[]]$f) }
  $bw.Close()
}

Write-Ico (Join-Path $Root 'tray\banana.ico') $false
Write-Ico (Join-Path $Root 'tray\banana-off.ico') $true
$big = New-Banana 256 $false; $big.Save((Join-Path $Root 'docs\banana.png'), [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
# preview sheet for checking small sizes by eye
$sheet = New-Object System.Drawing.Bitmap 400, 140
$g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::FromArgb(32, 32, 32))
$x = 8; foreach ($s in 16, 24, 32, 48, 64) { $b = New-Banana $s $false; $g.DrawImage($b, $x, 8, $s, $s); $o = New-Banana $s $true; $g.DrawImage($o, $x, 76, $s, $s); $x += $s + 12 }
$g.DrawImage((New-Banana 128 $false), 268, 6, 128, 128); $g.Dispose()
$sheet.Save((Join-Path $env:TEMP 'bananarama-icon-sheet.png'))
Write-Host "icons written; preview: $env:TEMP\bananarama-icon-sheet.png"
