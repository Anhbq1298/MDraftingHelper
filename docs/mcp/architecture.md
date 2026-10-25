# AutoCAD MCP integration — architecture

```text
Claude Code
   │  MCP over stdio (JSON-RPC)
   ▼
mdh_mcp.server ── FastMCP instance created by acad-cmd (unmodified upstream)
   │
   ├─ Tool modules (mdh_mcp.extensions.ToolModule)
   │     CoreTools ............ autocad_* tools (Phase 1)
   │     [future] RebarTools, TieTools, SectionTools
   │        └─ run_tool(): envelope, error mapping, timing, JSONL audit
   │
   ├─ CAD-facing service
   │     CadOperations (COM) .. validation, write guard, undo marks, busy retries, fresh ActiveDocument
   │     [future] in-process bridge client (named pipe → .NET add-in)
   │
   └─ acad-cmd connection layer
         AutoCADBridge ........ COM attach by versioned ProgID, major pinning, reconnect
         SessionLogger ........ logs/acad-cmd/<session>/session.jsonl
   │  COM (out-of-process, marshalled to AutoCAD's UI thread)
   ▼
AutoCAD 2021 (R24.0) ── drawing database, entities, layers, blocks
   ▲
   └─ MDraftingHeper add-in (src/, .NET Framework 4.8): Rebar, Tie & Hook, Steel sections
```

## Boundaries

| Layer | Owns | Must not contain |
| --- | --- | --- |
| Tool modules | MCP tool schemas, docstrings, the response envelope | COM calls, engineering rules |
| CadOperations | Argument validation, the "test drawings only" write guard, one undo mark per call, retries | MCP types |
| acad-cmd | Process/COM discovery, reconnect, audit logging | Our safety policy |
| Add-in modules | Engineering geometry and rules (Core library) | MCP or transport code |

## Why COM first

- It works out of process: no AutoCAD restart is needed to update the server, and nothing is loaded into AutoCAD.
- AutoCAD performs document locking and main-thread marshalling for COM calls.
- Its limits: slower than in-process calls, no transactions spanning several calls, and no access to .NET-only APIs (dynamic block parameters through the managed API, Core geometry).

## Threading and documents

- Tool calls may run on FastMCP worker threads. Each COM call initialises COM on its thread (`CoInitialize`), and AutoCAD executes the call on its own UI thread.
- Every operation resolves `Application.ActiveDocument` again, so switching drawings is followed and a stale document is never written.
- AutoCAD's busy answers (`RPC_E_CALL_REJECTED`, `RPC_E_SERVERCALL_RETRYLATER`) are retried with backoff.
- If AutoCAD is gone, calls return `not_running` immediately. Once a new instance is up (attach is retried for up to 20 s while `acad.exe` is starting), the same server session reconnects.

## Safety model

1. **Write roots.** Modifying tools act only when the active drawing lives under `MDH_MCP_WRITE_ROOTS`. Unsaved, read-only and other drawings are refused with `drawing_not_allowed` or `read_only`.
2. **No save tool.** The server never saves, and `open_drawing` is upstream's read/open only.
3. **Scope.** One entity per call; deletes need `confirm=true`. Claude Code's per-tool permission prompt is a second approval layer.
4. **No raw command execution by default.** acad-cmd's `send_command`, `run_lisp`, `load_lisp_file` and dictionary writes are removed from the tool list unless `MDH_MCP_EXPOSE_RAW_TOOLS=1`.
5. **Audit.** Every call is logged with its arguments, success, error and elapsed time.

## Adding a tool or module

1. Put the CAD operation in a service method with no MCP types: validate inputs, call `_writable_doc()` for writes, wrap writes in `_undo_group`, and return plain dicts (handle, entity_type, layer, properties).
2. Register the tool in a module:

```python
class RebarTools:
    name = "rebar"
    def register(self, ctx):
        @ctx.mcp.tool()
        def autocad_rebar_percentage(boundary_handle: str, circle_handles: list[str]) -> dict:
            """... read-only ..."""
            return run_tool(ctx, "autocad_rebar_percentage", {...}, lambda: ctx.ops.rebar_percentage(...), "Computed.")
        return ["autocad_rebar_percentage"]
```

3. Add it in `server.build()` with `add_module(RebarTools())`.
4. Add a unit test (no AutoCAD) and a smoke step, run against the test drawing.

## Path to the MDraftingHelper modules

- **Rebar percentage.** Read-only. The calculation (Core `RebarCalculator`) is .NET, so either expose an add-in command that returns JSON (e.g. writing to an XRecord read back over COM), or add an in-process bridge.
- **Tie hook generator, column section modeler.** These write geometry and need .NET Core geometry, transactions and dynamic blocks. They are better served by an **in-process bridge**: a .NET 4.8 add-in hosting a named-pipe server whose requests are queued and executed on AutoCAD's main thread (a document-locked transaction per request). acad-cmd's `AcadEventBridge` is the reference for the pipe and its lifecycle. MCP tools would call it through a bridge client that implements the same service contract as `CadOperations`.
- Structural design checks stay out of the MCP layer in every case.
