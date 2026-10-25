# CLAUDE.md — MDraftingHelper

Two parts live in this repository:

1. **AutoCAD 2021 add-in "MDraftingHeper"** (`src/`, C#/.NET Framework 4.8): Rebar Percentage, Tie & Hook, native Dynamic Block steel sections. See `README.md` and `docs/`.
2. **AutoCAD MCP server** (`mcp/`, Python): lets Claude Code inspect and edit a running AutoCAD 2021 through MCP tools (server name `autocad`). Phase 1 = connectivity and safe single-entity operations only.

## AutoCAD 2021 facts that constrain everything

- AutoCAD 2021 = release R24.0 = COM ProgID `AutoCAD.Application.24` = ACADVER `24.0`.
- In-process plug-ins must target **.NET Framework 4.8** (x64). .NET Core/.NET 8 plug-ins need AutoCAD 2025+. Do not use the Autodesk .NET 8 templates here.
- Out-of-process access is **COM automation** (pywin32 here). COM calls are marshalled into AutoCAD's UI thread; AutoCAD may answer "call rejected" or "retry later" while busy. Always retry those (see `mcp/server/src/mdh_mcp/cad_ops.py: com_retry`).
- In-process .NET code must touch the database only on AutoCAD's main thread, lock the document when not in a command (`LockDocument`), and use transactions. Never call AutoCAD APIs from a background/pipe thread; marshal work to the main thread first.
- Dynamic Block constraint parameters are evaluated when a command ends; read results in a later command.
- Batch scripts (`acad.exe /b`) never fire `Application.Idle`; LISP `(command)` wrappers share one undo group.
- Do not change persistent user settings (`LOGFILEMODE`, `LOGFILEPATH`, `SECURELOAD`, `TRUSTEDPATHS`) without restoring them.

## MCP server architecture

```text
Claude Code --stdio--> mdh_mcp.server (FastMCP from acad-cmd)
                        ├─ tool modules (extensions.py: ToolModule, run_tool envelope + audit log)
                        │    └─ CoreTools (tools_core.py): autocad_* tools
                        ├─ CadOperations (cad_ops.py): COM operations, safety guard, retries
                        └─ acad-cmd (external/acad-cmd, unmodified): COM attach/reconnect, logging, read-only tools
```

- `external/acad-cmd` is upstream (MIT), pinned to commit `63f2272`; **do not edit it**. Our differences are documented in `docs/mcp/upstream-notes.md`.
- MCP protocol code (tools_core, extensions) must not contain structural engineering logic. Future modules (rebar, ties, sections) call a CAD-facing service and return `run_tool` envelopes.

## Conventions

- Tool names: `autocad_<verb>_<noun>`; one tool = one focused, validated operation. No raw command/LISP passthrough (upstream raw tools are hidden unless `MDH_MCP_EXPOSE_RAW_TOOLS=1`).
- Every tool returns `{success, message, drawing, ..., error: {class, detail, action}, elapsed_ms}`. Modifying tools return the entity after the change (handle, entity_type, layer, properties).
- Write tools: only drawings under `MDH_MCP_WRITE_ROOTS` (default `mcp/tests/drawings`), never read-only drawings, one entity per call, one undo mark per call, never save. Deletes need `confirm=true`.
- Python: 3.10+, `mcp<2` (acad-cmd uses `mcp.server.fastmcp`, removed in mcp 2.x). Pinned versions: `mcp/server/constraints.txt`.

## Build / run / test

```powershell
.\mcp\install.ps1                                   # venv + deps + diagnostic
.\mcp\tests\Start-TestAutoCAD.ps1                   # AutoCAD 2021 with the test DWG
mcp\server\.venv\Scripts\python.exe -m mdh_mcp.diagnose
mcp\server\.venv\Scripts\python.exe -m unittest discover -s mcp\tests -p "test_*.py"   # no AutoCAD
mcp\server\.venv\Scripts\python.exe mcp\tests\smoke_test.py                           # real AutoCAD; restarts it in test 08
```

The add-in: see `docs/Build-and-Debug.md`.

## Testing and verification rules

- Never run write tools against a production drawing; use `mcp/tests/drawings/MDH_MCP_Test.dwg`.
- Report verified (executed, with evidence) separately from implemented-but-unverified. Do not mark unexecuted tests as passed.
- Smoke reports go to `mcp/tests/results/`; audit logs to `mcp/logs/logs/acad-cmd/<session>/session.jsonl`.
- Do not drive AutoCAD with simulated keystrokes (SendKeys): timing-flaky, and keys can land in other windows.

## Future modules (not implemented in the MCP server yet)

Rebar percentage, tie hook generator, column section modeler. The add-in already implements the first two and steel sections; exposing them to Claude Code is the next phase, via either COM-callable commands with structured results or an in-process .NET bridge (named pipe, cf. acad-cmd's AcadEventBridge). See `docs/mcp/architecture.md`.
