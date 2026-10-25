"""Environment-driven settings. Defaults are repository-relative, never user-specific."""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import List

# mcp/server/src/mdh_mcp/settings.py -> repository root is four levels up.
REPO_ROOT = Path(__file__).resolve().parents[4]

# Upstream acad-cmd tools that execute arbitrary command-line text / AutoLISP or rewrite
# drawing dictionaries. Hidden unless MDH_MCP_EXPOSE_RAW_TOOLS=1.
RAW_TOOLS = (
    "send_command",
    "run_lisp",
    "load_lisp_file",
    "dict_xrecord_set",
    "dict_xrecord_delete",
    "dict_delete",
)


def _bool(name: str, default: bool) -> bool:
    raw = (os.environ.get(name) or "").strip().lower()
    if not raw:
        return default
    return raw in ("1", "true", "yes", "on")


def _paths(name: str, default: List[Path]) -> List[Path]:
    raw = (os.environ.get(name) or "").strip()
    if not raw:
        return default
    out = []
    for part in raw.split(os.pathsep):
        part = part.strip().strip('"')
        if part:
            p = Path(os.path.expandvars(os.path.expanduser(part)))
            out.append(p if p.is_absolute() else (REPO_ROOT / p))
    return out


@dataclass
class Settings:
    # Drawings under these folders may be modified by write tools; everything else is read-only.
    write_roots: List[Path] = field(default_factory=list)
    expose_raw_tools: bool = False
    log_root: Path = REPO_ROOT / "mcp" / "logs"

    @staticmethod
    def load() -> "Settings":
        return Settings(
            write_roots=[p.resolve() for p in _paths("MDH_MCP_WRITE_ROOTS", [REPO_ROOT / "mcp" / "tests" / "drawings"])],
            expose_raw_tools=_bool("MDH_MCP_EXPOSE_RAW_TOOLS", False),
            log_root=_paths("MDH_MCP_LOG_ROOT", [REPO_ROOT / "mcp" / "logs"])[0],
        )

    def is_writable_drawing(self, full_path: str) -> bool:
        if not full_path:
            return False
        try:
            p = Path(full_path).resolve()
        except OSError:
            return False
        for root in self.write_roots:
            try:
                p.relative_to(root)
                return True
            except ValueError:
                continue
        return False
