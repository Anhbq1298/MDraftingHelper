<#
.SYNOPSIS
  Authors the MDraftingHeper steel Dynamic Block templates in a real AutoCAD 2021 session
  and saves them to assets\DynamicBlocks\MDH_SteelSections.dwg.

.DESCRIPTION
  Starts acad.exe with a generated script that runs the block editor (-BEDIT) once per
  template and calls the authoring functions in MDH_SteelBlocks.lsp. The command history
  log is written next to the output and scanned for MDH-AUTHOR-ERROR lines.
  With -Verify, a second session opens the produced DWG and runs MDH_VerifyBlocks.lsp.

  AutoCAD opens a visible window while this runs. Do not type into it.
#>
param(
    [string]$AcadDir = $(if ($env:Acad2021Dir) { $env:Acad2021Dir } else { "C:\Program Files\Autodesk\AutoCAD 2021" }),
    [string]$Output = (Join-Path $PSScriptRoot "..\..\assets\DynamicBlocks\MDH_SteelSections.dwg"),
    [int]$TimeoutSec = 300,
    [switch]$Verify,
    [switch]$VerifyOnly
)
$ErrorActionPreference = "Stop"

$acad = Join-Path $AcadDir "acad.exe"
if (-not (Test-Path $acad)) { throw "acad.exe not found in '$AcadDir'. Set -AcadDir or `$env:Acad2021Dir." }

$Output = [IO.Path]::GetFullPath($Output)
$outDir = Split-Path $Output
New-Item -ItemType Directory -Force $outDir | Out-Null
$work = Join-Path ([IO.Path]::GetTempPath()) ("MDH_BlockBuild_" + (Get-Date -Format yyyyMMdd_HHmmss))
New-Item -ItemType Directory -Force $work | Out-Null

function To-Lisp([string]$p) { $p -replace '\\', '/' }

function Invoke-AcadScript([string]$name, [string]$body, [string]$drawing) {
    $logDir = Join-Path $work ("log_" + $name)
    New-Item -ItemType Directory -Force $logDir | Out-Null
    $scr = Join-Path $work ($name + ".scr")
    $header = "FILEDIA 0`r`nCMDECHO 1`r`n(setq mdh:oldlogpath (getvar `"LOGFILEPATH`") mdh:oldlogmode (getvar `"LOGFILEMODE`"))`r`n(setvar `"LOGFILEPATH`" `"$(To-Lisp $logDir)/`")`r`nLOGFILEON`r`n"
    # LOGFILEMODE/LOGFILEPATH are persistent user settings: restore them before AutoCAD quits.
    $restore = "(setvar `"LOGFILEMODE`" mdh:oldlogmode)(setvar `"LOGFILEPATH`" mdh:oldlogpath)`r`n"
    $body = [regex]::Replace($body, "(?m)^QUIT", { param($m) $restore + "QUIT" })
    Set-Content $scr -Encoding ascii -Value ($header + $body)
    $argList = if ($drawing) { "`"$drawing`" /nologo /b `"$scr`"" } else { "/nologo /t acadiso.dwt /b `"$scr`"" }
    $p = Start-Process -FilePath $acad -ArgumentList $argList -PassThru
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        $p | Stop-Process -Force
        Write-Warning "AutoCAD did not finish '$name' within $TimeoutSec s and was stopped."
    }
    $log = Get-ChildItem $logDir -Filter *.log | ForEach-Object { Get-Content $_.FullName }
    Set-Content (Join-Path $outDir ("build_" + $name + ".log")) -Value $log
    return $log
}

if (-not $VerifyOnly) {
    $lsp = To-Lisp (Join-Path $PSScriptRoot "MDH_SteelBlocks.lsp")
    $tmpDwg = Join-Path $work "MDH_SteelSections.dwg"

    $blocks = @(
        @{ Name = "MDH_CHS";      Fn = "mdh-author-chs" },
        @{ Name = "MDH_SHS";      Fn = "mdh-author-shs" },
        @{ Name = "MDH_RHS";      Fn = "mdh-author-rhs" },
        @{ Name = "MDH_ISECTION"; Fn = "mdh-author-isection" }
    )
    $sb = New-Object Text.StringBuilder
    [void]$sb.AppendLine("(load `"$lsp`")")
    [void]$sb.AppendLine("INSUNITS 4")
    foreach ($b in $blocks) {
        [void]$sb.AppendLine("-BEDIT")
        [void]$sb.AppendLine($b.Name)
        [void]$sb.AppendLine("($($b.Fn))")
        [void]$sb.AppendLine("BSAVE")
        [void]$sb.AppendLine("BCLOSE")
        [void]$sb.AppendLine("(mdh-finish-block `"$($b.Name)`")")
    }
    [void]$sb.AppendLine("SAVEAS 2018 `"$(To-Lisp $tmpDwg)`"")
    [void]$sb.AppendLine("QUIT Y")

    $log = Invoke-AcadScript "author" $sb.ToString() $null
    $errors = @($log | Select-String "MDH-AUTHOR-ERROR|over-constrain|Invalid|; error:")
    $oks = ($log | Select-String "MDH-AUTHOR-OK").Count
    $expectedOks = 46   # CHS 7 + SHS 11 + RHS 11 + I-section 17 (constraint steps + finish markers)
    if ($oks -ne $expectedOks) { $errors += "Expected $expectedOks MDH-AUTHOR-OK markers, found $oks." }
    Write-Host "Authoring: $oks OK markers, $($errors.Count) problems."
    $errors | ForEach-Object { Write-Warning "    $errors | ForEach-Object { Write-Warning $_.Line }" }

    if (-not (Test-Path $tmpDwg)) { throw "AutoCAD did not save the template drawing. See build_author.log." }
    if ($errors.Count -gt 0) { throw "Authoring reported problems; output not replaced. See build_author.log." }
    Copy-Item $tmpDwg $Output -Force
    Write-Host "Saved $Output"

}

if ($Verify -or $VerifyOnly) {
    $vlsp = To-Lisp (Join-Path $PSScriptRoot "MDH_VerifyBlocks.lsp")
    # One stage per script line: constraint parameters re-evaluate when each line's command ends.
    $stages = @(
        "(load `"$vlsp`")",
        "(mdh-verify-set)", "(mdh-verify-check)",
        "(mdh-verify-copy-set)", "(mdh-verify-copy-check)",
        # Undo/Redo is checked by the add-in's MDH_SELFTEST command instead: AutoLISP in a
        # batch script holds an open undo group, so U here does not reflect interactive use.
        "(mdh-verify-invalid-set)", "(mdh-verify-invalid-check)",
        "QUIT Y")
    $vlog = Invoke-AcadScript "verify" (($stages -join "`r`n") + "`r`n") $Output
    $results = $vlog | Select-String "MDH-VERIFY"
    $results | ForEach-Object { Write-Host $_.Line }
    $fails = ($results | Select-String "FAIL").Count
    if ($fails -gt 0) { throw "$fails verification checks failed." }
}

