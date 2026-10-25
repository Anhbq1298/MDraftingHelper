<#
.SYNOPSIS
  Visual acceptance evidence: starts AutoCAD 2021 with the installed bundle, inserts test content,
  opens the Properties palette on a steel block and each MDraftingHeper dialog, and saves
  full-screen PNGs to -OutDir. Uses COM (PostCommand) and SendKeys; do not use the mouse/keyboard.
#>
param(
    [string]$AcadDir = $(if ($env:Acad2021Dir) { $env:Acad2021Dir } else { "C:\Program Files\Autodesk\AutoCAD 2021" }),
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) "MDH_Screens")
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, Microsoft.VisualBasic
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Shot([string]$name) {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $path = Join-Path $OutDir ($name + ".png")
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    $path
}

$p = Start-Process (Join-Path $AcadDir "acad.exe") -ArgumentList "/nologo /t acadiso.dwt" -PassThru
$app = $null
for ($i = 0; $i -lt 40 -and -not $app; $i++) {
    Start-Sleep -Seconds 3
    try { $app = [Runtime.InteropServices.Marshal]::GetActiveObject("AutoCAD.Application.24") } catch { }
}
if (-not $app) { $p | Stop-Process -Force; throw "AutoCAD COM object not available." }
function Wait-Quiet { for ($i = 0; $i -lt 120; $i++) { try { if ($app.GetAcadState().IsQuiescent) { return } } catch { }; Start-Sleep -Milliseconds 500 } }
function Activate { [Microsoft.VisualBasic.Interaction]::AppActivate($p.Id) | Out-Null; Start-Sleep -Milliseconds 400 }
try {
    Wait-Quiet; Start-Sleep -Seconds 4
    $app.WindowState = 3  # maximised
    $doc = $app.ActiveDocument
    $doc.SendCommand("MDH_SELFTEST`n"); Wait-Quiet
    $doc.SendCommand("MDH_SELFTEST_STEEL2`n"); Wait-Quiet
    # Show the UB instance edited to Custom (H=450 restored by STEEL2, so pick the first UB) and the ribbon tab.
    $doc.SendCommand("(command `"_.ZOOM`" `"_W`" `"2700,-400`" `"6300,700`")`n"); Wait-Quiet
    $doc.SendCommand("(sssetfirst nil (ssget `"_C`" '(4790 -10) '(4810 10) '((0 . `"INSERT`"))))`n"); Wait-Quiet
    $doc.PostCommand("_PROPERTIES`n"); Start-Sleep -Seconds 3
    Activate
    Shot "01_properties_palette_ub" | Out-Null
    $doc.SendCommand("(sssetfirst nil nil)`n"); Wait-Quiet

    # Steel dialog
    $doc.PostCommand("MDH_STEEL`n"); Start-Sleep -Seconds 4; Activate
    Shot "02_steel_dialog" | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}"); Start-Sleep -Seconds 2; Wait-Quiet

    # Tie dialog: two coplanar bars, picked on their circumference.
    $doc.SendCommand("(command `"_.CIRCLE`" `"0,-2000`" `"10`" `"_.CIRCLE`" `"300,-2000`" `"12.5`" `"_.ZOOM`" `"_W`" `"-200,-2300`" `"500,-1700`")`n"); Wait-Quiet
    $doc.PostCommand("(command `"MDH_TIE`" `"10,-2000`" `"312.5,-2000`")`n"); Start-Sleep -Seconds 5; Activate
    Shot "03_tie_dialog" | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}"); Start-Sleep -Seconds 2; Wait-Quiet

    # Rebar dialog: self-test section at 0..400 x 0..600 (window-select circles, pick the bottom edge).
    $doc.SendCommand("(command `"_.ZOOM`" `"_W`" `"-300,-100`" `"700,700`")`n"); Wait-Quiet
    $doc.PostCommand("(command `"MDH_REBAR`" `"_C`" `"-150,-50`" `"450,650`" `"`" `"0,150`")`n"); Start-Sleep -Seconds 5; Activate
    Shot "04_rebar_dialog" | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}"); Start-Sleep -Seconds 2; Wait-Quiet
}
finally {
    try { foreach ($d in @($app.Documents)) { $d.Close($false) } } catch { }
    try { $app.Quit() } catch { }
    if (-not $p.WaitForExit(30000)) { $p | Stop-Process -Force }
}
Get-ChildItem $OutDir -Filter *.png | Select-Object FullName
