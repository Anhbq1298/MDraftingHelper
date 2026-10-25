# AutoCAD MCP — troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `claude mcp list` shows `autocad ... Pending approval` | Project MCP servers need a one-time approval | Run `claude` in the repo and approve, or use `/mcp` |
| Server fails to start: "file not found" | Forward-slash relative command, Claude Code started outside the repo, or venv missing | Use the backslash command in `.mcp.json`, start Claude Code from the repo root, run `mcp\install.ps1` |
| `ModuleNotFoundError: mcp.server.fastmcp` | `mcp` 2.x installed (FastMCP was renamed) | `pip install -c mcp\server\constraints.txt "mcp<2"` (install.ps1 does this) |
| Tools return `not_running` | No `acad.exe` running | Start AutoCAD 2021; the next call reconnects |
| Tools return `not_connected` | `acad.exe` runs but `AutoCAD.Application.24` is not attachable (another AutoCAD version, still starting, or a modal dialog blocking COM) | Wait for startup to finish, close modal dialogs, make sure it is AutoCAD 2021 |
| `drawing_not_allowed` | Active drawing is outside `MDH_MCP_WRITE_ROOTS` (by design) | Open `mcp\tests\drawings\MDH_MCP_Test.dwg`, or put a copy in a configured test folder |
| `layer_not_found` / `layer_locked` | Layer missing or locked | `autocad_list_layers`; unlock or choose another layer |
| `confirmation_required` | Delete without `confirm=true` | Approve, then call again with `confirm=true` |
| Calls slow or `busy` | AutoCAD is in a command or a dialog (COM is rejected while busy) | Finish or cancel the command in AutoCAD; calls retry automatically |
| `run_lisp` / `send_command` missing | Hidden by policy | Set `MDH_MCP_EXPOSE_RAW_TOOLS=1` only for trusted, deliberate sessions |
| Where are the logs? | — | `mcp\logs\logs\acad-cmd\<session>\session.jsonl` (one line per tool call); smoke reports in `mcp\tests\results\` |

## Diagnostic

```powershell
mcp\server\.venv\Scripts\python.exe -m mdh_mcp.diagnose
```

It checks the package versions, the COM registration and running `acad.exe`, then attaches and reads the status, active drawing and layer count. It never writes. Exit codes: 0 connected, 1 AutoCAD not reachable, 2 environment problem.

## Notes

- acad-cmd's logfile tools (`start_logging`, `get_last_output`) switch on AutoCAD's persistent `LOGFILEMODE`. If you use them, `stop_logging` turns it off again (best effort). The `autocad_*` tools do not touch it.
- A normally launched AutoCAD 2021 on this machine registers itself for `GetActiveObject`, so no `/automation` switch is needed.
