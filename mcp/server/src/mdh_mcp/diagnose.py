"""Connection diagnostic (read-only): python -m mdh_mcp.diagnose

Checks the Python/package environment, the AutoCAD 2021 COM registration and running processes,
then attaches the same way the server does and reports status, active drawing and layer count.
Exit code 0 = connected, 1 = AutoCAD not reachable, 2 = environment problem.
"""

from __future__ import annotations

import json
import os
import sys
import winreg


def _line(ok: bool, label: str, detail: str = "") -> None:
    print(f"[{'OK' if ok else '--'}] {label}{(': ' + detail) if detail else ''}")


def main() -> int:
    print(f"python {sys.version.split()[0]} at {sys.executable}")
    try:
        import mcp  # noqa: F401
        from importlib.metadata import version
        mv = version("mcp")
        _line(mv.startswith("1."), "mcp package", mv + ("" if mv.startswith("1.") else " (acad-cmd needs mcp<2)"))
        _line(True, "acad-cmd package", version("acad-cmd"))
        _line(True, "pywin32", version("pywin32"))
    except Exception as e:
        _line(False, "packages", str(e))
        return 2

    try:
        with winreg.OpenKey(winreg.HKEY_CLASSES_ROOT, r"AutoCAD.Application.24\CLSID") as k:
            _line(True, "COM ProgID AutoCAD.Application.24 registered", winreg.QueryValueEx(k, "")[0])
    except OSError:
        _line(False, "COM ProgID AutoCAD.Application.24 registered", "not found - is AutoCAD 2021 installed?")
        return 2

    print("env: AUTOCAD_MCP_TARGET_MAJOR=%s AUTOCAD_MCP_ALLOW_NEW_INSTANCE=%s" % (
        os.environ.get("AUTOCAD_MCP_TARGET_MAJOR"), os.environ.get("AUTOCAD_MCP_ALLOW_NEW_INSTANCE")))

    os.environ.setdefault("AUTOCAD_MCP_TARGET_MAJOR", "24")
    os.environ.setdefault("AUTOCAD_MCP_ALLOW_NEW_INSTANCE", "0")
    from acad_cmd.autocad_bridge import AutoCADBridge, _tasklist_pids

    pids = _tasklist_pids("acad.exe")
    _line(bool(pids), "acad.exe processes", ", ".join(map(str, pids)) or "none running")
    if not pids:
        print("AutoCAD is not running. Start AutoCAD 2021 (mcp/tests/Start-TestAutoCAD.ps1 for the test drawing).")
        return 1

    from .cad_ops import CadError, CadOperations
    from .settings import Settings
    ops = CadOperations(AutoCADBridge(), Settings.load())
    try:
        status = ops.status()
        doc = ops.active_document()
        layers = ops.list_layers()
    except CadError as e:
        _line(False, "attach", f"{e} {e.action or ''}")
        return 1
    _line(True, "attached", json.dumps(status))
    _line(True, "active drawing", f"{doc['full_path'] or doc['name']} (writable by MCP: {doc['writable_by_mcp']})")
    _line(True, "layers", str(len(layers)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
