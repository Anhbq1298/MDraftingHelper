<#
.SYNOPSIS
  Runs the Quick Draw Rectangular native writer checks against the installed AutoCAD 2021 bundle.
  Build and install the bundle first; this script verifies that the installed DLLs match the bundle.
#>
param(
    [string]$AcadDir = $(if ($env:Acad2021Dir) { $env:Acad2021Dir } else { "C:\Program Files\Autodesk\AutoCAD 2021" }),
    [int]$TimeoutSec = 180
)
$ErrorActionPreference = "Stop"
$running = Get-Process acad -ErrorAction SilentlyContinue
if ($running) { throw "Close every AutoCAD process before running this host test (running PID(s): $($running.Id -join ', '))." }
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$bundleWindows = Join-Path $root "deploy\MDraftingHeper.bundle\Contents\Windows"
$installedWindows = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\MDraftingHeper.bundle\Contents\Windows"
foreach ($name in "MDraftingHeper.AutoCAD2021.dll", "MDraftingHeper.Core.dll", "MDraftingHeper.UI.dll") {
    $source = Join-Path $bundleWindows $name
    $installed = Join-Path $installedWindows $name
    if (-not (Test-Path -LiteralPath $installed)) { throw "Installed bundle is missing $name. Run deploy\Install-MDraftingHeper.ps1 first." }
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $installed).Hash) {
        throw "Installed $name does not match the built bundle. Reinstall before testing."
    }
}
$work = Join-Path ([IO.Path]::GetTempPath()) ("MDH_RectHost_" + (Get-Date -Format yyyyMMdd_HHmmss))
$logs = Join-Path $work "logs"
$null = New-Item -ItemType Directory -Force $logs
$scr = Join-Path $work "rectangular.scr"
$dwg = Join-Path $work "rectangular-result.dwg"
function L([string]$path) { $path -replace '\\', '/' }
@"
FILEDIA 0
CMDECHO 1
(setq mdh:oldlogpath (getvar "LOGFILEPATH") mdh:oldlogmode (getvar "LOGFILEMODE"))
(setvar "LOGFILEPATH" "$(L $logs)/")
LOGFILEON
MDH_SELFTEST_RECT
_SAVEAS
2018
$(L $dwg)
(setvar "LOGFILEMODE" mdh:oldlogmode)(setvar "LOGFILEPATH" mdh:oldlogpath)
_QUIT
"@ | Set-Content -LiteralPath $scr -Encoding Ascii

$acad = Join-Path $AcadDir "acad.exe"
$args = "/nologo /t acadiso.dwt /b `"$scr`""
$process = Start-Process -FilePath $acad -ArgumentList $args -PassThru
if (-not $process.WaitForExit($TimeoutSec * 1000)) { $process | Stop-Process -Force; throw "Rectangular host test timed out." }
$all = Get-ChildItem $logs -Filter *.log | ForEach-Object { Get-Content $_.FullName }
$lines = $all | Select-String "MDH-SELFTEST|MDraftingHeper|error" | ForEach-Object { $_.Line }
$lines
$fails = ($lines | Select-String "MDH-SELFTEST FAIL").Count
$passes = ($lines | Select-String "MDH-SELFTEST PASS rectangular").Count
Write-Host "`nRectangular host tests: $passes passed, $fails failed. Logs: $logs"
if ($fails -gt 0 -or $passes -lt 7) { exit 1 }
