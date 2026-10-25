# AutoCAD MCP — Phase 1 test report

Date: 2026-10-25.

Environment: Windows 11; AutoCAD 2021 (ACADVER `24.0s`, ProgID `AutoCAD.Application.24`); Python 3.10.11 venv; `mcp` 1.30.0; `pywin32` 312; acad-cmd `63f2272`; Claude Code 2.1.306.

## Verified (executed, with evidence)

| # | Check | Result | How |
| --- | --- | --- | --- |
| U | mdh_mcp unit tests: write-root guard, point validation, tool registration with raw tools hidden | 6/6 OK | `unittest` (no AutoCAD) |
| U | acad-cmd upstream unit tests | 16 run, OK, 1 integration skipped | `unittest` in `external/acad-cmd` |
| — | acad-cmd unmodified against AutoCAD 2021: stdio init, 16 tools, `run_lisp` returns `"24.0s (LMS Tech)"` | OK | upstream `scripts/mcp_smoketest_stdio.py` |
| 00 | MCP discovery: the 7 `autocad_*` tools are listed; `send_command`, `run_lisp`, `load_lisp_file` hidden | PASS | `smoke_test.py` over stdio |
| 01 | Connection: running AutoCAD 2021 detected (major 24, PID, idle) | PASS | smoke |
| 02 | Drawing: active test drawing identified; `writable_by_mcp` true | PASS | smoke |
| 03 | Layers: read (`0`, `MDH_MCP_TEST`) | PASS | smoke |
| 04 | Geometry: line created on `MDH_MCP_TEST`; handle returned | PASS | smoke |
| 05 | Entity query: type, layer, start, end, length read back | PASS | smoke |
| 06 | Modification: moved by (10,20), end changed, color 1; verified by re-read | PASS | smoke |
| 07 | Cleanup: deleted with `confirm=true`; re-read returns `not_found` | PASS | smoke |
| 08 | Recovery: while AutoCAD is down → `not_running` with action; after restart the same server session reconnects and writes | PASS | smoke (quits and restarts AutoCAD) |
| S1 | Safety: delete without `confirm` refused (`confirmation_required`) | PASS | smoke |
| S2 | Safety: write in a non-test (unsaved) drawing refused (`drawing_not_allowed`); active-document switch followed | PASS | smoke |
| S3 | Safety: test DWG file unchanged on disk (never saved) | PASS | smoke |
| C | Claude Code discovers the project server `autocad` from `.mcp.json` (stdio, env as configured) | Listed, *Pending approval* | `claude mcp list`, `claude mcp get autocad` |
| C | The exact `.mcp.json` command/args/env start the server from the repo root; `autocad_get_active_document` succeeds | OK | MCP stdio client using the config values |
| I | `mcp/install.ps1` re-run end to end (pinned clone, venv, pinned deps, diagnostic attached) | OK | script run |
| D | Diagnostic: attached and read status, active drawing and layers; also fails fast with exit 1 when AutoCAD was not running | OK | `python -m mdh_mcp.diagnose` |
| L | AutoCAD survives the server's exit (still running 30 s later) | OK | process check |

The full smoke suite passed 12/12 in two consecutive runs: `mcp/tests/results/smoke_20261025_120549.json` and `smoke_20261025_121105.json`.

## Defects found and fixed during testing

1. acad-cmd cannot import with `mcp` 2.x (FastMCP renamed): pinned `mcp<2`.
2. First call after AutoCAD start reported `not_connected` (not yet attachable): attach is retried for up to 20 s while `acad.exe` is running.
3. After an AutoCAD restart, reading a property of a just-created line failed (`AttributeError: AddLine.Lineweight` / "retry later"). The busy retry now covers `RPC_E_SERVERCALL_RETRYLATER`, and optional properties are read tolerantly, so a created entity is always reported with its handle.
4. A forward-slash relative `command` in `.mcp.json` fails in Windows `CreateProcess`: switched to backslashes.
5. The installer aborted on git's stderr output under Windows PowerShell 5.1: native tools are now judged by their exit code.

## Not verified / blocked

- **Claude Code approval and live use.** The `autocad` project server is discovered but awaits your one-time approval (Claude Code's trust prompt for project servers). After approving, confirm `claude mcp list` shows *Connected* and run, in a Claude Code session, the tools from `autocad_get_status` through `autocad_delete_entity` on the test drawing.
- acad-cmd's `.NET` event bridge (`AcadEventBridge`) was not built or loaded; it is disabled.
- Write behaviour on locked layers, read-only drawings and AutoCAD busy inside a long command is implemented (`layer_locked`, `read_only`, busy retry) but has no automated test yet.
- Upstream read-only tools (`selection`, `open_drawing`, dictionary reads) were listed but not exercised by our smoke test.

## Side effect found and corrected

During the earlier add-in host tests, scripts left AutoCAD's persistent `LOGFILEMODE=1` and `LOGFILEPATH` pointing at a temp folder. Both were restored to AutoCAD's defaults (logging off; `%LOCALAPPDATA%\Autodesk\AutoCAD 2021\R24.0\enu\`). `tools/DynamicBlocks/Build-DynamicBlocks.ps1` and `tools/HostTest/Run-HostTests.ps1` now save and restore both settings; that change is syntax-checked but has not been re-run in AutoCAD.
