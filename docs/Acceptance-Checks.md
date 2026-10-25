# Acceptance checks and verification report

Status as of 2026-10-25. Machine: Windows 11, AutoCAD 2021 (R24.0) full. "Host" means the check ran inside AutoCAD 2021 with the add-in loaded from the installed bundle. A Core test pass is **not** evidence of AutoCAD behaviour; both are listed separately.

## 1. Automated tests: 71/71 pass

`dotnet test tests\MDraftingHeper.Core.Tests` covers:

- **Rebar:** mixed diameters, tolerance grouping without chaining, nominal labels only when every bar fits, duplicate ids, As/Ag/ρ at full precision, target ρ, invalid input; boundary area including arc bulges, open/self-intersecting/degenerate rejection, inside/crossing/outside classification.
- **Tie:** all three angles × flip are tangent-continuous; exact sweeps; arcs centred on the actual bars; Dref = max(DA, DB) at both ends; 90/135/180 tail directions; independent A/B tail lengths; flip mirroring; rotation + translation invariance; single-hook ends; degenerate input; clashing tails rejected (not shortened); preset rule selection.
- **Steel:** CHS/RHS/SHS/I validation (void, corner, fillet overlap); geometric areas against closed-form formulas; anchor offsets; demo fixture loads with provenance; shipped open libraries (1,749 profiles) load without errors, unique ids, family counts, spot checks, standard corner-radius rules; CSV template; unit conversion; inner→outer radius; duplicate ids, missing data, unknown units, invalid geometry, missing source; missing radius kept as missing; profile matching and Custom.
- **Settings and presets:** round trip; corrupt or partial file falls back to defaults; shipped presets include Manual and none claims verification.
- **Quick Draw Rectangular:** face counts and corner de-duplication; face/custom naming; actual-area calculation; cover offsets; fresh IDs after face regeneration; rename-only identity/tie preservation; geometry-edit invalidation; custom-mode auto-arrangement refusal; centre/clear distance; restraint audit; stale revision rejection; outer-hoop arc/closure geometry; separate end-hook angles through the shared tie engine.

## 2. Host-verified in AutoCAD 2021

| Area | Check | Evidence |
| --- | --- | --- |
| Templates (Stage B) | 4 Dynamic Block definitions authored by AutoCAD's block editor; saved, closed and reopened; 45/45 geometry checks with the add-in **not loaded** (all five families follow D/t/B/H/tw/tf/r, inner radius = r − t, fillets, independent instances, copies) | `Build-DynamicBlocks.ps1 -Verify` |
| Native Properties | Ctrl+1 on an inserted UB shows **Custom: H, B, tw, tf, r** with Block Unit = Millimeters | screenshot (`Capture-Screens.ps1`) |
| Loading | Bundle autoloads from `%APPDATA%\Autodesk\ApplicationPlugins`; no Autodesk DLLs in the bundle | install + logs |
| Ribbon | Tab created once by the normal startup/Idle path; still once after a WSCURRENT change and with a second drawing; repeated Start/Ensure adds no duplicate | `Run-RibbonTest.ps1` (3/3), `MDH_SELFTEST` |
| Rebar | Reader: coplanarity filter, crossing/outside classification, As/Ag/ρ; native Table rows and cells, aligned to a 30° rotated UCS, layer | `MDH_SELFTEST` |
| Rebar dialog | Real selection → dialog shows groups, As, Ag, ρ; flagged bars red; Insert disabled until exclusions acknowledged | screenshot |
| Tie | Bars in a tilted plane (arbitrary OCS): LWPOLYLINE normal, 6 vertices, bulges = ±tan(θ/4), arcs centred on the actual WCS bar centres, Dref at both ends, H10 lineweight 0.18 mm, versioned XRecord. All 3 angles × flip | `MDH_SELFTEST` (54 checks) |
| Tie dialog | Real selection of two circles → dialog with DA, DB, Dref, Rfit/Rmandrel/Rc, clearances, preset provenance, preview | screenshot |
| Quick Draw Rectangular writer | Face layout and restraint audit; rotated-UCS insertion; one Circle per physical bar; true-bulge tie polylines; no text, axes or dimensions | `MDH_SELFTEST` |
| Steel | All five families inserted from the template DWG as geometry-only Dynamic Blocks; Properties hold the profile values; evaluated geometry matches | `MDH_SELFTEST_STEEL2` |
| Steel sync | Properties edit in a command → status becomes `custom`; untouched twin unchanged; restoring dimensions re-matches the profile; impossible t → `invalid` (geometry not trusted) | `MDH_SELFTEST_STEEL2/3` |
| Undo/Redo | Edit B → U restores B → REDO restores the edit, with valid geometry/status after both | `MDH_SELFTEST_UNDO*` |
| Anchor | Top-left anchor puts the section corner on the picked point | `MDH_SELFTEST_STEEL2` |
| WBLOCK | Edited block written to a new DWG keeps its dynamic block, edited values and metadata | `MDH_SELFTEST` |
| Save/reopen | Drawing saved with SAVEAS, reopened in a new session: blocks dynamic, metadata present | `MDH_SELFTEST_REOPEN` |
| Install/uninstall | Install copies only `MDraftingHeper.bundle`; uninstall removes only it (other plug-ins untouched) | scripts |

Last full host run on the final build: `Run-HostTests.ps1` passed 117 of 117 checks. `Run-RibbonTest.ps1` passed all three startup/workspace/document lifecycle rounds, explicitly finding four tools including `MDH_RECT`. A batch-mode Ribbon check was removed from that runner because Idle never fires inside a `/b` script; the startup path is covered by `Run-RibbonTest.ps1` instead.

Defects found by host testing and fixed:

- XAML style setter failure inside AutoCAD (Steel dialog would not open)
- Tie Accept/Flip prompt keyword mismatch
- An earlier transient tie preview assigned layer `MDH_TIE` before it existed, so the command failed in a fresh drawing. The current dialog-first workflow no longer uses that transient step; the layer is created only when **Apply & Draw** commits.
- Stale dialog result: a reopened dialog kept the previous round's action. After Esc in the tie preview, closing the reopened dialog with X re-entered the preview, and Enter then drew the tie. Steel and Rebar repeated their insertion prompt the same way. Dialogs now reset the action each time they open (`DialogViewModel.BeginDialog`), with regression tests in `DialogRoundTests`. This is the likely cause of the inconsistent keystroke runs.
- Tie dialog clipping
- Preview fallback text drawn over valid previews

The current workflow opens the configuration window first, uses **Select 2 Circles…** for model-space selection, then commits directly with **Apply & Draw**. The dialog preview supplies the visual confirmation; there is no additional command-line Accept/Flip step.

## 3. Implemented but not verified in the host

- Quick Draw Rectangular editor and picker interaction, command-level point/rotation prompts, and one-step Undo still need a manual UI run. The native writer, rotated-UCS geometry and geometry-only output are host-tested.

- Tie dialog-first interaction: configuration window opens before selection; cancelling **Select 2 Circles…** returns to the window; **Apply & Draw** inserts one polyline; one U removes it.
- Clicking *Insert Table* and *Insert Block…* in the dialogs (the underlying writers are host-tested).
- Esc at **every** prompt of every command (only selection-cancel paths were exercised).
- Typing values by hand into the Properties palette, and grip editing. The palette shows the fields, and the same values set through the API were tested. The Idle-time sync for edits made outside any command is unverified.
- Locked/frozen layer and read-only drawing messages; non-mm conversion and INSUNITS=0 prompts; steel refusal in non-mm drawings.
- Steel insertion with rotation and in a rotated UCS (the anchor is tested only unrotated in WCS).
- Reconciling a drawing whose steel blocks were edited with the add-in absent (reopen with intact metadata is tested; the externally edited case is not).
- Copy/paste between drawings through the clipboard (WBLOCK was tested instead).
- Settings persistence across AutoCAD sessions (file handling is unit-tested).
- That the WSCURRENT switch in the Ribbon test actually changed the workspace (the command was sent; the switch was not independently confirmed).

## 4. Blocked or outstanding

- **Profile data:** Dlubal library data is not available for reuse (online app only, no licence). Shipped data comes from openly licensed datasets (MPL-2.0 / MIT); UB/UC upstream does not name its catalogue. AISC W/HP/M not bundled (redistribution terms unclear); importable by the user. Taper-flange series need a new block. HF hollow sections: inner corner drawn as r − t (0.5 t) instead of 1.0 t.
- **Hook preset compliance:** the project bend-detail preset records the supplied diagram and is the default, but has no clause reference. The "EC2 + Singapore NA" preset holds unverified candidate values and must be checked against the authorised code text and National Annex before `IsVerified` is set.
- **Parameter limits:** the templates have no min/max value sets. Invalid values are detected by the add-in, not prevented by the block.

## 5. How to re-run

```powershell
dotnet test tests\MDraftingHeper.Core.Tests\MDraftingHeper.Core.Tests.csproj
.\tools\DynamicBlocks\Build-DynamicBlocks.ps1 -VerifyOnly
.\tools\Build-Bundle.ps1; .\deploy\Install-MDraftingHeper.ps1
.\tools\HostTest\Run-HostTests.ps1
.\tools\HostTest\Run-RibbonTest.ps1
.\tools\HostTest\Capture-Screens.ps1
.\deploy\Uninstall-MDraftingHeper.ps1
```
