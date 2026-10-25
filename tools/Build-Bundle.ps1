<#
.SYNOPSIS
  Builds Release and assembles deploy\MDraftingHeper.bundle (PackageContents.xml + Contents).
  Autodesk assemblies are never copied (they are referenced with Private=false and filtered here).
#>
param(
    [string]$Configuration = "Release",
    [string]$Acad2021Dir = $env:Acad2021Dir
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$proj = Join-Path $root "src\MDraftingHeper.AutoCAD2021\MDraftingHeper.AutoCAD2021.csproj"

$out = Join-Path $root "src\MDraftingHeper.AutoCAD2021\bin\$Configuration\net48"
# MSBuild never deletes stale copied files (e.g. a removed profile library); start from an empty Resources folder.
$staleResources = Join-Path $out "Resources"
if (Test-Path $staleResources) { Remove-Item $staleResources -Recurse -Force }

$buildArgs = @("build", $proj, "-c", $Configuration, "-nologo", "-v", "q")
if ($Acad2021Dir) { $buildArgs += "/p:Acad2021Dir=$Acad2021Dir" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$bundle = Join-Path $root "deploy\MDraftingHeper.bundle"
$contents = Join-Path $bundle "Contents"
if (Test-Path $contents) { Remove-Item $contents -Recurse -Force }
$win = New-Item -ItemType Directory -Force (Join-Path $contents "Windows")
$res = New-Item -ItemType Directory -Force (Join-Path $contents "Resources")

# Owned assemblies only.
$owned = "MDraftingHeper.AutoCAD2021.dll", "MDraftingHeper.Core.dll", "MDraftingHeper.UI.dll"
foreach ($f in $owned) { Copy-Item (Join-Path $out $f) $win }
$autodesk = Get-ChildItem $out -Filter *.dll | Where-Object { $_.Name -match '^(ac|ad)' -and $_.Name -notmatch '^MDraftingHeper' }
if ($autodesk) { throw "Autodesk assemblies found in build output: $($autodesk.Name -join ', ')" }

foreach ($dir in "Profiles", "HookPresets", "DynamicBlocks", "Icons") {
    $src = Join-Path $out "Resources\$dir"
    if (-not (Test-Path $src)) { throw "Missing resources folder $src" }
    Copy-Item $src (Join-Path $res $dir) -Recurse
}
foreach ($json in Get-ChildItem (Join-Path $res "Profiles") -Filter *.json) {
    if ((Get-Content $json.FullName -Raw | ConvertFrom-Json).isDemonstration) {
        throw "Demonstration profile data must not ship: $($json.Name)"
    }
}
if (-not (Test-Path (Join-Path $res "DynamicBlocks\MDH_SteelSections.dwg"))) {
    throw "Steel templates missing. Run tools\DynamicBlocks\Build-DynamicBlocks.ps1 first."
}

[xml](Get-Content (Join-Path $bundle "PackageContents.xml")) | Out-Null   # manifest must be well-formed
Get-ChildItem $bundle -Recurse -File | ForEach-Object { $_.FullName.Substring($bundle.Length + 1) }
