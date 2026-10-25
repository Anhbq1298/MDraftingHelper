<#
.SYNOPSIS
  Repeatable install of the MDraftingHelper AutoCAD MCP server into an isolated venv (mcp\server\.venv).
  1. Clones EmptyKot/acad-cmd into external\acad-cmd at the pinned commit (if missing).
  2. Creates the venv with Python 3.10+ (py launcher).
  3. Installs acad-cmd and mdh-mcp (editable) with pinned versions from mcp\server\constraints.txt.
  4. Runs the read-only connection diagnostic (exit 1 there only means AutoCAD is not running).
#>
param(
    [string]$Python = "3.10",
    [string]$AcadCmdCommit = "63f2272c662074def7c58bb150fa0861eb05b4e3"
)
$ErrorActionPreference = "Stop"

# Windows PowerShell 5.1 turns any native stderr line into a terminating error under "Stop";
# git and pip write progress there, so native tools are judged by their exit code instead.
function Invoke-Native([string]$exe, [string[]]$arguments, [string]$what) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & $exe @arguments 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -match '\S' } | Out-Host }
    finally { $ErrorActionPreference = $prev }
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)." }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ext = Join-Path $root "external\acad-cmd"

if (-not (Test-Path (Join-Path $ext "pyproject.toml"))) {
    Invoke-Native git @("clone", "https://github.com/EmptyKot/acad-cmd", $ext) "git clone"
}
$head = (& git -C $ext rev-parse HEAD).Trim()
if ($head -ne $AcadCmdCommit) {
    Invoke-Native git @("-C", $ext, "fetch", "--depth", "1", "origin", $AcadCmdCommit) "git fetch"
    Invoke-Native git @("-C", $ext, "checkout", "--quiet", $AcadCmdCommit) "git checkout"
}
Write-Host "acad-cmd at $AcadCmdCommit"

$venv = Join-Path $root "mcp\server\.venv"
$py = Join-Path $venv "Scripts\python.exe"
if (-not (Test-Path $py)) {
    Invoke-Native py @("-$Python", "-m", "venv", $venv) "Creating the venv with Python $Python (install Python 3.10+ or pass -Python 3.x)"
}
$constraints = Join-Path $root "mcp\server\constraints.txt"
Invoke-Native $py @("-m", "pip", "install", "-q", "--disable-pip-version-check", "-c", $constraints, $ext) "pip install acad-cmd"
Invoke-Native $py @("-m", "pip", "install", "-q", "--disable-pip-version-check", "-c", $constraints, "-e", (Join-Path $root "mcp\server")) "pip install mdh-mcp"

$env:AUTOCAD_MCP_TARGET_MAJOR = "24"
$env:AUTOCAD_MCP_ALLOW_NEW_INSTANCE = "0"
& $py -m mdh_mcp.diagnose
Write-Host "`nInstalled. Claude Code picks up the project-scoped server from .mcp.json (server name: autocad)."
