<#
.SYNOPSIS
  Per-user install of MDraftingHeper.bundle into %APPDATA%\Autodesk\ApplicationPlugins.
  Only this product's folder is created or replaced. No security settings are changed:
  ApplicationPlugins is one of AutoCAD's default trusted autoload locations.
  Close AutoCAD first (a loaded DLL cannot be replaced).
#>
param([string]$Source = (Join-Path $PSScriptRoot "MDraftingHeper.bundle"))
$ErrorActionPreference = "Stop"
if (Get-Process acad -ErrorAction SilentlyContinue) { throw "AutoCAD is running. Close it before installing (the DLL is locked while loaded)." }
if (-not (Test-Path (Join-Path $Source "Contents\Windows\MDraftingHeper.AutoCAD2021.dll"))) {
    throw "Bundle not built. Run tools\Build-Bundle.ps1 first."
}
$plugins = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
$target = Join-Path $plugins "MDraftingHeper.bundle"
New-Item -ItemType Directory -Force $plugins | Out-Null
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item $Source $target -Recurse
Write-Host "Installed to $target. Start AutoCAD 2021; the MDraftingHeper Ribbon tab loads automatically."
