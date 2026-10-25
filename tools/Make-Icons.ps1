<#
.SYNOPSIS
  Regenerates the 16/32 px Ribbon icons in assets\Icons (rebar, tie, steel) with System.Drawing.
#>
param([string]$OutDir = (Join-Path $PSScriptRoot "..\assets\Icons"))
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

$blue  = [System.Drawing.Color]::FromArgb(255, 33, 113, 181)
$dark  = [System.Drawing.Color]::FromArgb(255, 60, 60, 60)
$amber = [System.Drawing.Color]::FromArgb(255, 222, 142, 20)

function New-Icon([string]$name, [int]$size, [scriptblock]$draw) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 32.0, $size / 32.0)
    & $draw $g
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir ("{0}_{1}.png" -f $name, $size)), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$rebar = {
    param($g)
    $pen = New-Object System.Drawing.Pen $dark, 2.2
    $g.DrawRectangle($pen, 4, 4, 24, 24)
    $b = New-Object System.Drawing.SolidBrush $blue
    foreach ($p in @(@(9,9), @(19,9), @(9,19), @(19,19), @(14,9), @(14,19))) { $g.FillEllipse($b, $p[0], $p[1], 5, 5) }
}
$tie = {
    param($g)
    $b = New-Object System.Drawing.SolidBrush $dark
    $g.FillEllipse($b, 5, 13, 7, 7); $g.FillEllipse($b, 20, 13, 7, 7)
    $pen = New-Object System.Drawing.Pen $amber, 2.4
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, 2, 10, 13, 13, 90, 180)     # hook around A
    $g.DrawLine($pen, 8.5, 10, 23.5, 10)          # tie
    $g.DrawArc($pen, 17, 10, 13, 13, 270, 180)   # hook around B
    $g.DrawLine($pen, 23.5, 23, 18, 28)          # tail
    $g.DrawLine($pen, 8.5, 23, 14, 28)
}
$steel = {
    param($g)
    $b = New-Object System.Drawing.SolidBrush $blue
    $g.FillRectangle($b, 5, 4, 22, 5)
    $g.FillRectangle($b, 5, 23, 22, 5)
    $g.FillRectangle($b, 13.5, 8, 5, 16)
}
foreach ($s in 16, 32) { New-Icon "rebar" $s $rebar; New-Icon "tie" $s $tie; New-Icon "steel" $s $steel }
Get-ChildItem $OutDir -Filter *.png | Select-Object Name, Length
