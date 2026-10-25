"""Convert openly licensed steel section datasets into MDraftingHeper profile JSON (schema 1).

    python tools/Profiles/convert_open_profiles.py

Inputs (clone with tools/Profiles/Fetch-OpenProfileSources.ps1, pinned commits):
  external/steel-sections            p-horne/steel-sections           MPL-2.0  -> UB, UC
  external/Steel_Section_Database    ulmewix/Steel_Section_Database   MIT      -> IPE, HEA, HEB, HEM, CHS, SHS, RHS
Outputs (src/MDraftingHeper.Data/Profiles):
  open-uk-ub-uc.json, open-en-ipe-he.json, open-en-hollow.json

Deterministic: the same inputs always produce byte-identical files. Standard library only.
"""

from __future__ import annotations

import csv
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
EXT = REPO / "external"
OUT = REPO / "src" / "MDraftingHeper.Data" / "Profiles"

PHORNE = {"repo": "https://github.com/p-horne/steel-sections",
          "commit": "7c5864feab6f9f0c7f8294dcf1f37a00ec7d635b", "license": "MPL-2.0"}
ULMEWIX = {"repo": "https://github.com/ulmewix/Steel_Section_Database",
           "commit": "524172a3fff6a2c4da03f56a577402aa3a88876a", "license": "MIT"}


def num(s: str) -> float:
    v = float(s)
    return int(v) if v.is_integer() else v


def slug(text: str) -> str:
    return re.sub(r"[^a-z0-9.]+", "-", text.lower()).strip("-")


def toml_value(path: Path, key: str) -> str:
    m = re.search(rf'^{key}\s*=\s*"([^"]*)"', path.read_text(encoding="utf-8"), re.M)
    if not m:
        raise SystemExit(f"{path}: missing '{key}'")
    return m.group(1)


def write(name: str, source: dict, notes: str, profiles: list) -> None:
    doc = {
        "schemaVersion": 1,
        "source": source["name"],
        "sourceVersion": f"{source['repo']} @ {source['commit'][:7]} ({source['license']})",
        "isDemonstration": False,
        "units": "mm",
        "notes": notes,
        "profiles": profiles,
    }
    text = json.dumps(doc, indent=1, ensure_ascii=False) + "\n"
    (OUT / name).write_text(text, encoding="utf-8", newline="\n")
    print(f"{name}: {len(profiles)} profiles")


def ub_uc() -> None:
    src = EXT / "steel-sections" / "steelsections" / "data" / "uk_sections.csv"
    profiles = []
    with src.open(encoding="utf-8", newline="") as f:
        for row in csv.DictReader(f):
            if row["shape"] != "I":
                continue  # hollow sections come from Steel_Section_Database
            family = row["name"].split()[0]
            if family not in ("UB", "UC"):
                raise SystemExit(f"Unexpected I-section designation {row['name']}")
            profiles.append({
                "id": "uk-" + slug(row["name"]),
                "family": family,
                "standard": "BS 4-1 (designation; upstream does not name its catalogue)",
                "series": family,
                "name": row["name"],
                "dimensions": {"H": num(row["depth"]), "B": num(row["breadth"]), "tw": num(row["web_thickness"]),
                               "tf": num(row["flange_thickness"]), "r": num(row["corner_radius"])},
                "cornerRadiusStatus": "published",
            })
    write("open-uk-ub-uc.json", {**PHORNE, "name": "p-horne/steel-sections uk_sections.csv"},
          "UB/UC geometry (H, B, tw, tf, root radius r) converted unchanged from steel-sections, licensed MPL-2.0 "
          "(this file is Covered Software under MPL-2.0; see THIRD-PARTY-NOTICES.md). The upstream project does not "
          "state which catalogue the values come from; check critical sizes against the manufacturer's current data.",
          profiles)


def series_rows(rel: str):
    base = EXT / "Steel_Section_Database" / "data"
    toml = base / f"{rel}.toml"
    with (base / f"{rel}.csv").open(encoding="utf-8", newline="") as f:
        return toml, list(csv.DictReader(f))


def ipe_he() -> None:
    profiles = []
    for rel in ("ipe/IPE", "hea/HEA", "heb/HEB", "hem/HEM"):
        toml, rows = series_rows(rel)
        series, standard = toml_value(toml, "id"), toml_value(toml, "standard")
        for row in rows:
            profiles.append({
                "id": "en-" + slug(row["designation"]),
                "family": "IH",
                "standard": standard,
                "series": series,
                "name": row["designation"],
                "dimensions": {"H": num(row["h"]), "B": num(row["b"]), "tw": num(row["tw"]),
                               "tf": num(row["tf"]), "r": num(row["r"])},
                "cornerRadiusStatus": "published",
            })
    write("open-en-ipe-he.json", {**ULMEWIX, "name": "ulmewix/Steel_Section_Database (IPE, HEA, HEB, HEM)"},
          "Parallel-flange European I/H sections converted unchanged from Steel_Section_Database (MIT). Upstream: "
          "nominal dimensions per EN 10365:2017, not re-checked against EN 10365:2026.", profiles)


def corner_rule(rule: str, t: float) -> float:
    if rule == "EN10210-2":
        return 1.5 * t
    if rule == "EN10219-2":
        return (2.0 if t <= 6 else 2.5 if t <= 10 else 3.0) * t
    raise SystemExit(f"Unknown corner radius rule {rule}")


def hollow() -> None:
    profiles = []
    for rel in ("chs/CHS-HF", "chs/CHS-CF", "shs/SHS-HF", "shs/SHS-CF", "rhs/RHS-HF", "rhs/RHS-CF"):
        toml, rows = series_rows(rel)
        series, standard = toml_value(toml, "id"), toml_value(toml, "standard")
        process = "HF" if series.endswith("-HF") else "CF"
        family = series.split("-")[0]
        rule = None if family == "CHS" else toml_value(toml, "corner_radii")
        for row in rows:
            t = num(row["t"])
            p = {
                "id": "en-" + slug(f"{row['designation']} {process}"),
                "family": family,
                "standard": standard,
                "series": series,
                "name": f"{row['designation']} {process}",
            }
            if family == "CHS":
                p["dimensions"] = {"D": num(row["D"]), "t": t}
            else:
                explicit = (row.get("ro") or "").strip()
                ro = num(explicit) if explicit else round(corner_rule(rule, t), 6)
                dims = {"B": num(row["b"]), "t": t, "r": num(ro) if not isinstance(ro, int) else ro}
                if family == "RHS":
                    dims = {"H": num(row["h"]), **dims}
                p["dimensions"] = dims
                p["cornerRadiusConvention"] = "outer"
                # Rule-based radii are the standards' calculation convention, not mill-specific values.
                p["cornerRadiusStatus"] = "published" if explicit else "assumed"
            profiles.append(p)
    write("open-en-hollow.json", {**ULMEWIX, "name": "ulmewix/Steel_Section_Database (CHS, SHS, RHS)"},
          "Hollow sections converted from Steel_Section_Database (MIT): hot finished (HF) per EN 10210-2:2006 and cold "
          "formed (CF) per EN 10219-2:2006. Empty upstream corner radii are filled with the standards' calculation "
          "rule (HF: ro = 1.5 t; CF: ro = 2 t / 2.5 t / 3 t by t), marked 'assumed'. MDraftingHeper draws a uniform "
          "wall (inner radius = r - t): exact for CF, but for HF the standard's inner radius 1.0 t is drawn as 0.5 t.",
          profiles)


def main() -> int:
    for need in (EXT / "steel-sections", EXT / "Steel_Section_Database"):
        if not need.exists():
            print(f"Missing {need}. Run tools/Profiles/Fetch-OpenProfileSources.ps1 first.", file=sys.stderr)
            return 1
    ub_uc()
    ipe_he()
    hollow()
    return 0


if __name__ == "__main__":
    sys.exit(main())
