<#
.SYNOPSIS
  Removes the per-user MDraftingHeper.bundle. Touches nothing else in ApplicationPlugins.
  User settings and logs are kept unless -RemoveUserData is given.
#>
param([switch]$RemoveUserData)
$ErrorActionPreference = "Stop"
if (Get-Process acad -ErrorAction SilentlyContinue) { throw "AutoCAD is running. Close it before uninstalling." }
$target = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\MDraftingHeper.bundle"
if (Test-Path $target) { Remove-Item $target -Recurse -Force; Write-Host "Removed $target" }
else { Write-Host "MDraftingHeper is not installed for this user." }
if ($RemoveUserData) {
    foreach ($d in (Join-Path $env:APPDATA "MDraftingHeper"), (Join-Path $env:LOCALAPPDATA "MDraftingHeper")) {
        if (Test-Path $d) { Remove-Item $d -Recurse -Force; Write-Host "Removed $d" }
    }
}
