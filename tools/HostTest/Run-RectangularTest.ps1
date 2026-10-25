<#
.SYNOPSIS
  Builds the current Debug add-in, NETLOADs it into an isolated AutoCAD 2021 batch session, and
  runs the Quick Draw Rectangular native writer checks without replacing the installed bundle.
#>
param(
    [string]$AcadDir = $(if ($env:Acad2021Dir) { $env:Acad2021Dir } else { "C:\Program Files\Autodesk\AutoCAD 2021" }),
    [int]$TimeoutSec = 180
)
$ErrorActionPreference = "Stop"
$running = Get-Process acad -ErrorAction SilentlyContinue
if ($running) { throw "Close every AutoCAD process before running this host test (running PID(s): $($running.Id -join ', '))." }
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $root "src\MDraftingHeper.AutoCAD2021\MDraftingHeper.AutoCAD2021.csproj"
& dotnet build $project -c Debug -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dll = Join-Path $root "src\MDraftingHeper.AutoCAD2021\bin\Debug\net48\MDraftingHeper.AutoCAD2021.dll"
$work = Join-Path ([IO.Path]::GetTempPath()) ("MDH_RectHost_" + (Get-Date -Format yyyyMMdd_HHmmss))
$isolate = Join-Path $work "isolate"
$logs = Join-Path $work "logs"
$null = New-Item -ItemType Directory -Force $isolate, $logs
$scr = Join-Path $work "rectangular.scr"
function L([string]$path) { $path -replace '\\', '/' }
@"
FILEDIA 0
CMDECHO 1
(setq mdh:oldsecure (getvar "SECURELOAD") mdh:oldlogpath (getvar "LOGFILEPATH") mdh:oldlogmode (getvar "LOGFILEMODE"))
(setvar "SECURELOAD" 0)
(setvar "LOGFILEPATH" "$(L $logs)/")
LOGFILEON
NETLOAD
$(L $dll)
MDH_SELFTEST_RECT
(setvar "SECURELOAD" mdh:oldsecure)(setvar "LOGFILEMODE" mdh:oldlogmode)(setvar "LOGFILEPATH" mdh:oldlogpath)
QUIT
N
"@ | Set-Content -LiteralPath $scr -Encoding Ascii

$acad = Join-Path $AcadDir "acad.exe"
$args = "/nologo /t acadiso.dwt /isolate MDHRect `"$isolate`" /b `"$scr`""
$process = Start-Process -FilePath $acad -ArgumentList $args -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit($TimeoutSec * 1000)) { $process | Stop-Process -Force; throw "Rectangular host test timed out." }
$all = Get-ChildItem $logs -Filter *.log | ForEach-Object { Get-Content $_.FullName }
$lines = $all | Select-String "MDH-SELFTEST|MDraftingHeper|error" | ForEach-Object { $_.Line }
$lines
$fails = ($lines | Select-String "MDH-SELFTEST FAIL").Count
$passes = ($lines | Select-String "MDH-SELFTEST PASS rectangular").Count
Write-Host "`nRectangular host tests: $passes passed, $fails failed. Logs: $logs"
if ($fails -gt 0 -or $passes -lt 7) { exit 1 }
