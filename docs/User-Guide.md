# MDraftingHeper user guide

All geometry is in millimetres. In a drawing whose INSUNITS is not millimetres, Rebar and Tie ask once per drawing whether to convert (INSUNITS itself is never changed); Steel requires a millimetre drawing. Unitless drawings (INSUNITS=0) ask whether to interpret units as millimetres.

Each tool opens its own small dialog. When the tool needs a selection, the dialog closes, you pick in model space, and it reopens. Esc at any prompt cancels safely; nothing is written until you confirm. Every completed insertion is one Undo step.

## Rebar Percentage (`MDH_REBAR`)

1. Select the rebar **Circles** (direct Circle entities; circles inside blocks are not read).
2. Select the **closed lightweight polyline** that is the gross concrete outline (arc segments are supported; a Circle is accepted for circular sections).
3. The dialog shows bar groups (size, quantity, area per bar, total area), the number of bars, As, Ag and ρ = As/Ag.

- Diameters are measured (2 × radius). Bars within the **Ø tolerance** are grouped. A group is labelled with a nominal size (e.g. H16) only when every bar is within tolerance of it; otherwise it is shown as non-standard with its measured diameter. Areas always use the measured diameters.
- Bars outside or crossing the boundary, and circles not in the boundary's plane, are **not counted** and are listed (red in the preview). To continue, either reselect or tick *Exclude the flagged bars*.
- The boundary must be closed, planar, non-self-intersecting and have positive area.
- **Target ρ** is optional; it adds As,target and As − As,target.
- **Insert Table** asks for a point and inserts a native AutoCAD Table on layer `MDH_REBAR_TABLE`, aligned with the current UCS. The table is a dated **snapshot**; it does not update when bars change.

## Tie & Hook (`MDH_TIE`)

1. Pick **Circle A**, then **Circle B** (the longitudinal bars; both must lie in one plane).
2. Choose hook angle (90°, 135°, 180°), tie diameter (H10–H20), hook position (A, B or both) and Flip.
3. Choose a hook preset:
   - **Manual input** (default): you enter the inside bend (mandrel) diameter and the straight tail length.
   - **EC2 + Singapore NA (candidate values) — UNVERIFIED**: example rules that have not been checked against the authorised code text. Do not present the result as code-compliant.
4. *Preview & Draw* shows the tie in model space: **Enter** accepts, **F** (Flip) mirrors, **Esc** returns to the dialog with nothing drawn.

Geometry rules:

- Dref = max(DA, DB) is used for the hooks at **both** ends; each hook stays centred on its own bar, so the smaller bar gets more clearance (shown in the dialog).
- Centreline bend radius Rc = max(Dref/2 + clear gap + tie/2, mandrel/2 + tie/2). Rfit, Rmandrel, Rc and the clearances are shown separately.
- The straight tie is tangent to both bends (offset Rc from the line between bar centres). A 180° hook is a semicircle around the bar with its tail running back parallel to the tie.
- If tails would clash with each other or pass through a bar, drawing is refused; tails are never shortened silently.
- Output: one lightweight polyline with true arc bulges on layer `MDH_TIE`, with entity lineweight H10 0.18, H12 0.20, H13 0.25, H16 0.25, H20 0.30 mm (graphical only). Hook settings are stored on the polyline (XRecord `MDH_TIE`). The tie does not follow later bar moves.

## Steel Section (`MDH_STEEL`)

1. Choose the family (CHS, SHS, RHS, UB, UC), search and pick a profile, or tick **Custom dimensions** and type them.
2. Check the preview, provenance (source, assumed/missing radius) and validation messages.
3. Choose the insertion anchor (centroid, top-left, bottom-centre), rotation, centre axes and label, then **Insert Block…** and pick the point.

After insertion, select the block and press **Ctrl+1**. Under **Custom** the Properties palette lists the numeric parameters, which change the real geometry:

| Family | Block | Properties |
| --- | --- | --- |
| CHS | MDH_CHS | D, t |
| SHS | MDH_SHS | B, t, r |
| RHS | MDH_RHS | H, B, t, r |
| UB / UC | MDH_ISECTION | H, B, tw, tf, r |

- `r` is the **outer** corner radius for SHS/RHS (inner = r − t, so r must exceed t) and the **root** radius for UB/UC.
- These are native Dynamic Block constraint parameters: they also work in AutoCAD without the add-in, and each instance is independent.
- The block base point is the centroid. The anchor only positions the insertion; later edits keep the centroid fixed.
- Family and profile are chosen in the dialog only; they are not editable fields in Properties.
- While the add-in is loaded, edits made in Properties are followed at the end of the command: the label switches to "Custom …" when the dimensions no longer match a library profile (and back when they do), and the axes and label are refitted. These updates belong to the same Undo step as the edit. Changes made by Undo/Redo are not re-processed. `MDH_STEELSYNC` re-checks every steel block in the drawing (also run automatically when a drawing is opened, writing only when something is out of date).
- If a value cannot be solved (e.g. t ≥ B/2), AutoCAD shows the new value but keeps the previous geometry. MDraftingHeper marks such a block `invalid` in its metadata and does not relabel it. Enter a valid value or Undo.

### Profile data

The shipped libraries are converted from openly licensed datasets (`tools/Profiles/convert_open_profiles.py`, pinned commits; notices in `Resources\Profiles\THIRD-PARTY-NOTICES.md`):

| File | Families / series | Source (licence) |
| --- | --- | --- |
| `open-uk-ub-uc.json` | UB (107), UC (46) | p-horne/steel-sections (MPL-2.0) |
| `open-en-ipe-he.json` | IH: IPE, HEA, HEB, HEM (90), EN 10365:2017 | ulmewix/Steel_Section_Database (MIT) |
| `open-en-hollow.json` | CHS (344), SHS (406), RHS (756); HF = EN 10210-2, CF = EN 10219-2, names end in HF/CF | ulmewix/Steel_Section_Database (MIT) |

Hollow-section corner radii left empty upstream use the standards' calculation rule (HF ro = 1.5 t; CF 2t/2.5t/3t) and are shown as *assumed*. The block draws a uniform wall (inner radius r − t): exact for CF; for HF the standard's inner radius 1.0 t is drawn as 0.5 t. Taper-flange series (IPN, S, RSJ) and AISC/other national series are not included. Dlubal's online library has no reuse licence and is not included.

Put other authorised data in `%APPDATA%\MDraftingHeper\Profiles` as JSON (schema below) or CSV (see `profile-template.csv` in the install's `Resources\Profiles`). Records missing data are rejected with reasons in the log; duplicate ids are rejected.

JSON schema version 1:

```json
{
  "schemaVersion": 1, "source": "…", "sourceVersion": "…", "isDemonstration": false, "units": "mm",
  "profiles": [
    { "id": "unique-id", "family": "RHS", "standard": "…", "series": "…", "name": "RHS 200x100x8",
      "dimensions": { "H": 200, "B": 100, "t": 8, "r": 12 },
      "cornerRadiusConvention": "outer", "cornerRadiusStatus": "published",
      "publishedArea_mm2": 4480, "publishedMass_kg_per_m": 35.1 }
  ]
}
```

Units may be mm, cm, m or in. `cornerRadiusConvention` (outer/inner) is required for SHS/RHS; inner radii are converted to outer. `cornerRadiusStatus` is published, assumed or missing; a missing radius must be entered before insertion. Published area/mass are kept separate from the area computed from the drafted geometry.

## Layers and settings

Default layers: `MDH_REBAR_TABLE`, `MDH_TIE`, `MDH_STEEL`, `MDH_AXES`, `MDH_LABEL`. Existing layers keep their properties; locked or frozen layers are refused with a message. Names and other defaults can be changed in `%APPDATA%\MDraftingHeper\settings.json`.
