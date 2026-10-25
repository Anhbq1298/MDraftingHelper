"""AutoCAD operations over COM (AutoCAD.Application.24). No MCP types here.

Threading: AutoCAD's COM server runs in AutoCAD's UI thread (an STA). Out-of-process COM calls
are marshalled into that thread by COM itself and AutoCAD locks the document for each call, so
no AutoCAD database API is touched from this process's threads. Every call goes through
com_retry below, which backs off while AutoCAD reports "call rejected" or "retry later" (busy).

Document switching: every operation resolves Application.ActiveDocument afresh instead of
reusing a cached document object.
"""

from __future__ import annotations

import math
import time
from typing import Any, Dict, List, Optional, Sequence

import pythoncom
import pywintypes
import win32com.client

from acad_cmd.autocad_bridge import AutoCADBridge, _com_init, _tasklist_pids

from .settings import Settings

MAX_COORD = 1e9

# AutoCAD answers with either code while its UI thread is busy (e.g. just after startup).
# acad-cmd's com_retry only recognises the first, so this module uses its own retry.
RPC_E_CALL_REJECTED = -2147418111
RPC_E_SERVERCALL_RETRYLATER = -2147417846
BUSY_HRESULTS = {RPC_E_CALL_REJECTED, RPC_E_SERVERCALL_RETRYLATER}
CONNECT_WAIT_SEC = 20.0


def _hresult(e: Exception) -> Optional[int]:
    if isinstance(e, pywintypes.com_error):
        try:
            return int(e.hresult if getattr(e, "hresult", None) is not None else e.args[0])
        except (TypeError, ValueError, IndexError):
            return None
    return None


def com_retry(fn, *, retries: int = 40, base_delay: float = 0.05, max_delay: float = 1.0):
    _com_init()
    delay = base_delay
    for attempt in range(retries):
        try:
            return fn()
        except pywintypes.com_error as e:
            if _hresult(e) not in BUSY_HRESULTS or attempt == retries - 1:
                raise
        time.sleep(delay)
        delay = min(max_delay, delay * 1.6)


def _opt(fn, default=None):
    """Optional property read: missing or not-yet-available members yield the default."""
    try:
        return com_retry(fn)
    except (AttributeError, pywintypes.com_error):
        return default


class CadError(Exception):
    """An actionable failure. error_class is stable for clients; action tells the user what to do."""

    def __init__(self, error_class: str, message: str, action: Optional[str] = None):
        super().__init__(message)
        self.error_class = error_class
        self.action = action


def _point(value: Any, name: str) -> List[float]:
    if not isinstance(value, (list, tuple)) or len(value) not in (2, 3):
        raise CadError("invalid_argument", f"{name} must be [x, y] or [x, y, z].")
    pts = []
    for v in value:
        if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) or abs(v) > MAX_COORD:
            raise CadError("invalid_argument", f"{name} must contain finite numbers with |value| <= {MAX_COORD:g}.")
        pts.append(float(v))
    if len(pts) == 2:
        pts.append(0.0)
    return pts


def _variant(p: Sequence[float]):
    return win32com.client.VARIANT(pythoncom.VT_ARRAY | pythoncom.VT_R8, tuple(p))


def _r(v: Any, nd: int = 9) -> Any:
    return round(float(v), nd)


def _pt_out(v: Any) -> List[float]:
    return [_r(x) for x in tuple(v)]


class CadOperations:
    def __init__(self, bridge: AutoCADBridge, settings: Settings):
        self.bridge = bridge
        self.settings = settings

    # ------------------------------------------------------------------ connection

    def app(self):
        # Without a running acad.exe, acad-cmd's pinned-major path would Dispatch() a new AutoCAD and,
        # with AUTOCAD_MCP_ALLOW_NEW_INSTANCE=0, quit it again. Fail fast with an actionable error instead.
        if not _tasklist_pids("acad.exe"):
            self.bridge._connected = False
            raise CadError(
                "not_running",
                "AutoCAD is not running.",
                "Start AutoCAD 2021 (for tests: mcp/tests/Start-TestAutoCAD.ps1) and call the tool again; "
                "the server reconnects automatically.",
            )
        # acad.exe exists but may still be starting (not yet attachable via COM): keep trying briefly.
        deadline = time.monotonic() + CONNECT_WAIT_SEC
        while not self.bridge.ensure_connection() and time.monotonic() < deadline:
            time.sleep(1.0)
        if not self.bridge._connected:
            raise CadError(
                "not_connected",
                "AutoCAD 2021 is not running or not attachable (AutoCAD.Application.24).",
                "Start AutoCAD 2021 (for tests: mcp/tests/Start-TestAutoCAD.ps1) and call the tool again; "
                "the server reconnects automatically.",
            )
        return self.bridge._acad  # bridge exposes only the cached object; resolved fresh above

    def doc(self):
        app = self.app()
        try:
            return com_retry(lambda: app.ActiveDocument)
        except pywintypes.com_error:
            # The cached application died (AutoCAD restarted): force a reconnect once.
            self.bridge._connected = False
            app = self.app()
            return com_retry(lambda: app.ActiveDocument)

    @staticmethod
    def doc_label(doc) -> Dict[str, Any]:
        name = str(com_retry(lambda: doc.Name))
        full = str(com_retry(lambda: doc.FullName) or "")
        return {"name": name, "full_path": full or None}

    # ------------------------------------------------------------------ read

    def status(self) -> Dict[str, Any]:
        self.app()  # fast "not running" check before acad-cmd's snapshot (which may try to launch)
        snap = self.bridge.get_status_snapshot()
        if not snap.get("connected"):
            raise CadError(
                snap.get("error_class") or "not_connected",
                "AutoCAD 2021 is not running or not attachable (AutoCAD.Application.24).",
                "Start AutoCAD 2021 and retry; the server reconnects automatically.",
            )
        app = self.app()
        return {
            "acad_version": str(com_retry(lambda: app.Version)),
            "acadver": snap.get("acadver"),
            "major": snap.get("locked_major"),
            "pid": snap.get("acad_pid"),
            "busy": bool(snap.get("busy")),
            "command_active": snap.get("cmdactive"),
            "documents_open": int(com_retry(lambda: app.Documents.Count)),
            "drawing": snap.get("dwg"),
        }

    def active_document(self) -> Dict[str, Any]:
        doc = self.doc()
        info = self.doc_label(doc)
        read_only = bool(com_retry(lambda: doc.ReadOnly))
        info.update(
            {
                "saved": bool(com_retry(lambda: doc.Saved)),
                "read_only": read_only,
                "insunits": int(com_retry(lambda: doc.GetVariable("INSUNITS"))),
                "active_layer": str(com_retry(lambda: doc.ActiveLayer.Name)),
                "modelspace_count": int(com_retry(lambda: doc.ModelSpace.Count)),
                "writable_by_mcp": (not read_only) and self.settings.is_writable_drawing(info["full_path"] or ""),
                "write_roots": [str(p) for p in self.settings.write_roots],
            }
        )
        return info

    def list_layers(self) -> List[Dict[str, Any]]:
        doc = self.doc()
        layers = []
        for lay in com_retry(lambda: list(doc.Layers)):
            layers.append(
                {
                    "name": str(lay.Name),
                    "color": int(lay.color),
                    "on": bool(lay.LayerOn),
                    "frozen": bool(lay.Freeze),
                    "locked": bool(lay.Lock),
                    "linetype": str(lay.Linetype),
                    "lineweight": int(lay.Lineweight),
                }
            )
        return layers

    def entity(self, handle: str) -> Dict[str, Any]:
        doc = self.doc()
        return self._describe(self._by_handle(doc, handle))

    # ------------------------------------------------------------------ write (test drawings only)

    def _writable_doc(self):
        doc = self.doc()
        info = self.doc_label(doc)
        if bool(com_retry(lambda: doc.ReadOnly)):
            raise CadError("read_only", f"'{info['name']}' is open read-only.", "Open a writable test drawing.")
        if not self.settings.is_writable_drawing(info["full_path"] or ""):
            raise CadError(
                "drawing_not_allowed",
                f"Write tools only modify drawings under the configured test folders; active drawing is "
                f"'{info['full_path'] or info['name'] + ' (unsaved)'}'.",
                "Open the test drawing mcp/tests/drawings/MDH_MCP_Test.dwg (or a copy in a folder listed in "
                "MDH_MCP_WRITE_ROOTS) and retry.",
            )
        return doc

    def _undo_group(self, doc, fn):
        """One AutoCAD undo step per tool call."""
        com_retry(lambda: doc.StartUndoMark())
        try:
            return fn()
        finally:
            try:
                com_retry(lambda: doc.EndUndoMark())
            except Exception:
                pass

    def _layer_exists(self, doc, name: str) -> bool:
        try:
            com_retry(lambda: doc.Layers.Item(name))
            return True
        except pywintypes.com_error:
            return False

    def _check_layer(self, doc, layer: str) -> None:
        if not isinstance(layer, str) or not layer.strip():
            raise CadError("invalid_argument", "layer must be a non-empty string.")
        if not self._layer_exists(doc, layer):
            raise CadError("layer_not_found", f"Layer '{layer}' does not exist.", "Use autocad_list_layers to pick an existing layer.")
        lay = com_retry(lambda: doc.Layers.Item(layer))
        if bool(lay.Lock):
            raise CadError("layer_locked", f"Layer '{layer}' is locked.", "Unlock it in AutoCAD or use another layer.")

    def create_line(self, start: Any, end: Any, layer: str = "0") -> Dict[str, Any]:
        p1, p2 = _point(start, "start"), _point(end, "end")
        if math.dist(p1, p2) < 1e-9:
            raise CadError("invalid_argument", "start and end must differ.")
        doc = self._writable_doc()
        self._check_layer(doc, layer)

        def _do():
            line = com_retry(lambda: doc.ModelSpace.AddLine(_variant(p1), _variant(p2)))
            com_retry(lambda: setattr(line, "Layer", layer))
            return line

        return self._describe(self._undo_group(doc, _do))

    def update_entity(self, handle: str, properties: Dict[str, Any]) -> Dict[str, Any]:
        allowed = {"start", "end", "layer", "color", "move_by"}
        if not isinstance(properties, dict) or not properties:
            raise CadError("invalid_argument", "properties must be a non-empty object.")
        unknown = set(properties) - allowed
        if unknown:
            raise CadError("invalid_argument", f"Unsupported properties: {sorted(unknown)}. Allowed: {sorted(allowed)}.")
        doc = self._writable_doc()
        ent = self._by_handle(doc, handle)
        kind = str(com_retry(lambda: ent.ObjectName))
        if ("start" in properties or "end" in properties) and kind != "AcDbLine":
            raise CadError("invalid_argument", f"start/end apply to lines only; entity is {kind}.")
        new_start = _point(properties["start"], "start") if "start" in properties else None
        new_end = _point(properties["end"], "end") if "end" in properties else None
        move = _point(properties["move_by"], "move_by") if "move_by" in properties else None
        color = properties.get("color")
        if color is not None and (isinstance(color, bool) or not isinstance(color, int) or not 0 <= color <= 256):
            raise CadError("invalid_argument", "color must be an AutoCAD Color Index 0..256 (256 = ByLayer, 0 = ByBlock).")
        if "layer" in properties:
            self._check_layer(doc, properties["layer"])
        lay = com_retry(lambda: doc.Layers.Item(str(com_retry(lambda: ent.Layer))))
        if bool(lay.Lock):
            raise CadError("layer_locked", "The entity is on a locked layer.", "Unlock the layer in AutoCAD and retry.")

        def _do():
            if new_start is not None:
                com_retry(lambda: setattr(ent, "StartPoint", _variant(new_start)))
            if new_end is not None:
                com_retry(lambda: setattr(ent, "EndPoint", _variant(new_end)))
            if move is not None:
                com_retry(lambda: ent.Move(_variant([0.0, 0.0, 0.0]), _variant(move)))
            if "layer" in properties:
                com_retry(lambda: setattr(ent, "Layer", properties["layer"]))
            if color is not None:
                com_retry(lambda: setattr(ent, "color", color))

        self._undo_group(doc, _do)
        return self._describe(ent)

    def delete_entity(self, handle: str) -> Dict[str, Any]:
        doc = self._writable_doc()
        ent = self._by_handle(doc, handle)
        before = self._describe(ent)
        lay = com_retry(lambda: doc.Layers.Item(before["layer"]))
        if bool(lay.Lock):
            raise CadError("layer_locked", "The entity is on a locked layer.", "Unlock the layer in AutoCAD and retry.")
        self._undo_group(doc, lambda: com_retry(lambda: ent.Delete()))
        still_there = True
        try:
            self._by_handle(doc, handle)
        except CadError:
            still_there = False
        return {"deleted": not still_there, "entity_before_delete": before}

    # ------------------------------------------------------------------ helpers

    def _by_handle(self, doc, handle: str):
        if not isinstance(handle, str) or not handle.strip() or not all(c in "0123456789abcdefABCDEF" for c in handle.strip()):
            raise CadError("invalid_argument", "handle must be a hexadecimal entity handle string.")
        try:
            return com_retry(lambda: doc.HandleToObject(handle.strip().upper()))
        except pywintypes.com_error:
            raise CadError("not_found", f"No entity with handle {handle} in the active drawing (erased or wrong drawing).")

    def _describe(self, ent) -> Dict[str, Any]:
        # Handle, type and layer are required; display properties are read tolerantly so an entity that
        # was created is always reported with its handle even if a property lookup fails transiently.
        kind = str(com_retry(lambda: ent.ObjectName))
        props: Dict[str, Any] = {
            "color": _opt(lambda: int(ent.color)),
            "linetype": _opt(lambda: str(ent.Linetype)),
            "lineweight": _opt(lambda: int(ent.Lineweight)),
        }
        if kind == "AcDbLine":
            s = _pt_out(com_retry(lambda: ent.StartPoint))
            e = _pt_out(com_retry(lambda: ent.EndPoint))
            props.update({"start": s, "end": e, "length": _r(math.dist(s, e)),
                          "angle_deg": _r(math.degrees(math.atan2(e[1] - s[1], e[0] - s[0])) % 360)})
        else:
            try:
                mn, mx = com_retry(lambda: ent.GetBoundingBox())
                props["bbox"] = [_pt_out(mn), _pt_out(mx)]
            except Exception:
                pass
        return {
            "handle": str(com_retry(lambda: ent.Handle)),
            "entity_type": kind,
            "layer": str(com_retry(lambda: ent.Layer)),
            "properties": props,
        }
