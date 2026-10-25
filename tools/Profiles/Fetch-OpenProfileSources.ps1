<#
.SYNOPSIS
  Clones the openly licensed profile datasets into external\ at the pinned commits used by
  convert_open_profiles.py. Data only: nothing from these repositories is executed.
#>
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$sources = @(
    @{ Dir = "steel-sections";          Url = "https://github.com/p-horne/steel-sections";         Commit = "7c5864feab6f9f0c7f8294dcf1f37a00ec7d635b" },
    @{ Dir = "Steel_Section_Database";  Url = "https://github.com/ulmewix/Steel_Section_Database"; Commit = "524172a3fff6a2c4da03f56a577402aa3a88876a" }
)
foreach ($s in $sources) {
    $dir = Join-Path $root ("external\" + $s.Dir)
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"   # git writes progress to stderr
    if (-not (Test-Path (Join-Path $dir ".git"))) { git clone --quiet $s.Url $dir 2>&1 | Out-Null }
    git -C $dir fetch --quiet --depth 1 origin $s.Commit 2>&1 | Out-Null
    git -C $dir checkout --quiet $s.Commit 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    $head = (git -C $dir rev-parse HEAD).Trim()
    if ($head -ne $s.Commit) { throw "$($s.Dir): expected $($s.Commit), got $head" }
    Write-Host "$($s.Dir) @ $head"
}
