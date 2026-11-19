param(
  [Parameter(Mandatory)] [string] $Source,
  [Parameter(Mandatory)] [string] $Dest,
  [int] $MaxSide = 1024,
  [long] $Quality = 85
)
# Downscale an image and save it as JPEG (used for chat previews of big PNGs).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($Source)
try {
  $scale = [Math]::Min(1.0, $MaxSide / [double][Math]::Max($img.Width, $img.Height))
  $w = [int][Math]::Max(1, [Math]::Round($img.Width * $scale))
  $h = [int][Math]::Max(1, [Math]::Round($img.Height * $scale))
  $bmp = New-Object System.Drawing.Bitmap -ArgumentList $w, $h
  try {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($img, 0, 0, $w, $h)
    $g.Dispose()
    $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
    $params = New-Object System.Drawing.Imaging.EncoderParameters -ArgumentList 1
    $params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter -ArgumentList ([System.Drawing.Imaging.Encoder]::Quality), $Quality
    $bmp.Save($Dest, $codec, $params)
  } finally { $bmp.Dispose() }
} finally { $img.Dispose() }
