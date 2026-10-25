<#
.SYNOPSIS
  Per-user install of MDraftingHeper.bundle into %APPDATA%\Autodesk\ApplicationPlugins.
  Only this product's folder is created or replaced. No security settings are changed:
  ApplicationPlugins is one of AutoCAD's default trusted autoload locations.
  Close AutoCAD first (a loaded DLL cannot be replaced).
  Works from the repository (deploy\) and from an extracted release zip (same folder layout).
#>
param([string]$Source = (Join-Path $PSScriptRoot "MDraftingHeper.bundle"))
$ErrorActionPreference = "Stop"
if (Get-Process acad -ErrorAction SilentlyContinue) { throw "AutoCAD is running. Close all AutoCAD windows, then install again." }
if (-not (Test-Path (Join-Path $Source "Contents\Windows\MDraftingHeper.AutoCAD2021.dll"))) {
    throw "MDraftingHeper.bundle not found next to this script ($Source). Extract the whole zip, or run tools\Build-Bundle.ps1 in the repository."
}
$acadKey = Get-ChildItem "HKLM:\SOFTWARE\Autodesk\AutoCAD\R24.0" -ErrorAction SilentlyContinue
if (-not $acadKey) { Write-Warning "AutoCAD 2021 (R24.0) was not found in the registry. MDraftingHeper only loads in AutoCAD 2021." }

$plugins = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$target = Join-Path $plugins "MDraftingHeper.bundle"
New-Item -ItemType Directory -Force $plugins | Out-Null
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item $Source $target -Recurse
# Files from a downloaded/e-mailed zip carry the "from the internet" mark; .NET will not load blocked DLLs.
Get-ChildItem $target -Recurse -File | Unblock-File
Write-Host "Installed to $target"
Write-Host "Start AutoCAD 2021: the MDraftingHeper Ribbon tab and the MDH_REBAR, MDH_TIE, MDH_STEEL, MDH_RECT commands load automatically."
