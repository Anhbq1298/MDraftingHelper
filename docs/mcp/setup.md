# AutoCAD MCP server — setup and startup

Connects Claude Code to a running **AutoCAD 2021 (R24.0)** on Windows through MCP over stdio. The server attaches through COM (`AutoCAD.Application.24`); it never launches AutoCAD itself.

## Prerequisites

| Item | Check |
| --- | --- |
| Windows 10/11 x64 | — |
| AutoCAD 2021 full | `mcp\tests\Start-TestAutoCAD.ps1` finds it via `HKLM\SOFTWARE\Autodesk\AutoCAD\R24.0` (or `-AcadExe` / `Acad2021Dir`) |
| Python 3.10+ with the `py` launcher | `py -0p` |
| Git | `git --version` |
| Claude Code CLI | `claude --version` |

## Install (repeatable)

```powershell
.\mcp\install.ps1            # or: .\mcp\install.ps1 -Python 3.13
```

This script:

- clones `EmptyKot/acad-cmd` into `external\acad-cmd` at the pinned commit;
- creates `mcp\server\.venv`;
- installs `acad-cmd` and `mdh-mcp` (editable) with the pins in `mcp\server\constraints.txt` (notably `mcp==1.30.0`, `pywin32==312`);
- runs the read-only diagnostic.

## Claude Code configuration

The project-scoped server is defined in `.mcp.json` at the repository root (server name `autocad`). It uses no user-specific paths: the command is the repo-relative venv interpreter.

```json
"command": "mcp\\server\\.venv\\Scripts\\python.exe",
"args": ["-m", "mdh_mcp.server"],
"env": { "AUTOCAD_MCP_TARGET_MAJOR": "24", "AUTOCAD_MCP_ALLOW_NEW_INSTANCE": "0", ... }
```

Use backslashes: a forward-slash relative path fails in Windows `CreateProcess` ("file not found"). Start Claude Code from the repository root, because the relative command resolves against that directory.

The first time, Claude Code asks you to approve the project server: run `claude` in the repo and accept it, or approve it in `/mcp`. Check with:

```powershell
claude mcp list        # autocad: ... Connected (after approval)
claude mcp get autocad
```

Inside Claude Code, `/mcp` shows the server and its tools.

## Daily startup

1. Start AutoCAD 2021 normally, or for testing run `.\mcp\tests\Start-TestAutoCAD.ps1`, which opens `mcp\tests\drawings\MDH_MCP_Test.dwg`.
2. Start Claude Code in the repo. The server starts on demand. If AutoCAD is closed or restarted, tools return `not_running`, and the next call after AutoCAD is back reconnects automatically.

Connection diagnostic (read-only):

```powershell
mcp\server\.venv\Scripts\python.exe -m mdh_mcp.diagnose
```

## Environment variables

| Variable | Default | Meaning |
| --- | --- | --- |
| `AUTOCAD_MCP_TARGET_MAJOR` | `24` (in .mcp.json) | Attach only to AutoCAD 2021 |
| `AUTOCAD_MCP_ALLOW_NEW_INSTANCE` | `0` (in .mcp.json) | Never let COM activation spawn AutoCAD |
| `AUTOCAD_MCP_EVENT_BRIDGE_ENABLED` | `0` | acad-cmd's optional in-process event bridge (not built or used in Phase 1) |
| `MDH_MCP_WRITE_ROOTS` | `mcp/tests/drawings` | `;`-separated folders whose drawings write tools may modify (relative paths are from the repo root) |
| `MDH_MCP_EXPOSE_RAW_TOOLS` | `0` | `1` re-enables acad-cmd's `send_command`, `run_lisp`, `load_lisp_file` and dictionary writes |
| `MDH_MCP_LOG_ROOT` | `mcp/logs` | Audit log root |

## Tools

| Tool | Kind | Notes |
| --- | --- | --- |
| `autocad_get_status` | read | version, PID, busy, command active, open documents, active drawing |
| `autocad_get_active_document` | read | name, full path, saved, read-only, INSUNITS, active layer, `writable_by_mcp` |
| `autocad_list_layers` | read | color, on/frozen/locked, linetype, lineweight |
| `autocad_get_entity(handle)` | read | type, layer, color/linetype/lineweight; lines: start, end, length, angle |
| `autocad_create_line(start, end, layer="0")` | write | test drawings only; layer must exist and be unlocked |
| `autocad_update_entity(handle, properties)` | write | `start`, `end` (lines), `move_by`, `layer`, `color` |
| `autocad_delete_entity(handle, confirm)` | write, destructive | requires `confirm=true` |
| acad-cmd read tools | read | `get_status`, `open_drawing`, `selection`, logfile streams, `dict_list/keys/xrecord_get` |

Write tools make one AutoCAD undo step each and never save the drawing.

## Tests

```powershell
mcp\server\.venv\Scripts\python.exe -m unittest discover -s mcp\tests -p "test_*.py"   # no AutoCAD needed
mcp\server\.venv\Scripts\python.exe mcp\tests\smoke_test.py                           # needs AutoCAD 2021
mcp\server\.venv\Scripts\python.exe mcp\tests\smoke_test.py --skip-restart            # without test 08
```

The smoke test quits and restarts AutoCAD for test 08, but only when no other modified drawing is open; otherwise test 08 reports BLOCKED. Reports are written to `mcp\tests\results\`.
