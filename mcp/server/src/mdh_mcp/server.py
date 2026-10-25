"""stdio entry point: acad-cmd's FastMCP server plus the MDraftingHelper tool modules.

Upstream acad-cmd is imported unmodified. Its log directory is derived from the working directory at
import time, so the working directory is set to the configured log root first.
"""

from __future__ import annotations

import os
import sys

from .settings import RAW_TOOLS, Settings


def build():
    settings = Settings.load()
    settings.log_root.mkdir(parents=True, exist_ok=True)
    os.chdir(settings.log_root)  # acad-cmd writes logs/acad-cmd/<session>/ relative to CWD

    from acad_cmd import tools as upstream  # noqa: E402  (after chdir on purpose)

    from .cad_ops import CadOperations
    from .extensions import ToolContext, add_module, register_all
    from .tools_core import CoreTools

    if not settings.expose_raw_tools:
        for name in RAW_TOOLS:
            try:
                upstream.mcp.remove_tool(name)
            except Exception:
                pass

    ctx = ToolContext(mcp=upstream.mcp, ops=CadOperations(upstream.state.bridge, settings),
                      settings=settings, audit=upstream.state.audit)
    add_module(CoreTools())
    registered = register_all(ctx)
    upstream.state.audit.log("mdh_server_start", {
        "modules": registered,
        "raw_tools_exposed": settings.expose_raw_tools,
        "write_roots": [str(p) for p in settings.write_roots],
        "target_major": os.environ.get("AUTOCAD_MCP_TARGET_MAJOR"),
        "allow_new_instance": os.environ.get("AUTOCAD_MCP_ALLOW_NEW_INSTANCE"),
    })
    return upstream.mcp, ctx


def main() -> None:
    mcp, _ = build()
    print("mdh-mcp: AutoCAD MCP server running on stdio", file=sys.stderr, flush=True)
    mcp.run()


if __name__ == "__main__":
    main()
