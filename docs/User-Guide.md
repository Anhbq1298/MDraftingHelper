# MDraftingHeper user guide

All geometry is in millimetres. In a drawing whose INSUNITS is not millimetres, Rebar, Tie and Quick Draw Rectangular ask once per drawing whether to convert (INSUNITS itself is never changed); Steel requires a millimetre drawing. Unitless drawings (INSUNITS=0) ask whether to interpret units as millimetres.

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

A 2D drafting tool: it draws tie and hoop **centrelines** around existing longitudinal bar circles. It performs no structural or code checks, no 3D work and no scheduling.

1. Choose **Open Tie** or **Closed Hoop**.
2. Choose the tie diameter (H10–H20), the **Start A** and **End B** hook angles (90°, 135°, 180°, independently) and, optionally, a clear gap and **Flip direction**.
   - Open Tie: **Hook position** — both ends, Start A only or End B only.
   - Closed Hoop: **Closure corner** — the corner bar that carries both hooks.
3. Click **Select 2 Circles…** (open tie: pick Circle A, then Circle B) or **Select Bars…** (hoop: select the bar circles, e.g. with a window). The window reopens with a live preview.
4. Click **Apply & Draw**. Cancelling the selection returns to the window without drawing.

Mandrel diameter and tail lengths are calculated automatically and shown read-only, using EC2 **drafting rules**: inside mandrel 4φ for φ ≤ 16 mm, 7φ above; tail max(10φ, 70 mm) for hooks up to 150°, max(5φ, 50 mm) above 150°. These size the drawing only; the result is not a verified EC2 design.

Geometry rules:

- Bar centres never move. The largest selected bar diameter sizes every bend, and the actual tie diameter is used to place the centreline.
- Straight legs run Ø/2 + clear gap + tie/2 from the bar centres, so the tie touches the largest bar. Where the mandrel bend is larger than the bar, the bar nests in the bend (touching the inside of the arc) instead of the tie being pushed away from it.
- Open tie: the straight tie runs between the two bars; a 180° hook is a semicircular return with its tail running back parallel to the tie.
- Closed hoop: wraps the outer bars of the selection (bars inside it are ignored), with one bend per corner. Both hooks sit on the closure corner, each measured from its own leg: 135°/135° gives the usual two parallel tails into the core; a 90° hook laps along the adjacent leg.
- Invalid drafting output (tails crossing, a tail through a bar, bends too large for the bar spacing) is refused; tails are never shortened silently.
- Output: one 2D lightweight polyline with true arc bulges on layer `MDH_TIE`, entity lineweight H10 0.18, H12 0.20, H13 0.25, H16 0.25, H20 0.30 mm (graphical only). No metadata is attached, and the polyline does not follow later bar moves.

## Steel Section (`MDH_STEEL`)

1. Choose the family (CHS, SHS, RHS, UB, UC), search and pick a profile, or tick **Custom dimensions** and type them.
2. Check the preview, provenance (source, assumed/missing radius) and validation messages.
3. Choose the insertion anchor (centroid, top-left, bottom-centre) and rotation, then **Insert Block…** and pick the point.

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
- Steel insertion creates the Dynamic Block geometry only—no companion label, centre axes or dimensions. While the add-in is loaded, edits made in Properties are checked at the end of the command and the profile/custom/invalid status is stored in metadata. `MDH_STEELSYNC` re-checks every steel block in the drawing (also run automatically when a drawing is opened, writing only when something is out of date).
- If a value cannot be solved (e.g. t ≥ B/2), AutoCAD shows the new value but keeps the previous geometry. MDraftingHeper marks such a block `invalid` in its metadata. Enter a valid value or Undo.

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

## Quick Draw Rectangular (`MDH_RECT`)

1. Enter section width B, height H and concrete cover. Cover is measured to the **outside face of the outer tie**.
2. Choose one longitudinal mode:
   - **Specify by Face:** enter counts and diameters for opposite B and H faces, then Apply Layout. Corners are generated once (`C-TL`, `C-TR`, `C-BL`, `C-BR`); intermediate bars use `T1…`, `B1…`, `L1…`, `R1…`.
   - **Custom Coordinates:** add uniquely named individual bars with diameter, X and Y. X/Y are local coordinates about the section centre. Rename-only edits preserve tie references; diameter/coordinate/add/delete changes clear every old tie.
3. In face mode, configure the tie size, bend-detail preset and the explicitly labelled centre-to-centre or clear-distance restraint assumption, then **Generate / Replace Auto Ties**. Automatic arrangement is disabled in Custom Coordinates mode.
4. Use the Tie Bars heading toolbar to add, edit or delete ties. The picker contains longitudinal bars only; click Circle A then Circle B, or use the named lists. Separate Hook A/Hook B and Flip regenerate through the same geometry engine used by `MDH_TIE`.
5. Check the live bar count, As and ρ, then **Draw in AutoCAD**. Pick an insertion point and rotation.

The longitudinal table remains visible in both modes and is read-only in face mode. The Tie Bars table is the final output list: insertion does not silently regenerate deleted/edited ties. A committed layout change increments its revision and atomically invalidates all previous ties; stale/missing references or impossible hook geometry block insertion. The status reports names that fail the final restraint audit.

Output consists only of the concrete boundary, actual rebar Circles and true-bulge tie polylines. Bar/tie names, axes and dimensions may be shown in the dialog preview but are never inserted into the drawing. Preview and CAD use the same Core paths. One insertion is one Undo step, and cancelling placement writes nothing.

This tool is for 2D drafting and restraint arrangement. It does not perform PMM, shear capacity or longitudinal member design. The default 150 mm centre-to-centre limit is a visible, configurable preview assumption and is not claimed as a verified code interpretation.

## Layers and settings

Default layers: `MDH_REBAR_TABLE`, `MDH_REBAR`, `MDH_SECTION`, `MDH_TIE`, `MDH_STEEL`, `MDH_AXES`, `MDH_LABEL`. Existing layers keep their properties; locked or frozen layers are refused with a message. Names and other defaults can be changed in `%APPDATA%\MDraftingHeper\settings.json`.
