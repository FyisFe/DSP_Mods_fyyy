# Rebuild the geometric package icon; no runtime dependency on drawing libraries.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object Drawing.Bitmap 256, 256
$g = [Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([Drawing.Color]::FromArgb(12, 25, 36))
$teal = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(63, 220, 203)), 4
$subtle = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(31, 66, 82)), 1
$white = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(226, 244, 245)), 7
$orange = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 174, 86))
$fill = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(24, 48, 61))
try {
    foreach ($x in @(32, 64, 96, 128, 160, 192, 224)) { $g.DrawLine($subtle, $x, 16, $x, 240); $g.DrawLine($subtle, 16, $x, 240, $x) }
    $g.DrawEllipse($teal, 27, 143, 202, 74)
    $g.FillRectangle($fill, 60, 66, 136, 104)
    $g.DrawRectangle($white, 60, 66, 136, 104)
    $g.DrawLine($white, 81, 66, 94, 48)
    $g.DrawLine($white, 94, 48, 139, 48)
    $g.DrawLine($white, 139, 48, 150, 66)
    $g.DrawEllipse($white, 95, 88, 67, 67)
    $g.DrawEllipse($teal, 108, 101, 41, 41)
    $g.FillEllipse($orange, 170, 81, 10, 10)
    $g.FillEllipse($orange, 28, 169, 13, 13)
    $g.FillEllipse($orange, 188, 197, 13, 13)
    $g.DrawLine($teal, 225, 175, 211, 161)
    $g.DrawLine($teal, 225, 175, 211, 184)
    $bitmap.Save((Join-Path $PSScriptRoot '../package/icon.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally { $orange.Dispose(); $fill.Dispose(); $white.Dispose(); $teal.Dispose(); $subtle.Dispose(); $g.Dispose(); $bitmap.Dispose() }
