# 3DFastCraft 1.7.5 — plan


## Context

Four changes, all from using the app:

1. **Multi-object Rotate/Resize is wrong by default.** With several objects selected, Rotate turns
   each about its own origin instead of turning the selection as one object, and the bar's boxes
   mix absolute and "shared" meanings. The user wants the group treated as one object, keeping
   per-object behaviour as an option.
2. **3MF** — 3D Builder's own format and every modern slicer's preferred one. Migrating users
   arrive with `.3mf` files the app cannot open.
3. **Extrude down** — 3D Builder had it. Gives a scan with a ragged bottom, a relief skin, or a
   tilted figure a flat base on the plate.
4. **Split with connectors** — cut a model too big for the plate into halves that register with
   pins, reusing Split and the mould's key logic.

Decisions already taken with the user:
- Pivot toggle named for what it does (*around selection centre* / *around each object's centre*);
  default is the group. One switch for the Move, Rotate and Resize bars, shown when several objects
  are selected. It governs **typed values in all three** and **dragging in Rotate and Resize**
  (dragging Move is identical either way). This replaces the proposed separate "Group Action" tab.
- The switch decides *what a number refers to*; a leading `+`/`-` decides *to* or *by*:

  | Box | Pivot: Selection (group) | Pivot: Each object |
  |---|---|---|
  | X Y Z shows | group centre | shared value, or *mixed* |
  | `20` | group centre to 20, arrangement kept | every object's centre to 20 (they line up) |
  | `+5` | group by 5 | each by 5 (same result) |
  | W D H shows | group extent | shared size, or *mixed* |
  | `100` | scale selection about its centre to 100 (gaps scale) | each object to 100 about its own centre |
  | `+10` | group box 10 larger | each 10 larger |
  | Roll Pitch Yaw shows | 0 | shared angle, or *mixed* |
  | `15` | turn selection 15° about its centre, reads 0 again | set each to 15° about its own centre |
  | `+15` | same as `15` | turn each by 15° about its own centre |

  Group rotation is necessarily relative: parts turned different ways have no single angle.
- Connectors: **offer both** — separate round pins (default) and pegs fused onto one half.
- Extrude down: **cut at a height, extend down** to a flat base at Z = 0.

---

## 1. Pivot for Rotate and Resize

**Render** — `src/FastCraft3D/Render/GizmoController.cs`
- Add `public bool AroundSelectionCentre { get; set; } = true;`
- `DragRotate` (~l.747–772): today only `Rotation` is composed with `turn`. In group mode also move
  each `Position` about `dragCentre`: `pos = dragCentre + Vector3.Transform(before.Position - dragCentre, turn)`.
  Per-object mode keeps today's code.
- `DragScale` (~l.663): in group mode scale **positions** about the selection's held point/centre as
  well as sizes (the arithmetic already exists for one-side: `GizmoMath.ScaledAbout`). Per-object
  mode keeps today's "each grows where it stands".
- Status text names the pivot.

**ViewModel** — `src/FastCraft3D/ViewModels/MainViewModel.cs`
- `bool AroundSelectionCentre` (default true) → pushed to the gizmo, same pattern as `ScaleOneSide`
  (`MainWindow.xaml.cs` ~l.1081).
- Each box's getter and setter branch on the pivot, implementing the table above:
  - `GroupX/Y/Z` (~l.332): group → `GroupCentre()` / `MoveGroupTo` (~l.561, exists); each →
    `Shared(o => o.PositionX)` getter and a setter that sets every object's position.
  - `GroupSizeX/Y/Z` (~l.383): group → `GroupExtent()` / `ResizeGroup` (~l.486, exists, already
    scales positions about the centre); each → `Shared` size getter and per-object resize about its
    own centre, positions untouched.
  - `GroupRoll/Pitch/Yaw` (~l.357): each → today's `Shared(...)` + set-all (exists); group → getter
    returns 0, setter turns the whole selection about `GroupCentre()` (rotate positions as well as
    orientations), then re-raises so the box reads 0.
- Switching the pivot re-raises all Group* boxes, since their meaning changes.
- **Relative typing:** the boxes bind floats today, so a `+5` never reaches the setter. Add a small
  converter/parser used by the Group* and Object* boxes: a leading `+`/`-` followed by a number
  means "by", anything else means "to". Put the parse in a pure static (`ViewModels/FieldInput.cs`)
  so it is unit-testable.
- Mixed display: a converter that renders `NaN`/null as empty with a "mixed" watermark.

**View** — `src/FastCraft3D/MainWindow.xaml`
- Toggle icon next to the Lock / One-way buttons in Rotate and Resize bars (l.~1526–1660), visible
  when `HasManySelected`. Tooltips as agreed. Every box tooltip: *"Type a value to set it on all
  selected, or +5 / −5 to change each by that much."*
- Labels: `Roll/Pitch/Yaw` stay (already used by Split); `W/D/H` gain tooltips "Width – along X" etc.

## 2. 3MF import and export

**New** `src/FastCraft3D/Io/ThreeMfIo.cs` (namespace must not start with a digit — class `ThreeMf`).
- 3MF = OPC zip; model in `3D/3dmodel.model` (XML, units attribute, `<object><mesh><vertices>
  <triangles>`, `<build><item transform=...>`). Reuse `System.IO.Compression` as
  `SceneSerializer.cs` already does.
- **Read:** every `object` with a mesh → one part; apply the build item's 3×4 transform; honour
  `unit` (micron/mm/cm/inch/foot/meter → mm); `name` attribute → object name; `basematerials`
  displaycolor → colour when present. Components (`<components>`) flattened.
- **Write:** one `object` per exported part, `unit="millimeter"`, colour as `basematerials`,
  plus the required `[Content_Types].xml` and `_rels/.rels`.
- Wire-in: `IncomingFiles.Known` (add `.3mf`) — drop and command line follow automatically;
  import dispatch in `MainViewModel.ImportFiles` (~l.4398) beside OBJ; `ExportFormat.ThreeMf` in
  `Io/ExportOptions.cs` (+`Extension`, `Filter`), radio in `View/ExportDialog.xaml(.cs)`, write
  branch beside OBJ (~l.4478) using `ExportComposer.ComposeForObj` (per-part list, drop to plate).

## 3. Extrude down (Tools tab)

**New** `src/FastCraft3D/Geometry/ExtrudeDown.cs` — `Mesh Apply(Mesh world, float cutHeight)`:
1. `PlaneClip.Keep(world, Identity, +Z, cutHeight, cap: false)` → the part above, open along the cut.
2. Chain the open ring(s) at `cutHeight`. `PlaneClip.Rings` (l.277) is private — make it `internal`
   (or expose `PlaneClip.CutRings(...)`) rather than writing a second loop-chainer.
3. For each ring: a vertical wall of quads from `cutHeight` down to Z = 0, sharing the ring's
   vertices bit-for-bit (same welding discipline as `PlaneClip.Between`).
4. Base at Z = 0: `Polygon2.Nest` + `Polygon2.Triangulate` (holes supported — a hollow neck stays
   hollow), wound to face down.
5. `Welded()`, `CheckHealth()`; refuse (never warn) if not watertight, per project rule.

**VM/UI:** `ExtrudeDownCommand` on the Tools tab (`MainWindow.xaml` l.522), a small mode with a
height box and the horizontal plane preview reusing `ShowSplit`/split plane visual; default height
= lowest point + 1 mm. One undo step via `ReplaceObjectsCommand`. Refuses when the cut height is at
or above the model's top.

## 4. Split with connectors (Tools tab)

Reuse the Split mode end-to-end (plane gizmo, bar, preview, `ApplySplit` ~l.4095) and add a
**Connectors** section to its panel, visible when launched from the Tools button:

- Options: *Separate pins* (default) / *Pegs on one half*; count (auto, 1–6); diameter (default
  5 mm); depth into each half (default 6 mm); clearance (default 0.2 mm, same meaning as Subtract).
- **Placement** (new pure static `Geometry/ConnectorLayout.cs`): take the cut rings (same exposure as
  §3), flatten to the plane frame, `Polygon2.Nest` into outline + holes, then place points that are
  inside the solid with ≥ radius + 1.5 mm to every ring edge — grid sample + `Polygon2.Contains`,
  spread by farthest-point selection. Fewer fit than asked → say so, never place a pin in a thin wall.
- **Separate pins:** subtract a cylinder (Ø + 2·clearance, depth + clearance) from **both** halves
  at each point via `LocalCsg.Subtract`; add pin objects (Ø, 2·depth) laid flat beside the parts.
- **Pegs:** `LocalCsg.Union` a cylinder onto the front half, `LocalCsg.Subtract` the grown socket
  from the back. Keys a hair off the plane as in `MouldBuilder.Keys` (coplanar faces tear).
- Self-check each half watertight → heal once → refuse the connectors (keep the plain split) if still
  torn, and say so. Never ship a torn half.

## Tests (xUnit, names as sentences)

- `GizmoControllerTests`: group rotate moves positions about the centre; per-object keeps them;
  group resize spreads positions; per-object does not.
- `PivotTypingTests` (view model, STA like `CancelToolTests`): one test per row of the table in
  Context — each of `20` / `+5`, `100` / `+10`, `15` / `+15` under both pivots, plus *mixed* shown
  when values differ and the group rotation box reading 0 after a turn.
- `FieldInputTests`: `+5`, `-5`, `5`, `-5` as absolute vs relative, blanks, garbage.
- `ThreeMfTests`: round-trip a two-part coloured scene; unit conversion (inch file → mm); build
  transform applied; components flattened.
- `ExtrudeDownTests`: box lifted 10 mm → base at 0, watertight, volume = expected; ring with a hole
  keeps the hole; cut at/above top refused.
- `ConnectorLayoutTests`: pins stay inside with margin on a box face and an L-shaped face; a face too
  small returns fewer and says so; both halves watertight after pins and after pegs.

## Verification

1. `dotnet test` — whole suite green.
2. By hand: select three cubes → Rotate 45° with the pivot on group, then on each; type `+10` in X
   with several selected; import a `.3mf` exported from PrusaSlicer/Bambu; export `.3mf` and open it
   in a slicer; Extrude down on the Bearded Gentleman scan; Split with connectors on a 150 mm box,
   print test pins at 0.2 mm clearance.
3. README (manipulator section, new tools, formats), site "New in", release 1.7.5 — per the ship skill.

## Order

1 (pivot) → 2 (3MF) → 3 (Extrude down, which exposes the rings) → 4 (connectors, reuses them).
Each lands with its tests before the next starts; nothing committed until the user asks.
