"""Extension boundary for MCP tool modules.

A tool module groups related MCP tools (core CAD tools today; MDraftingHelper modules such as
rebar, ties and sections later). Modules receive a ToolContext and register tools on the shared
FastMCP server. They must not contain structural design logic in the protocol layer: modules call
into a CAD-facing service (COM CadOperations today, an in-process .NET bridge later) and return the
envelope produced by `run_tool`.
"""

from __future__ import annotations

import time
import traceback
from dataclasses import dataclass
from typing import Any, Callable, Dict, List, Optional, Protocol

from .cad_ops import CadError, CadOperations
from .settings import Settings


@dataclass
class ToolContext:
    mcp: Any                 # FastMCP server shared with acad-cmd
    ops: CadOperations       # CAD-facing service (COM implementation)
    settings: Settings
    audit: Any               # acad_cmd SessionLogger (JSONL)


class ToolModule(Protocol):
    name: str

    def register(self, ctx: ToolContext) -> List[str]:
        """Register tools on ctx.mcp; return the tool names registered."""


_modules: List[ToolModule] = []


def add_module(module: ToolModule) -> None:
    if any(m.name == module.name for m in _modules):
        raise ValueError(f"Tool module '{module.name}' registered twice")
    _modules.append(module)


def register_all(ctx: ToolContext) -> Dict[str, List[str]]:
    return {m.name: m.register(ctx) for m in _modules}


def run_tool(ctx: ToolContext, tool: str, args: Dict[str, Any], fn: Callable[[], Dict[str, Any]],
             message: str, *, modifies: bool = False) -> Dict[str, Any]:
    """Runs fn and wraps it in the standard envelope; logs request, result, timing and errors."""
    t0 = time.perf_counter()
    drawing: Optional[str] = None
    try:
        data = fn()
        drawing = _drawing(ctx)
        out = {"success": True, "message": message, "drawing": drawing, **data}
    except CadError as e:
        drawing = _drawing(ctx)
        out = {"success": False, "message": str(e), "drawing": drawing,
               "error": {"class": e.error_class, "detail": str(e), "action": e.action}}
    except Exception as e:  # tool boundary: report, never crash the server
        drawing = _drawing(ctx)
        out = {"success": False, "message": f"Unexpected {type(e).__name__}: {e}", "drawing": drawing,
               "error": {"class": "unexpected", "detail": traceback.format_exc(limit=3),
                         "action": "See mcp/logs for the audit log; retry after checking AutoCAD is idle."}}
    out["elapsed_ms"] = round((time.perf_counter() - t0) * 1000, 1)
    try:
        ctx.audit.log(tool, {"args": args, "modifies": modifies, "success": out["success"],
                             "error": out.get("error"), "elapsed_ms": out["elapsed_ms"],
                             "handle": out.get("handle")}, dwg=drawing)
    except Exception:
        pass
    return out


def _drawing(ctx: ToolContext) -> Optional[str]:
    try:
        return ctx.ops.doc_label(ctx.ops.doc())["full_path"] or ctx.ops.doc_label(ctx.ops.doc())["name"]
    except Exception:
        return None
