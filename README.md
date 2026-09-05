# 3DFastCraft

A small Windows desktop app for building simple printable geometry — a replacement for the
retired Microsoft 3D Builder. Drop in primitive shapes, size them exactly, cut and combine
them, and export a clean `.stl` or `.obj` for slicing.

Everything is in **millimetres**, **Z-up**, on a 200 × 200 mm build plate — the conventions
every slicer expects.

![3DFastCraft](docs/screenshot.png)

> *In loving memory of Windows 3D Builder. Rest in peace.*

## Requirements

- Windows 10/11, 64-bit
- A GPU supporting Direct3D 11 (feature level 11_0)
- .NET 8 Desktop Runtime — *unless* you use the self-contained build below

## Building and running

Double-click **`build.bat`** to produce a standalone `buildDFastCraft.exe` (~160 MB) that runs
on any 64-bit Windows PC with no .NET install, then **`run.bat`** to start it. `run.bat` falls
back to running from source if no exe has been built yet.

From a shell instead:

```bash
dotnet run --project src/FastCraft3D
```

```bash
dotnet test
```

> `build.bat` deliberately does not pass `PublishTrimmed`. WPF resolves XAML types reflectively
> and trimming breaks the app at runtime.

## Using it

| Tab | What it does |
|---|---|
| **Insert** | Cube, cylinder, cone, sphere, pyramid, wedge, torus, hexagon, tetrahedron; import STL/OBJ |
| **Object** | Subtract / Intersect / Merge, Smooth, Round edges, Split with a plane, Text, Engrave a pattern, Colour, duplicate (beside or in place), delete, drop to plate, mirror |
| **Align** | Line the selection up on X, Y or Z: flush to either edge, centred, or spread evenly |
| **Edit** | Repair, rebuild, simplify, hollow, undo, redo |
| **File** | New, open, recent, save, save as, save a version, versions, export STL/OBJ |
| **View** | Zoom to fit, top / front / right / isometric, measure, wireframe, x-ray, build plate size and visibility |

**Camera:** left-drag orbits, right-drag pans, the wheel zooms. Dragging horizontally turns the
scene around the vertical **Z** axis, like a turntable - the plate never rolls onto its side.

**Selection:** a menu on the right of the viewport holds the selection tools, with
**Sticky selection** on by default, as in 3D Builder. Collapse the menu with the chevron.

With sticky selection **on**, clicking an object toggles that object and nothing else: click to
select, click again to deselect - the only way to deselect one - and click a second object to
add it. No modifier key. Clicking empty space orbits the camera and leaves the selection alone.

With it **off**, selection follows File Explorer: a plain click selects one object, **Ctrl+click**
adds or removes one, **Shift+click** takes everything between the last object clicked and this
one in object-list order, and clicking empty space clears the selection.

| Menu entry | What it does |
|---|---|
| **Group** | Combines the selected objects into one. Their meshes are concatenated, not fused, so Ungroup can tell them apart again. For a true boolean union use **Merge** on the Object tab. |
| **Ungroup** | Splits the selection into its separate pieces. Pieces are found geometrically, so this also breaks up an imported STL holding several loose parts. Parts that genuinely touch are one piece and stay together. |
| **Select all** / **Deselect all** | Everything, or nothing. |
| **Invert selection** | Swaps what is and is not selected. |
| **Sticky selection** | The toggle described above. |

Left-drag *on an object* also moves it across the plate; hold **Shift** to constrain to one axis.

Inserting a shape never moves the camera - it lands at the origin, sitting on the plate.

In the position, size and rotation boxes, **Up/Down** and the **mouse wheel** nudge the value by
1 — hold **Shift** for 10 or **Ctrl** for 0.1. A nudge also rounds onto that step, so a value
like 4.37 tidies up rather than carrying its rounding error forever.

Resizing works about the object's centre, so both faces move outward and the dimension grows by
twice the handle's travel. The toggle beside the proportions one - or **O** - holds the face
opposite the handle exactly where it is, so the object grows by precisely what you dragged and
only the way you dragged it. That is what you want when a part has to keep meeting its neighbour.

**Snap** in the manipulator bar constrains drags to whole millimetres or 5 mm.

**Stop on contact** - the toggle beside it, or **C** - stops a dragged object where it meets
another rather than letting it pass through, which is how parts get slid together without typing
coordinates for them. It works on bounding boxes: for the boxes, plates and walls this is mostly
used for, the box is the shape, and on a rounded or angled face it stops a little early rather
than late, so nothing ever passes through anything. Two parts that already overlap are left free
to move - they were put that way on purpose, usually on the way to a boolean. It snaps where the
object *lands*, not how far it travels, so two parts dragged onto the same grid meet exactly.

Shortcuts: `Ctrl+Z` / `Ctrl+Y` undo & redo, `Delete`, `Ctrl+C` / `Ctrl+V` copy & paste,
`Ctrl+A` select all, `Ctrl+D` deselect all, `Ctrl+N/O/S` new/open/save, `Ctrl+I` import,
`Ctrl+E` export, and `M` / `R` / `S` to switch manipulator mode.

### Colour

New shapes are handed a colour in turn from a six-colour cycle, so a scene stays legible without
anyone having to paint anything.

To change one, select the objects and click a swatch in the **Colour** grid on the right - the
whole selection is painted in a single undo step. **Custom...** on the Object tab (or the button
under the grid) opens a picker with a saturation/value square, a hue strip, and hex and RGB
boxes; it opens on the colour the selection already has, so a small adjustment starts from
where you are.

Colour is a property of the object, so it survives duplication, booleans, splitting and
rounding, and it is saved in the project file. It reaches **OBJ** exports as a `.mtl` sidecar.
**STL has no notion of colour** - an STL export carries geometry only, which is what slicers
read anyway.

### Repairing a model

A model that would not print raises a banner over the viewport - *one or more objects are
invalidly defined* - and **Repair** on the Edit tab mends what it can: it welds, drops what is
genuinely nothing, makes neighbouring faces agree which way they face, turns any shell that came
out inside out, and caps the holes. A shell enclosed by another is left facing inward, because
that is a cavity and turning it outward would fill in the hollow.

It repairs the selection, or everything if nothing is selected.

What it will not do is guess. A mesh that overlaps itself cannot be mended by patching it
locally - the tools that manage it voxelise the model and rebuild the surface, which would
flatten every detail this app exists to cut. Faced with damage it cannot understand it hands the
mesh straight back and says so, rather than tearing it further.

### Measuring

**Measure** on the View tab reads the distance between two points. Click one, click another, and
the tape is drawn over the model with the distance on it - along with the gap broken down by
axis, since a single number hides which way it runs. A third click starts a fresh measurement.

**Snap to corners and edges** pulls each click onto the nearest corner or edge midpoint, which is
what makes the reading exact rather than approximately wherever the pointer landed. It is on by
default.

The tape is drawn over the viewport rather than in it, so it is never hidden behind the model -
the whole point is to read it.

### Seeing inside

**Wireframe** draws every triangle edge, which is how you see what an import actually costs and
where a boolean has left a mess. **X-ray** fades everything that is not selected, so a part
buried inside another can be seen and worked on - only the unselected fade, since the point is to
look past them at what is selected.

The **build plate** takes any size from 20 to 2000 mm, with the common beds offered, and can be
hidden altogether. It is rebuilt rather than stretched when the size changes: its squares are
10 mm so the board doubles as a ruler.

### Smoothing

**Smooth...** on the Object tab rounds a shape off, with the result shown on the plate as you set
it. Two settings, because smoothing has two halves that are easy to confuse:

- **Smoothness** is how hard the surface is pulled toward its own average.
- **Detail** is how finely the shape is divided up first. Smoothing can only move the points it
  has, so a cube - which has eight - cannot be rounded at all until it has been divided. Each
  step splits every triangle into four without moving anything, and the box is offered enough
  by default.

**Smooth open edges** lets the rim of an unclosed shape move too. Off by default, so an opening
keeps its proper shape; it makes no difference to a closed model.

Smoothing changes the part's size, so the dialog reports it: corners pull in while flat faces
dome very slightly outward, which is what makes a smoothed cube a pillow rather than a smaller
cube. A 20 mm cube comes back about 20.75 mm across.

### Simplifying

**Simplify...** on the Edit tab cuts the triangle count down while keeping the shape. Edges are
collapsed cheapest first, where the cost of a collapse is how far it moves the surface away from
the planes the original triangles lay in - so it costs almost nothing to collapse an edge in the
middle of a flat panel and a great deal to collapse one across a crease. Flat parts thin out;
features stay.

It is the natural partner to **Rebuild**, which produces a great many triangles by design.

### Hollowing

**Hollow...** on the Edit tab turns a solid into a shell of a chosen wall thickness, which is
how a large print stops costing a spool of filament.

The obvious way to do it - offset the surface inward and subtract it - is the one that does not
work: offsetting a mesh folds itself inside out at any concave corner. This measures instead how
deep inside the material each point sits and keeps only what lies within one wall of the surface.
The outer face and the cavity come out of the same pass, correctly facing.

The cavity is sealed. A resin print needs a drain hole, which a cylinder and **Subtract** will
cut. A part with no room for the wall you asked for is left solid and says so.

### Rebuilding a broken model

When **Repair** declines, **Rebuild...** beside it mends the model a different way. Instead of
working on the triangles, it decides for every point in a grid over the model whether it is
inside, and builds a fresh surface between inside and out. That is watertight by construction, so
it fixes anything - including a model whose surface passes through itself, which is what an
import with lettering dropped onto its body rather than fused to it usually is.

The cost is resolution: detail finer than one voxel is gone, and the triangle count quadruples
every time the detail doubles. The dialog says both before you commit - a 20 mm cube at 160
voxels across is over three hundred thousand triangles.

### The manipulator

Select something and a bar appears at the bottom of the viewport with three modes and the
live values for whichever is active. Colours follow the axis indicator in the corner:
**red is X, green is Y, blue is Z**.

| Mode | Handles | Readout |
|---|---|---|
| **Move** (`M`) | A double arrow on each of the six box faces. Drag one to slide along that axis only. | X / Y / Z in mm |
| **Rotate** (`R`) | A ring per axis. Drag a ring to turn around it. The toggle snaps to 15° steps. | Roll / Pitch / Yaw in degrees |
| **Resize** (`S`) | The bounding box with corner markers, plus the six axis arrows. Resizing works about the centre, so both faces move. The toggle keeps proportions. | X / Y / Z in mm |

Every drag is one undo step, however many mouse-move events it took, and a drag that changes
nothing adds no step at all. Typing into the readout boxes works the same way as the side
panel — the value commits when the field loses focus.

A selected object is drawn with a **white outline** along its own edges and lifted slightly in
colour. The outline traces the shape rather than the tessellation — a cube shows twelve edges, a
smooth sphere none — so it reads clearly without turning round objects into wireframe. Very
dense imports (over 200k triangles) skip the outline and use the colour lift alone.

The status bar always shows the selected object's size, triangle count, volume, and whether it
is watertight.

### Rounding edges

**Round...** on the Object tab rebuilds a **cube** or **cylinder** with rounded edges at a radius
you choose, shown on the plate as you set it, on whichever edge groups you tick:

| | Cube | Cylinder |
|---|---|---|
| **Top** | the four edges around the top face | the top rim |
| **Bottom** | the four edges around the bottom face | the bottom rim |
| **Sides** | the four upright edges | *not available - a cylinder has none* |

Any combination works. The radius ceiling follows the choice: rounding only the upright edges of
a tall thin box is limited by its footprint, not its height, so the maximum updates as you tick.

The shape is built by sweeping a horizontal cross-section down it — a rounded rectangle whose
corner radius gives the upright edges, inset along a quarter-circle near each end to give the
top and bottom ones. One sweep covers all eight combinations, including the awkward cases where
a fillet has to die into a sharp edge, which is exactly where treating the three groups as
separate pieces of geometry would need special cases to stitch them together.

It regenerates the shape from its parameters rather than filleting the mesh. Filleting an
arbitrary mesh means detecting edge loops, rolling a ball along them and re-stitching the
surface - a hard problem that fails worst exactly where a boolean has already left an untidy
edge. Generating the rounded form is exact, fast and always watertight, at the price of only
working while the object is still described by its parameters. So rounding is offered on a cube
and a cylinder, and refused on a boolean result, a split half, or an import - which is the
honest answer rather than something that half-works everywhere.

Because the shape is rebuilt at its current size, its scale resets in the process; resizing it
unevenly afterwards will stretch the rounded edges.

Closing, opening or starting a new model with unsaved changes asks first, and cancelling the
prompt cancels the whole action — including the window close.

### Lettering

**Text...** on the Object tab cuts words into a face or raises them off it. Select the object,
press it, click the face, and type: the lettering appears on the face as you set it up, with the
font, height and depth alongside.

Letters are real outlines rather than a bitmap, so an O has a proper hole in it and the result is
a few hundred triangles rather than tens of thousands. **Raised instead of cut** stands the
lettering proud of the face rather than sinking it in.

For an FDM print, the same limits apply as to engraving: **0.4 mm deep** is two layers, and
strokes thinner than a couple of nozzle widths will not slice. Bold at 8 mm or more is a safe
starting point.

### Engraving a pattern

**Engrave...** on the Object tab cuts a repeating pattern into one flat face - brickwork on a
wall, boards on a shed, lap siding on a gable.

Select the object, press **Engrave...**, then **click the face** you want. It highlights in
blue, and the panel on the right fills in with the face's size and how many grooves the current
settings would cut. Click a different face at any time to move the pattern; **Cancel** leaves
the mode.

| Pattern | What you get | Size means |
|---|---|---|
| **Brick** | Running-bond masonry: level courses with the perpend joints staggered half a brick | Brick length; the height follows at a third of it |
| **Wood** | Flowing grain that parts around knots, with a ring or two marking each one | The spacing between grain lines |
| **Stripes** | Evenly spaced parallel grooves - lap siding, panelling, ribs | The gap from one groove to the next |

**Line width** is how wide the cut lines are and **Depth** how far they go in. Stripes and wood
can run either way across the face; brick courses are always level, so it has no direction of
its own.

The pattern is drawn **on the face as you set it up**, from the same code that builds the cutter,
so what you see is what gets taken away. Every field takes **arrow keys and the mouse wheel** as
well as typing - Shift for 10 mm steps, Ctrl for 0.1 mm.

#### Making a corner meet

Two walls will not line up by themselves. Each face measures its pattern from its own bottom-left
corner, and which corner that is depends on which way the face points, so the courses on adjacent
walls start at different places and the joints miss each other where they meet.

**Shift across** and **Shift along** slide the pattern over the face to fix it. Engrave the first
wall, then on the second nudge **Shift across** with the arrow keys until the preview's joints
line up with the wall you have already cut. Shifting by a whole repeat changes nothing, so there
is only ever a fraction of a brick to find.

A face picks up its own orientation, so **U runs horizontally on any upright face**: courses come
out level on a wall whichever way the wall faces, without you having to line anything up.

#### Depth, and what will actually print

The panel warns when the settings will not survive the printer, because the numbers that look
reasonable often do not:

- **Under 0.4 mm deep** is less than two 0.2 mm layers, and an FDM slicer will simply drop it.
  0.1 mm is fine on resin and invisible on FDM.
- **Lines under 0.8 mm wide** are narrower than two passes of a 0.4 mm nozzle, and go the same
  way.
- **Deeper than the wall** is called out with the thickness actually available behind the face,
  as is anything taking more than half of it.

Since nozzles and filaments differ, the depth is yours to set - the panel only tells you what
the number means. For most FDM work **0.5-0.8 mm deep with 1-1.5 mm lines** reads well and
prints without any support: the overhang at the top of a groove is only as wide as the groove is
deep, which every printer bridges.

Wood is cut differently from the other two. Brick and stripes are rectangles, resolved into one
region so their joints can run into each other; grain is a set of curves, each extruded along its
own path. The curves are generated freely and then walked in order with a minimum gap enforced
between neighbours, because two of them crossing would leave a wall inside the material being
removed and tear the result open. Where the gap bites, the grain flattens slightly - it can never
fold.

Engraving is a boolean underneath, so the result is watertight and the object becomes plain
geometry - as after a Merge, it can no longer be rounded.

### Versions

Undo keeps every step with no limit, but only while the app is open. To come back to a model
after closing it, **Save a version** keeps a named snapshot *inside* the `.3dfc` file, and
**Versions...** lists them and restores one. Restoring is a normal undo step, so `Ctrl+Z` brings
the current model straight back.

Versions are deliberately named snapshots rather than a persisted undo stack. Undo steps are
fine-grained — every drag is one — so storing them would mean hundreds of near-identical scenes,
and none of them would tell you which was the one worth returning to. A handful of named
versions is smaller and far easier to navigate.

An ordinary save never discards them, and saves are written to a temporary file and moved into
place, so an interrupted save cannot destroy the scene and its whole history together.

### Splitting

Select one or more objects and press **Split** on the Object tab. The plane appears with its own handles:
the blue arrows slide it along its normal, and the coloured rings tilt it — so the plane is not
limited to the three axis-aligned orientations. Then choose which side to keep; the buttons are
named after the direction the plane actually faces, so a horizontal plane offers **Top** and
**Bottom** rather than an abstract front and back. Both halves come out capped and closed.

The plane belongs to the scene rather than to an object, so it cuts **everything selected** in
one stroke and one undo step - which is how an assembly gets sliced in half. Its travel and its
handles are sized to the whole selection, and objects the plane misses are left alone rather than
being dropped.

### Making a hole

Insert a cube. Insert a cylinder, set its size to 10 × 10 × 40 mm so it passes right through.
Select both (`Ctrl+A`) and press **Subtract**. The status bar should read
*"Watertight — ready to print"*.

Boolean order follows the object list: the first selected object is the one the others are cut
out of.

## Formats

| Format | Notes |
|---|---|
| `.stl` (binary, default) | Smallest and fastest. Objects merge into one triangle soup — STL has no notion of separate parts. |
| `.stl` (ASCII) | Human-readable, roughly five times larger. |
| `.obj` | Keeps objects named and separate, and writes a `.mtl` sidecar with colours. |
| `.3dfc` | The project format: GZip-compressed JSON that keeps objects and transforms editable. |

**Export** opens an options dialog before the file dialog:

- **What to export** — everything on the plate (the default) or just the selection. Scope is
  asked for rather than inferred from what happens to be selected, because a model that quietly
  lost half its parts is only discovered in the slicer.
- **Format** — binary STL, ASCII STL or OBJ.
- **Drop to the build plate** — moves the whole export together so its lowest point rests on
  Z = 0, keeping the parts in their relative positions.

The dialog reports the triangle count, volume, bounding size and whether the result is
watertight before anything is written. Exporting warns — but does not block — if it is not.

## How it works

The modelling core (`Geometry`, `Model`, `Io`) contains no renderer types at all; only
`Render/` knows about Direct3D. That separation is what keeps the viewport swappable.

**Boolean operations** use a hand-written BSP-tree CSG engine (`Geometry/Csg/`). No maintained
CSG library exists on NuGet, and the same engine backs three features: the boolean menu, the
plane split — expressed as a boolean against an oversized half-space box, so the cut faces come
out capped automatically — and the guarantee that results stay closed.

A few decisions worth knowing about if you touch this code:

- **CSG runs in double precision.** Float32 leaves ~1.5e-5 mm of noise at build-plate
  coordinates, enough to misclassify a vertex against a cutting plane and leak the solid.
- **CSG runs on a dedicated 256 MB-stack thread** (`CsgRunner`). BSP building recurses per tree
  level; a few tens of thousands of triangles overflows the default 1 MB stack.
- **Splitting is parallelised** across cores above 512 polygons, with per-chunk buckets merged
  in order so output is identical to the serial path — a unit test enforces this. Reproducible
  results matter: polygon order determines the shape of the BSP tree.
- **Results are repaired** (`MeshRepair`). BSP CSG leaves T-junctions and occasional
  zero-thickness "fins"; both read as non-manifold. The two repairs iterate together, because
  closing a T-junction re-welds and can create fins.
- **Welding searches a 27-cell neighbourhood.** Merging only exact grid-key matches silently
  fails for vertices that straddle a cell boundary, leaving zero-length edges.
- **Mirroring flips triangle winding.** A negative-determinant transform reverses handedness;
  without the flip the STL exports inside-out. Handled once, in `MeshTransform.Transformed`.

**Two separate settings have to agree that Z is up.** The camera's `UpDirection` only decides
how the picture is oriented; the turntable orbits around the viewport's `ModelUpDirection`,
which defaults to Y. Left at the default, a horizontal drag orbited around the green axis and
the scene could roll onto its side. The view cube reads the same property. It is set to
`(0,0,1)` in `ConfigureCameraGestures`.

**The object list does not bind `ListBoxItem.IsSelected`.** That looks like the natural way to
mirror `SceneObject.IsSelected`, and it is quietly wrong: a ListBox generates item containers on
a later layout pass, and a fresh container starts out unselected because the Selector does not
know about it yet. A two-way binding pushes that initial `false` back into the model, undoing a
selection made in code — a newly inserted shape never looks selected, and a click in the 3D view
is reverted on mouse-up. `View/SelectionListSync` copies the two sides explicitly instead, one
direction at a time behind a re-entrancy flag, with the scene as the single source of truth.

**The manipulator handles are 2D, not 3D.** `GizmoController` draws them on a canvas over the
viewport, positioning each by projecting a point of the selection's bounding box to screen
space. A 3D arrow shrinks to nothing when you zoom away from a 5 mm part; a projected one stays
a constant, clickable size, picking becomes ordinary WPF hit-testing, and every drag reduces to
2D vector maths. The canvas has no background, so a click that misses a handle falls straight
through to the camera. Two consequences worth knowing:

- The controller depends on `IScreenProjector`, not on the viewport. That is what lets the
  handle layout and all three drag behaviours be tested against a stand-in camera with no GPU —
  WPF shapes expose nothing to accessibility tools, so there is no other way to check them
  automatically.
- Geometry is always built with its top-left at the origin and then placed with `Canvas.Left`.
  A WPF `Path` offsets its geometry to the bounding-box origin during layout, so handing it
  absolute screen coordinates would apply that shift twice.
- The per-frame update asks whether the handles are still where the *current projection* puts
  them, rather than watching the camera and window size that feed it. A resize changes the
  answer without touching either, and the viewport may still be reporting the old answer at the
  moment the resize is signalled — so an input-based check laid out from stale coordinates and
  then never noticed. Watching the output makes any discrepancy self-correct on the next frame.
- A layout is only applied when the projection is usable at all. A viewport that cannot project
  collapses every point onto one spot, so a solid with real size becomes a dot. The handles hide
  instead, and `NeedsReposition` forces a retry.
- `SceneObject.WorldBounds` is cached, because that per-frame check reads it and computing it
  means transforming every vertex.

**GPU use is confined to rendering.** Direct3D 11 via `HelixToolkit.SharpDX.Core.Wpf` gives
MSAA and handles imported meshes of millions of triangles. Booleans stay on the CPU
deliberately: BSP tree work is serial and branch-heavy, and GPU voxel/SDF booleans — robust as
they are — produce resolution-limited, stair-stepped output, which is the wrong trade for clean
primitives headed to a slicer.

## Known limits

- Boolean operations on imported meshes above ~200k triangles are slow (the app warns on
  import). The GPU renders them fine; the BSP tree is the bottleneck.
- `SharpDX` 4.2.0 is unmaintained upstream. It works on Windows 11 and is fully managed, but it
  is the one long-term liability — the renderer-agnostic core is the insurance.
- Engraving covers the face's **rectangular extent**. On anything convex the overshoot
  hangs in mid-air and cuts nothing, so a gable end or a round cap comes out right, but a
  separate face lying in the same plane and inside that rectangle is engraved too, and an
  inside corner loses half a millimetre off the neighbouring face.
- No mesh repair for damaged *imported* meshes, no smoothing/subdivision, no 3MF.

## Layout

```
src/FastCraft3D/
  Geometry/   meshes, primitives, transforms, repair, plane split, Csg/ (the BSP engine)
  Model/      scene objects, scene, Commands/ (undo-redo)
  Io/         STL, OBJ, .3dfc project files, export composition
  Render/     the only Direct3D-aware layer, plus the on-screen manipulator
  View/       selection mirroring between the object list and the scene
  ViewModels/ commands and application state
tests/FastCraft3D.Tests/   geometry, CSG, IO, workflow and manipulator cover
```
