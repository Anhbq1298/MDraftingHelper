"""End-to-end smoke tests through the real MCP stdio server and a real AutoCAD 2021.

    mcp\\server\\.venv\\Scripts\\python.exe mcp\\tests\\smoke_test.py [--skip-restart]

Starts AutoCAD with the test drawing if needed (Start-TestAutoCAD.ps1), spawns `python -m mdh_mcp.server`
over stdio exactly as Claude Code does, and runs tests 01-08 plus safety checks via MCP tool calls.
Only mcp/tests/drawings/MDH_MCP_Test.dwg is modified (in memory; never saved). Test 08 quits and
restarts AutoCAD and is BLOCKED when any other drawing is open, so user work is never closed.
Writes a JSON report to mcp/tests/results/.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import subprocess
import sys
import time
from pathlib import Path
from typing import Any, Dict, List

import anyio
from mcp.client.session import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
TEST_DWG = (HERE / "drawings" / "MDH_MCP_Test.dwg").resolve()
PY = Path(sys.executable)

results: List[Dict[str, Any]] = []


def record(test_id: str, name: str, status: str, detail: Any = "") -> None:
    results.append({"id": test_id, "name": name, "status": status, "detail": detail})
    print(f"{status:7} {test_id} {name}" + (f" :: {detail}" if detail and status != "PASS" else ""), flush=True)


def start_autocad() -> None:
    script = HERE / "Start-TestAutoCAD.ps1"
    r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script)],
                       capture_output=True, text=True, timeout=300)
    if r.returncode != 0:
        raise RuntimeError(f"Start-TestAutoCAD failed: {r.stdout}\n{r.stderr}")


def com_app():
    import pythoncom
    import win32com.client
    pythoncom.CoInitialize()
    return win32com.client.GetActiveObject("AutoCAD.Application.24")


def payload(res) -> Dict[str, Any]:
    data = res.structuredContent
    if data is None:
        for item in res.content or []:
            txt = getattr(item, "text", None)
            if isinstance(txt, str):
                try:
                    data = json.loads(txt)
                    break
                except ValueError:
                    continue
    if isinstance(data, dict) and isinstance(data.get("result"), dict) and "success" not in data:
        data = data["result"]
    if not isinstance(data, dict):
        raise RuntimeError(f"Unexpected tool result: {res.content}")
    return data


async def call(session: ClientSession, tool: str, args: Dict[str, Any] | None = None, timeout: float = 60) -> Dict[str, Any]:
    with anyio.fail_after(timeout):
        return payload(await session.call_tool(tool, args or {}))


async def run(skip_restart: bool) -> None:
    dwg_mtime = TEST_DWG.stat().st_mtime
    start_autocad()

    env = {**os.environ, "AUTOCAD_MCP_TARGET_MAJOR": "24", "AUTOCAD_MCP_ALLOW_NEW_INSTANCE": "0"}
    params = StdioServerParameters(command=str(PY), args=["-m", "mdh_mcp.server"], env=env, cwd=str(REPO))
    err_log = REPO / "mcp" / "logs" / "smoke_server_stderr.log"
    err_log.parent.mkdir(parents=True, exist_ok=True)

    with err_log.open("w", encoding="utf-8", errors="replace") as err_f:
        async with stdio_client(params, errlog=err_f) as (r, w):
            async with ClientSession(r, w) as session:
                with anyio.fail_after(60):
                    await session.initialize()
                    tools = {t.name for t in (await session.list_tools()).tools}
                wanted = {"autocad_get_status", "autocad_get_active_document", "autocad_list_layers", "autocad_create_line",
                          "autocad_get_entity", "autocad_update_entity", "autocad_delete_entity"}
                hidden = {"send_command", "run_lisp", "load_lisp_file"} & tools
                record("00", "MCP discovery: focused tools listed, raw tools hidden",
                       "PASS" if wanted <= tools and not hidden else "FAIL",
                       {"missing": sorted(wanted - tools), "raw_exposed": sorted(hidden)})

                st = await call(session, "autocad_get_status")
                ok = st.get("success") and st["status"].get("major") == 24
                record("01", "Connection: running AutoCAD 2021 detected", "PASS" if ok else "FAIL", st)

                doc = await call(session, "autocad_get_active_document")
                d = doc.get("document") or {}
                ok = doc.get("success") and Path(d.get("full_path") or "").resolve() == TEST_DWG and d.get("writable_by_mcp")
                record("02", "Drawing: active test drawing identified and writable by MCP", "PASS" if ok else "FAIL", doc)

                lay = await call(session, "autocad_list_layers")
                names = {l["name"] for l in lay.get("layers", [])}
                record("03", "Layers: read", "PASS" if lay.get("success") and {"0", "MDH_MCP_TEST"} <= names else "FAIL",
                       sorted(names))

                cr = await call(session, "autocad_create_line", {"start": [0, 0], "end": [100, 50], "layer": "MDH_MCP_TEST"})
                handle = cr.get("handle")
                p = cr.get("properties", {})
                ok = cr.get("success") and handle and p.get("start") == [0, 0, 0] and p.get("end") == [100, 50, 0]
                record("04", "Geometry: test line created", "PASS" if ok else "FAIL", cr)

                ge = await call(session, "autocad_get_entity", {"handle": handle or "0"})
                gp = ge.get("properties", {})
                ok = (ge.get("success") and ge.get("entity_type") == "AcDbLine" and ge.get("layer") == "MDH_MCP_TEST"
                      and abs(gp.get("length", 0) - (100 ** 2 + 50 ** 2) ** 0.5) < 1e-6)
                record("05", "Entity query: line properties read back", "PASS" if ok else "FAIL", ge)

                up = await call(session, "autocad_update_entity",
                                {"handle": handle or "0", "properties": {"move_by": [10, 20], "color": 1}})
                up2 = await call(session, "autocad_update_entity",
                                 {"handle": handle or "0", "properties": {"end": [210, 70]}})
                ge2 = await call(session, "autocad_get_entity", {"handle": handle or "0"})
                q = ge2.get("properties", {})
                ok = (up.get("success") and up2.get("success") and q.get("start") == [10, 20, 0]
                      and q.get("end") == [210, 70, 0] and q.get("color") == 1)
                record("06", "Modification: line moved, end point and color changed, verified by re-read",
                       "PASS" if ok else "FAIL", {"move": up, "end": up2, "reread": ge2})

                bad = await call(session, "autocad_delete_entity", {"handle": handle or "0"})
                record("S1", "Safety: delete without confirm is refused",
                       "PASS" if not bad.get("success") and bad["error"]["class"] == "confirmation_required" else "FAIL", bad)
                de = await call(session, "autocad_delete_entity", {"handle": handle or "0", "confirm": True})
                gone = await call(session, "autocad_get_entity", {"handle": handle or "0"})
                ok = de.get("success") and de.get("deleted") and not gone.get("success") and gone["error"]["class"] == "not_found"
                record("07", "Cleanup: test line deleted and verified gone", "PASS" if ok else "FAIL", {"delete": de, "reread": gone})

                # Safety: a drawing outside the test folder is read-only for MCP (also exercises document switching).
                app = com_app()
                scratch = app.Documents.Add()
                time.sleep(1)
                try:
                    refused = await call(session, "autocad_create_line", {"start": [0, 0], "end": [1, 1]})
                    ad = await call(session, "autocad_get_active_document")
                    ok = (not refused.get("success") and refused["error"]["class"] == "drawing_not_allowed"
                          and not ad["document"]["writable_by_mcp"])
                    record("S2", "Safety: write refused in a non-test drawing; active document switch followed",
                           "PASS" if ok else "FAIL", {"create": refused, "doc": ad.get("document")})
                finally:
                    scratch.Close(False)
                    time.sleep(1)

                if skip_restart:
                    record("08", "Recovery: reconnect after AutoCAD restart", "SKIPPED", "--skip-restart")
                else:
                    await recovery(session)

    record("S3", "Safety: test drawing file never saved by MCP",
           "PASS" if TEST_DWG.stat().st_mtime == dwg_mtime else "FAIL", str(TEST_DWG))


async def recovery(session: ClientSession) -> None:
    app = com_app()
    docs = list(app.Documents)
    others = [d.FullName for d in docs if Path(d.FullName or "x").resolve() != TEST_DWG and not d.Saved]
    if others:
        record("08", "Recovery: reconnect after AutoCAD restart", "BLOCKED",
               f"Other modified drawings are open {others}; not closing user work.")
        return
    for d in docs:
        d.Close(False)  # discard in-memory test changes; nothing is saved
    app.Quit()
    del app
    for _ in range(60):
        if not subprocess.run(["tasklist", "/FI", "IMAGENAME eq acad.exe", "/NH"], capture_output=True, text=True).stdout.count("acad.exe"):
            break
        time.sleep(1)
    down = await call(session, "autocad_get_status")
    down_ok = not down.get("success") and down["error"]["class"] in ("not_running", "not_connected", "disconnected")
    start_autocad()
    up = await call(session, "autocad_get_status", timeout=120)
    cr = await call(session, "autocad_create_line", {"start": [0, 0], "end": [10, 0], "layer": "MDH_MCP_TEST"})
    de = await call(session, "autocad_delete_entity", {"handle": cr.get("handle") or "0", "confirm": True})
    ok = down_ok and up.get("success") and cr.get("success") and de.get("success")
    record("08", "Recovery: actionable error while down; same server session reconnects after restart and can write",
           "PASS" if ok else "FAIL", {"while_down": down, "after_restart": up.get("status"), "write": cr.get("success")})


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--skip-restart", action="store_true")
    a = ap.parse_args()
    t0 = time.time()
    try:
        anyio.run(run, a.skip_restart)
    except Exception as e:
        record("XX", "Harness error", "FAIL", f"{type(e).__name__}: {e}")
    out_dir = HERE / "results"
    out_dir.mkdir(exist_ok=True)
    stamp = dt.datetime.now().strftime("%Y%m%d_%H%M%S")
    report = {"timestamp": stamp, "duration_s": round(time.time() - t0, 1), "results": results}
    (out_dir / f"smoke_{stamp}.json").write_text(json.dumps(report, indent=2, default=str), encoding="utf-8")
    counts = {s: sum(1 for r in results if r["status"] == s) for s in ("PASS", "FAIL", "BLOCKED", "SKIPPED")}
    print(f"\nSummary: {counts}  report: mcp/tests/results/smoke_{stamp}.json")
    return 0 if counts["FAIL"] == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
