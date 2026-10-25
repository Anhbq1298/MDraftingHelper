# Upstream: acad-cmd

- Source: https://github.com/EmptyKot/acad-cmd, MIT licence, cloned to `external/acad-cmd`.
- Pinned commit: `63f2272c662074def7c58bb150fa0861eb05b4e3` (2026-04-22).
- **No upstream files are modified.** All changes are in `mcp/server` (package `mdh_mcp`), which imports acad-cmd.

## Review summary (before running)

- Dependencies: `mcp`, `pywin32`. No network access.
- Subprocess use: `tasklist` to find `acad.exe` PIDs, plus optional `acad.exe` launch only when `AUTOCAD_MCP_ACAD_EXE` is set (not set here).
- Registry: reads AutoCAD ProgID `CurVer` only when `AUTOCAD_MCP_PREFER_CURVER=1`.
- Exposes raw `send_command` / `run_lisp` / `load_lisp_file` and dictionary writes.
- Its optional `AcadEventBridge` (.NET 4.8 plug-in, named pipe) is not built or loaded in Phase 1.

## Verified behaviour (AutoCAD 2021 on this machine)

- Unit tests: 16 run, OK (1 integration test skipped), with `mcp==1.30.0`.
- `scripts/mcp_smoketest_stdio.py`: initialise, list 16 tools, `start_logging`, and `run_lisp("(getvar 'ACADVER)")` returning `"24.0s (LMS Tech)"` from the live session.

## Compatibility issues found and how they are handled (without editing upstream)

| Issue | Handling in mdh_mcp |
| --- | --- |
| Declares `mcp>=1.0.0` but imports `mcp.server.fastmcp`, which mcp 2.x removed; a fresh install pulls 2.3.0 and fails to import | Pin `mcp<2` (`constraints.txt`: 1.30.0) |
| With `AUTOCAD_MCP_TARGET_MAJOR` set and AutoCAD not running, `connect()` falls back to `Dispatch()`, which can start AutoCAD and (with `ALLOW_NEW_INSTANCE=0`) quit it again; slow, and documented upstream as hang-prone | `CadOperations.app()` checks for `acad.exe` first and returns `not_running` immediately |
| `com_retry` retries only `RPC_E_CALL_REJECTED`; a freshly started AutoCAD also answers `RPC_E_SERVERCALL_RETRYLATER` / transient member lookups | Own `com_retry` handles both codes, attach is retried up to 20 s while `acad.exe` is starting, and optional properties are read tolerantly |
| Caches the document object from connect time | Every operation resolves `ActiveDocument` again |
| Log dir is `<cwd>/logs/acad-cmd`, fixed at import | `server.build()` changes directory to `MDH_MCP_LOG_ROOT` before importing |
| Smoke script expects `external/acad-cmd/.venv` | A local directory junction to `mcp/server/.venv` was used to run it (not committed) |
