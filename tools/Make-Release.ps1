<#
.SYNOPSIS
  Builds the bundle and packs a release zip for installing on other machines:
  dist\MDraftingHeper-<version>.zip containing
    MDraftingHeper.bundle\   Install.cmd   Uninstall.cmd   Install-MDraftingHeper.ps1
    Uninstall-MDraftingHeper.ps1   INSTALL.md
#>
param([string]$Acad2021Dir = $env:Acad2021Dir)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$deploy = Join-Path $root "deploy"

& (Join-Path $PSScriptRoot "Build-Bundle.ps1") -Acad2021Dir $Acad2021Dir | Out-Null

$version = ([xml](Get-Content (Join-Path $deploy "MDraftingHeper.bundle\PackageContents.xml"))).ApplicationPackage.AppVersion
$name = "MDraftingHeper-$version"
$dist = Join-Path $root "dist"
$stage = Join-Path $dist $name
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item (Join-Path $deploy "MDraftingHeper.bundle") $stage -Recurse
foreach ($f in "Install.cmd", "Uninstall.cmd", "Install-MDraftingHeper.ps1", "Uninstall-MDraftingHeper.ps1", "INSTALL.md") {
    Copy-Item (Join-Path $deploy $f) $stage
}

$zip = Join-Path $dist "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "Release: $zip"
Get-ChildItem $stage -Recurse -File | ForEach-Object { "  " + $_.FullName.Substring($stage.Length + 1) }
