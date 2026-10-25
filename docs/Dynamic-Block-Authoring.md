# Steel Dynamic Block templates

`assets/DynamicBlocks/MDH_SteelSections.dwg` holds four block definitions: `MDH_CHS`, `MDH_SHS`, `MDH_RHS` and `MDH_ISECTION` (shared by UB and UC). They are produced by AutoCAD 2021's own block editor and are not hand-made or faked. The .NET API cannot create block constraint parameters, so authoring is scripted instead:

```powershell
.\tools\DynamicBlocks\Build-DynamicBlocks.ps1 -Verify
```

The script starts `acad.exe /t acadiso.dwt /b <script>`, which runs `-BEDIT <name>` for each block, calls the authoring functions in `tools/DynamicBlocks/MDH_SteelBlocks.lsp`, then runs `BSAVE` and `BCLOSE`. It saves the result and replaces the asset only when every step reports `MDH-AUTHOR-OK` (46 markers). With `-Verify`, a second session opens the saved DWG **without the add-in** and runs `MDH_VerifyBlocks.lsp`.

## Design

- Geometry is lines and arcs on layer 0, centred on the centroid, with a fixed POINT at the origin (the block base point).
- `AUTOCONSTRAIN` on all objects adds coincident, tangent, horizontal, vertical and concentric constraints.
- **Constraint parameters** (`BCPARAMETER`) are the Properties fields: CHS `D`, `t`; SHS `B`, `t`, `r`; RHS `H`, `B`, `t`, `r`; I-section `H`, `B`, `tw`, `tf`, `r`.
- Hidden dimensional constraints with expressions complete the definition:
  - other corners/fillets `= r`
  - I-section other flange tips `= tf`, bottom flange `= B`
  - SHS height `= B`
  - origin offsets `B/2`, `H/2`, `tw/2`, `D/2` (centroid stays at the base point)
  - CHS centre coincident with the origin.
- Hollow sections: inner arcs are concentric with the outer arcs and the inner lines are tangent to them. With one `t` dimension, the inner radius therefore follows as `r − t` and the wall stays uniform.
- A radius parameter is a horizontal distance between the two end points of a 90° arc, which equals the radius. This avoids object picks, which do not work in scripts.
- Authoring sizes are deliberately distinct, so `AUTOCONSTRAIN` cannot infer accidental equalities.

## Scripting notes (AutoCAD 2021)

- The core console (`accoreconsole`) has no `BEDIT`; full `acad.exe` is required.
- Constraint commands reject typed coordinates and entity names in scripts. Use osnap-prefixed points, e.g. `_endp of x,y` or `_node of 0,0`.
- `-PARAMETERS` is not available inside the block editor, so every user-facing value is a constraint parameter.
- `CMDACTIVE` has bit 4 set while a script runs; test bit 1 to detect a command still waiting for input.
- Parameter edits are evaluated when the current command ends; read geometry in a later command or script line.

## Manual authoring (if the script cannot run)

For each block: `BEDIT`, draw the outline as described above with lines and arcs, run `AUTOCONSTRAIN` on everything, add a POINT at 0,0 and `GEOMCONSTRAINT > Fix` it, then add the `BCPARAMETER`s and hidden `DIMCONSTRAINT`s listed in `MDH_SteelBlocks.lsp`, using exactly the parameter names above. Set the block units to millimetres, then `BSAVE`. Run `Build-DynamicBlocks.ps1 -VerifyOnly` against the result.

## Verification performed (AutoCAD 2021, add-in not loaded)

`MDH_VerifyBlocks.lsp`, 45 checks, all passed:

- Each family: properties present and editable; after setting profile values, extents, outer radii = r, inner radii = r − t, wall offsets t, web faces tw/2, flange undersides H/2 − tf and fillet centres all match.
- An untouched instance keeps the template values, and a copied instance is edited independently.
- The DWG was saved, closed and reopened in a new session before verification.

An impossible `t` (150 on B = 200) is accepted as a property value, but the geometry stays at its last valid state and AutoCAD reports that the constraints cannot be resolved. This is native behaviour. Constraint parameters carry no min/max limits in these templates; the add-in validates instead (see User Guide).
