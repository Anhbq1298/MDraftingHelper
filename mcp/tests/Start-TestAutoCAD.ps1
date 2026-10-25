<#
.SYNOPSIS
  Development startup: ensures an AutoCAD 2021 session is running with the dedicated MCP test drawing
  open, then waits until it is attachable through COM (AutoCAD.Application.24) and idle.
  If AutoCAD is already running it is reused and the test drawing is opened in it; nothing is closed.
#>
param(
    [string]$AcadExe = "",
    [string]$Drawing = (Join-Path $PSScriptRoot "drawings\MDH_MCP_Test.dwg"),
    [int]$TimeoutSec = 120
)
$ErrorActionPreference = "Stop"

function Find-Acad2021 {
    if ($AcadExe -and (Test-Path $AcadExe)) { return $AcadExe }
    if ($env:Acad2021Dir -and (Test-Path (Join-Path $env:Acad2021Dir "acad.exe"))) { return (Join-Path $env:Acad2021Dir "acad.exe") }
    # Install location from the registry (R24.0 = AutoCAD 2021).
    foreach ($k in Get-ChildItem "HKLM:\SOFTWARE\Autodesk\AutoCAD\R24.0" -ErrorAction SilentlyContinue) {
        $loc = (Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue).AcadLocation
        if ($loc -and (Test-Path (Join-Path $loc "acad.exe"))) { return (Join-Path $loc "acad.exe") }
    }
    throw "AutoCAD 2021 (R24.0) not found. Pass -AcadExe or set Acad2021Dir."
}

function Get-Acad {
    try { return [Runtime.InteropServices.Marshal]::GetActiveObject("AutoCAD.Application.24") } catch { return $null }
}

$Drawing = (Resolve-Path $Drawing).Path
$app = Get-Acad
if (-not $app) {
    if (Get-Process acad -ErrorAction SilentlyContinue) {
        throw "acad.exe is running but not attachable as AutoCAD.Application.24 (another AutoCAD version, or still starting). Wait or close it."
    }
    $exe = Find-Acad2021
    Write-Host "Starting $exe with $Drawing"
    Start-Process $exe -ArgumentList "/nologo `"$Drawing`"" | Out-Null
}
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    if (-not $app) { $app = Get-Acad }
    if ($app) {
        try {
            if ($app.GetAcadState().IsQuiescent) {
                $active = $app.ActiveDocument.FullName
                if ($active -ne $Drawing) {
                    $open = @($app.Documents) | Where-Object { $_.FullName -eq $Drawing } | Select-Object -First 1
                    if ($open) { $open.Activate() } else { $null = $app.Documents.Open($Drawing) }
                    Start-Sleep -Seconds 2
                    continue
                }
                Write-Host "AutoCAD $($app.Version) ready; active drawing: $active"
                return
            }
        } catch { }
    }
    Start-Sleep -Seconds 2
}
throw "AutoCAD did not become ready within $TimeoutSec s."
