"""Core AutoCAD tools (Phase 1): status, document, layers, and one-entity line operations."""

from __future__ import annotations

from typing import Any, Dict, List, Optional

from .extensions import ToolContext, run_tool


class CoreTools:
    name = "core"

    def register(self, ctx: ToolContext) -> List[str]:
        mcp, ops = ctx.mcp, ctx.ops

        @mcp.tool()
        def autocad_get_status() -> Dict[str, Any]:
            """Connection status of the AutoCAD 2021 session (version, PID, busy state, active drawing). Read-only."""
            return run_tool(ctx, "autocad_get_status", {}, lambda: {"status": ops.status()},
                            "Connected to AutoCAD.")

        @mcp.tool()
        def autocad_get_active_document() -> Dict[str, Any]:
            """Active drawing: name, full path, saved/read-only state, units, and whether MCP write tools may modify it. Read-only."""
            return run_tool(ctx, "autocad_get_active_document", {}, lambda: {"document": ops.active_document()},
                            "Active document read.")

        @mcp.tool()
        def autocad_list_layers() -> Dict[str, Any]:
            """All layers of the active drawing with color, on/frozen/locked state, linetype and lineweight. Read-only."""
            def _do():
                layers = ops.list_layers()
                return {"count": len(layers), "layers": layers}
            return run_tool(ctx, "autocad_list_layers", {}, _do, "Layers read.")

        @mcp.tool()
        def autocad_create_line(start: List[float], end: List[float], layer: str = "0") -> Dict[str, Any]:
            """Create ONE line in model space of the active TEST drawing. start/end are [x, y] or [x, y, z] in
            drawing units; layer must already exist and be unlocked. Refused outside the configured test folders.
            Returns the new handle and its properties. One AutoCAD undo step; the drawing is not saved."""
            return run_tool(ctx, "autocad_create_line", {"start": start, "end": end, "layer": layer},
                            lambda: ops.create_line(start, end, layer), "Line created.", modifies=True)

        @mcp.tool()
        def autocad_get_entity(handle: str) -> Dict[str, Any]:
            """Read one entity of the active drawing by handle: type, layer, and properties
            (lines: start, end, length, angle). Read-only."""
            return run_tool(ctx, "autocad_get_entity", {"handle": handle}, lambda: ops.entity(handle),
                            "Entity read.")

        @mcp.tool()
        def autocad_update_entity(handle: str, properties: Dict[str, Any]) -> Dict[str, Any]:
            """Modify ONE entity in the active TEST drawing. Allowed properties: start/end ([x,y(,z)], lines only),
            move_by ([dx,dy(,dz)]), layer (existing, unlocked), color (ACI 0..256). Returns the entity after the
            change. One AutoCAD undo step; the drawing is not saved."""
            return run_tool(ctx, "autocad_update_entity", {"handle": handle, "properties": properties},
                            lambda: ops.update_entity(handle, properties), "Entity updated.", modifies=True)

        @mcp.tool()
        def autocad_delete_entity(handle: str, confirm: bool = False) -> Dict[str, Any]:
            """Delete ONE entity by handle in the active TEST drawing. Destructive: requires confirm=true.
            Returns the entity as it was and verifies it no longer exists. One AutoCAD undo step; not saved."""
            def _do():
                if confirm is not True:
                    from .cad_ops import CadError
                    raise CadError("confirmation_required", "Deleting requires confirm=true.",
                                   "Call again with confirm=true after the user approves deleting this entity.")
                return ops.delete_entity(handle)
            return run_tool(ctx, "autocad_delete_entity", {"handle": handle, "confirm": confirm}, _do,
                            "Entity deleted.", modifies=True)

        return ["autocad_get_status", "autocad_get_active_document", "autocad_list_layers", "autocad_create_line",
                "autocad_get_entity", "autocad_update_entity", "autocad_delete_entity"]
