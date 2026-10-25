<#
.SYNOPSIS
  Runs the in-AutoCAD self-tests against the INSTALLED bundle (deploy\Install-MDraftingHeper.ps1).
  Session 1: MDH_SELFTEST, undo/redo checks across commands, save.
  Session 2: reopen the saved drawing and check blocks + metadata.
  AutoCAD windows open while this runs; do not type into them.
#>
param(
    [string]$AcadDir = $(if ($env:Acad2021Dir) { $env:Acad2021Dir } else { "C:\Program Files\Autodesk\AutoCAD 2021" }),
    [int]$TimeoutSec = 300
)
$ErrorActionPreference = "Stop"
$acad = Join-Path $AcadDir "acad.exe"
$work = Join-Path ([IO.Path]::GetTempPath()) ("MDH_HostTest_" + (Get-Date -Format yyyyMMdd_HHmmss))
New-Item -ItemType Directory -Force $work | Out-Null
$dwg = Join-Path $work "selftest.dwg"
function L([string]$p) { $p -replace '\\', '/' }

function Run([string]$name, [string]$body, [string]$drawing) {
    $logDir = Join-Path $work ("log_" + $name); New-Item -ItemType Directory -Force $logDir | Out-Null
    $scr = Join-Path $work ($name + ".scr")
    # LOGFILEMODE/LOGFILEPATH are persistent user settings: restore them before AutoCAD quits.
    $restore = "(setvar `"LOGFILEMODE`" mdh:oldlogmode)(setvar `"LOGFILEPATH`" mdh:oldlogpath)`r`n"
    $body = [regex]::Replace($body, "(?m)^QUIT", { param($m) $restore + "QUIT" })
    Set-Content $scr -Encoding ascii -Value ("FILEDIA 0`r`nCMDECHO 1`r`n(setq mdh:oldlogpath (getvar `"LOGFILEPATH`") mdh:oldlogmode (getvar `"LOGFILEMODE`"))`r`n(setvar `"LOGFILEPATH`" `"$(L $logDir)/`")`r`nLOGFILEON`r`n" + $body)
    $a = if ($drawing) { "`"$drawing`" /nologo /b `"$scr`"" } else { "/nologo /t acadiso.dwt /b `"$scr`"" }
    $p = Start-Process $acad -ArgumentList $a -PassThru
    if (-not $p.WaitForExit($TimeoutSec * 1000)) { $p | Stop-Process -Force; Write-Warning "$name timed out" }
    Get-ChildItem $logDir -Filter *.log | ForEach-Object { Get-Content $_.FullName }
}

# Each line is a separate command: dynamic block constraints evaluate when a command ends.
# REDO must directly follow U, so the redo check runs after a fresh edit + U + REDO.
$s1 = @"
MDH_SELFTEST
MDH_SELFTEST_STEEL2
MDH_SELFTEST_STEEL3
MDH_SELFTEST_UNDO1
MDH_SELFTEST_UNDO2
MDH_SELFTEST_UNDOCHECK 155
_U
MDH_SELFTEST_UNDOCHECK 100
MDH_SELFTEST_UNDO2
_U
_REDO
MDH_SELFTEST_UNDOCHECK 155
SAVEAS 2018 "$(L $dwg)"
QUIT
"@
$log1 = Run "session1" $s1 $null
# Session 2: reopen. (The Idle-driven Ribbon startup path cannot run inside a /b script; it is checked by Run-RibbonTest.ps1.)
$log2 = if (Test-Path $dwg) { Run "session2" "MDH_SELFTEST_REOPEN`r`nQUIT Y`r`n" $dwg } else { @("MDH-SELFTEST FAIL session1 did not save $dwg") }

$all = @($log1) + @($log2)
Set-Content (Join-Path $work "hosttest.log") $all
$lines = $all | Select-String "MDH-SELFTEST|MDraftingHeper|error" | ForEach-Object { $_.Line }
$lines
$fails = ($lines | Select-String "MDH-SELFTEST FAIL").Count
$passes = ($lines | Select-String "MDH-SELFTEST PASS").Count
Write-Host "`nHost tests: $passes passed, $fails failed. Full log: $(Join-Path $work 'hosttest.log')"
if ($fails -gt 0 -or $passes -eq 0) { exit 1 }
