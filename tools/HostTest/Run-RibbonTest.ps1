<#
.SYNOPSIS
  Checks the Ribbon lifecycle of the INSTALLED bundle in a normally started AutoCAD 2021
  (no batch script, so the Idle-driven startup path runs as it does for users):
  tab created once, still once after a workspace switch and with a second drawing open.
  Drives AutoCAD through COM (AutoCAD.Application.24). Do not type into the window.
#>
param(
    [string]$AcadDir = $(if ($env:Acad2021Dir) { $env:Acad2021Dir } else { "C:\Program Files\Autodesk\AutoCAD 2021" }),
    [int]$StartupWaitSec = 90
)
$ErrorActionPreference = "Stop"
$log = Join-Path $env:LOCALAPPDATA "MDraftingHeper\logs\mdh.log"
$before = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }

$p = Start-Process (Join-Path $AcadDir "acad.exe") -ArgumentList "/nologo /t acadiso.dwt" -PassThru
$app = $null
$deadline = (Get-Date).AddSeconds($StartupWaitSec)
while (-not $app -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    try { $app = [Runtime.InteropServices.Marshal]::GetActiveObject("AutoCAD.Application.24") } catch { }
}
if (-not $app) { $p | Stop-Process -Force; throw "AutoCAD COM object not available." }

function Wait-Ready {
    for ($i = 0; $i -lt 240; $i++) {
        try { if ($app.GetAcadState().IsQuiescent) { return } } catch { }
        Start-Sleep -Milliseconds 500
    }
    throw "AutoCAD stayed busy while waiting for a quiescent command state."
}
function Send([string]$cmd) {
    Wait-Ready
    for ($i = 0; $i -lt 40; $i++) {
        try { $app.ActiveDocument.SendCommand($cmd + "`n"); return } catch { Start-Sleep -Milliseconds 500 }
    }
    throw "AutoCAD stayed busy; could not send '$cmd'."
}
try {
    # AutoCAD rejects COM calls (RPC_E_CALL_REJECTED) while it is still starting up.
    $ready = $false
    for ($i = 0; $i -lt 240 -and -not $ready; $i++) {
        try { $ready = [bool]$app.GetAcadState().IsQuiescent } catch { }
        if (-not $ready) { Start-Sleep -Milliseconds 500 }
    }
    Start-Sleep -Seconds 5                     # let Idle fire after startup
    Send "MDH_SELFTEST_RIBBON"
    $ws = $app.ActiveDocument.GetVariable("WSCURRENT")
    $other = if ($ws -ne "3D Basics") { "3D Basics" } else { "Drafting & Annotation" }
    Send "(setvar `"WSCURRENT`" `"$other`")"
    Start-Sleep -Seconds 4
    Send "(setvar `"WSCURRENT`" `"$ws`")"
    Start-Sleep -Seconds 4
    Send "MDH_SELFTEST_RIBBON"
    $null = $app.Documents.Add("acadiso.dwt")
    Start-Sleep -Seconds 4
    Send "MDH_SELFTEST_RIBBON"
    Start-Sleep -Seconds 2
}
finally {
    try { foreach ($d in @($app.Documents)) { $d.Close($false) } } catch { }
    try { $app.Quit() } catch { }
    if (-not $p.WaitForExit(30000)) { $p | Stop-Process -Force }
}
$lines = (Get-Content $log | Select-Object -Skip $before) | Select-String "MDH-SELFTEST" | ForEach-Object { $_.Line }
$lines
if (($lines | Select-String "FAIL").Count -gt 0 -or ($lines | Select-String "PASS").Count -lt 3) { exit 1 }
