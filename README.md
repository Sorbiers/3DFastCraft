# 3DFastCraft

A small Windows desktop app for building simple printable geometry — a replacement for the
retired Microsoft 3D Builder. Drop in primitive shapes, size them exactly, cut and combine
them, and export a clean `.stl` or `.obj` for slicing.

**Website: [3dfastcraft.com](https://3dfastcraft.com/)** · [Download the latest release](https://github.com/Sorbiers/3DFastCraft/releases/latest)

> *In loving memory of Windows 3D Builder. Rest in peace.*


![3DFastCraft](docs/screenshot.png)


## Intro

Windows 3D Builder was a beautiful thing: fast modelling for professionals and for people who had
never modelled anything, with almost nothing to learn before the first part was on the plate.
Microsoft retired it. This app exists to take its place, and works the same way — the same short
path from a shape on the plate to a printable part. These are the tools it adds on top.

| | Tool | What it does |
|---|---|---|
| 01 | **Stair** | Treads, risers, width and a rise per turn as one object you type the numbers for. |
| 02 | **Subtract with tolerance** | Takes the cutter out a fraction wider than it is, so the two printed parts actually fit. |
| 03 | **Duplicate in place** | A copy exactly where the original stands, ready to be cut about — or beside it, clear of it. |
| 04 | **Repeat** | Along a line or round a circle, with a rise per step for a spiral stair. |
| 05 | **Align** | Lay a part on a face you pick, or line a selection up flush, centred or evenly spread. |
| 06 | **Pattern engraving** | Brick, roof tile, wall tile, plank, wood grain and stripes — cut in or standing proud, and laid *around* openings already cut. |
| 07 | **Round edges** | A cube or cylinder rebuilt with its edges rounded to the radius you ask for. |
| 08 | **Rebuild** | Remakes the surface from scratch when Repair cannot mend it. |
| 09 | **Mould** | A block to cast silicone in: works out which way the mould comes apart, cuts at the widest section, keys the halves together and drills the pour hole. |
| 10 | **In-file versioning** | Named snapshots kept *inside* the `.3dfc` project itself, and a way back to any of them. |
| 11 | **Scale auto conversion** | Set 1:87 and every box reads in real metres beside the millimetres. Type into either one. |
| 12 | **Split with connectors** | Cut a part too big for the plate into halves that go back together one way only: round pins, or pegs standing up out of the lower half. |
| 13 | **Connect objects** | Pins or pegs through the face two parts rest on each other on, only where both have solid material, each part kept as itself. |
| 14 | **As one** | Move, turn and resize several objects as one about the middle of the lot, or each on its own - and type `+=5` into any box to change a value by that much. |
| 15 | **Gears** | Involute gears, ring gears and racks, bevels, worm drives and ratchets - straight, helical or herringbone - each with its partner made already in mesh. |
| 16 | **Reciprocating frame** | A gear cut away to a sector and the frame it drives to and fro from a motor that only turns one way, made from the motion itself, with a lock that holds it between pushes. |
| 17 | **Threads and holes** | ISO metric rods, bolts, nuts and threaded holes that screw together printed; screw holes, countersinks, counterbores, nut pockets and heat-set insert pockets, one at a time, in a row or round a bolt circle. |
| 18 | **Sketch** | An outline drawn on the plate or read from an SVG, extruded up or revolved into a solid. |
| 19 | **Blueprint** | Front, top, right and isometric views with every straight edge dimensioned and a title block, printed or saved as a PDF. |
| 20 | **Set pivot** | Measure and turn an object about a point you pick - a shaft hole, a hinge line - rather than the middle of its box. |
| 21 | **Align to a face** | Line a selection up on the middle of any face, or centre a face of it on a face of something else. |
| 22 | **Lithophane** | A photograph as a thin plate that shows when a light is behind it, flat or curved round for a shade. |
| 23 | **Voronoi** | A part cut into a web of struts: an openwork lamp, or a foam through the inside to take weight out. |
| 24 | **Export session and Record** | Every undo step written out as a file of its own, or a screenshot taken after every action. |

**Almost all of it is vibecoded.** The geometry, the renderer, the interface and the tests were
written by [Claude Code](https://claude.com/claude-code) from prompts, with only tiny manual
interventions.

## Requirements

- Windows 10/11, 64-bit
- **Graphics that support Direct3D 11** (feature level 11_0). Not a separate card - the integrated
  graphics in any processor since about 2010 will do. The whole viewport is Direct3D, so this is a
  requirement rather than a preference: without it the app says so and stops. The cases where it is
  genuinely missing are a virtual machine with no display driver, some remote desktop connections,
  and a fresh Windows install still running on the Basic Display Adapter - installing the machine's
  graphics driver fixes that last one.
- .NET 8 Desktop Runtime — *unless* you use the self-contained build below

## Building and running

Double-click **`build.bat`** to produce a standalone `build\3DFastCraft.exe` (~160 MB) that runs
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
| **Insert** | Cube, cylinder, cone, sphere, pyramid, wedge, torus, hexagon, tetrahedron; a custom shape; text; a lithophane; a sketch; import an STL, OBJ or 3MF, or another project; a fit test; a hole; a thread; a stair; a gear |
| **Edit** | Everything that changes the selection: undo, redo; copy, cut, paste; duplicate beside or in place, delete; Subtract / Intersect / Merge, split with a plane; set a pivot; round, twist, taper, bend; simplify, smooth, hollow; colour |
| **Align** | Where it sits: drop to plate, lay on face, align to another object, align to a face, centre face to face, fit to the bed, distribute, mirror, and lining the selection up on X, Y or Z - to either edge, centred, or spread evenly |
| **Tools** | The work around a model: mould, hull; repair, rebuild; Voronoi, extrude down, split with connectors, connect objects; emboss, engrave; repeat; measure, fit check |
| **File** | New, open, recent, save, save as, save a version, versions, export STL/OBJ/3MF, blueprint, export session, record, about, shortcuts |
| **View** | Zoom to fit, the six axis views and isometric, wireframe, x-ray, outline, plate, overhangs, grid; units and model scale |

**Edit** sits second because it is where the work is: whatever changes the selection - what it is
made of or what its surface is - is there. **Align** changes only where a thing sits, and
**Tools** measures a model, mends it, or makes something new from it.

**Classic or Advanced**, at the right of the tabs, decides how much of that is shown. Classic
shows only the tools 3D Builder had, for anyone who wants the old app back and nothing else;
Advanced, the default, shows everything. Only buttons are hidden, never a setting inside a panel,
every key works in both, and **F1** lists the keys for whatever is showing. The choice is
remembered.

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
| **Group** | Combines the selected objects into one. Their meshes are concatenated, not fused, so Ungroup can tell them apart again. For a true boolean union use **Merge** on the Edit tab. |
| **Ungroup** | Splits the selection into its separate pieces. Pieces are found geometrically, so this also breaks up an imported STL holding several loose parts. Parts that genuinely touch are one piece and stay together. |
| **Select all** / **Deselect all** | Everything, or nothing. |
| **Invert selection** | Swaps what is and is not selected. |
| **Sticky selection** | The toggle described above. |

Sticky selection is remembered from one run to the next.

**The object list** under the menu has an eye and a padlock on every row. A **hidden** object is
not drawn, cannot be picked, and is left out of an export of everything - `H` hides the selection
and `Alt+H` brings everything back. A **locked** one stays in view and in the way, so it still
stops a dragged part on contact, but it cannot be selected, so nothing can change it by accident -
`L` locks the selection and `Alt+L` unlocks everything. Both are saved with the project. The dot
at the start of each row is the object's colour; clicking it opens the colour picker for that
object, or for the whole selection when it is part of one.

The side panel below the list gives the selection's **Name**, then **Colour**, **Filament**,
**Position**, **Size** and **Rotation** under one **Properties** heading that folds away when the
room is wanted for a tool. Whether it is folded is remembered.

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
coordinates for them. It is measured on the **shapes themselves**, not on the boxes round them,
so a ball brought up to a cone stops touching the slope rather than a base radius short of it, and
a part that has been turned stops where it actually meets rather than where its box does. Two
parts that already overlap are left free to move - they were put that way on purpose, usually on
the way to a boolean. It snaps where the object *lands*, not how far it travels, so two parts
dragged onto the same grid meet exactly.

A drag works its answer out once, on the way down, so none of that measuring happens as the mouse
moves. Something too dense to flatten - an import in the hundreds of thousands of triangles -
falls back to its bounding box rather than stalling the drag.

**F1** lists every key the app answers to, and the drags worth knowing, from the same table the
keys are bound from - so the list cannot drift from what the keys do. The ones used most:

| Keys | What they do |
|---|---|
| `Ctrl+Z` / `Ctrl+Y` | Undo, redo |
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | Copy, cut, paste - a paste lands exactly where the copy came from |
| `Ctrl+D` / `Ctrl+Shift+D` | Duplicate beside, duplicate in place |
| `Delete` | Delete |
| `Ctrl+A` / `Ctrl+Shift+A` | Select everything, select nothing |
| `Ctrl+G` / `Ctrl+Shift+G` | Group, ungroup |
| `H` / `Alt+H`, `L` / `Alt+L` | Hide the selection, show everything; lock the selection, unlock everything |
| `Ctrl+Space` | Run the last tool again, with what it was last given |
| `M` / `R` / `S` | Move, rotate or resize handles |
| `O`, `C` | One way only when resizing; stop on contact when moving |
| Arrows, `Page Up` / `Page Down` | Nudge the selection along X, Y and Z by the snap step, or 1 mm with snap off; a held key is one undo step |
| `F`, `1` `2` `3` `4` | Zoom to fit; top, front, right and isometric views |
| `Ctrl+N` / `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` | New, open, save, save as |
| `Ctrl+I` / `Ctrl+E` / `Ctrl+P` | Import, export, blueprint |
| `Esc` | Put down the tool in hand |

Every one of them is also named in the tooltip of the button it belongs to. A greyed-out button
still shows its tooltip, and one that needs a selection says what it needs.

### Colour

New shapes are handed a colour in turn from a six-colour cycle, so a scene stays legible without
anyone having to paint anything.

To change one, select the objects and click a swatch in the **Colour** grid on the right - the
whole selection is painted in a single undo step. **Colour** on the Edit tab (or **Custom...**
under the grid) opens a picker with a saturation/value square, a hue strip, and hex and RGB
boxes; it opens on the colour the selection already has, so a small adjustment starts from
where you are.

Colour is a property of the object, so it survives duplication, booleans, splitting and
rounding, and it is saved in the project file. It reaches **OBJ** exports as a `.mtl` sidecar.
**STL has no notion of colour** - an STL export carries geometry only, which is what slicers
read anyway.

**Filament** is a separate number: which spool prints the part on a printer with more than one -
an AMS slot, an MMU tool. It is not the colour, because two parts of the same shade are not
thereby the same material. It goes into an exported 3MF, on the object and in the sidecar the
PrusaSlicer family reads; STL and OBJ have nowhere to put it.

### Repairing a model

A model that would not print raises a banner over the viewport - *one or more objects are
invalidly defined* - and **Repair** on the Tools tab mends what it can: it welds, drops what is
genuinely nothing, makes neighbouring faces agree which way they face, turns any shell that came
out inside out, and caps the holes. A shell enclosed by another is left facing inward, because
that is a cavity and turning it outward would fill in the hollow.

It repairs the selection, or everything if nothing is selected.

A damaged object is outlined in **red** from the moment it is loaded or made, the outline tracing
the tear itself, so it is plain which part the banner is about and where it is broken.

What it will not do is guess. A mesh that overlaps itself cannot be mended by patching it
locally - the tools that manage it voxelise the model and rebuild the surface, which would
flatten every detail this app exists to cut. Faced with damage it cannot understand it hands the
mesh straight back and says so, rather than tearing it further.

### Measuring

**Measure** on the Tools tab reads the distance between two points. Click one, click another, and
the tape is drawn over the model with the distance on it - along with the gap broken down by
axis, since a single number hides which way it runs. A third click starts a fresh measurement.

**Either end can be dragged** once it is down. The first click is rarely on the exact corner
meant, and correcting it used to mean measuring the whole thing again.

**Snap to corners and edges** pulls each click onto the nearest corner or edge midpoint, which is
what makes the reading exact rather than approximately wherever the pointer landed. It is on by
default.

The tape is drawn over the viewport rather than in it, so it is never hidden behind the model -
the whole point is to read it.

### Seeing inside

**Wireframe** draws every triangle edge, which is how you see what an import actually costs and
where a boolean has left a mess. **X-ray** fades everything that is not selected, so a part
buried inside another can be seen and worked on - only the unselected fade, since the point is to
look past them at what is selected. While a tool has the plate to itself - a split, a preview -
X-ray brings the rest back, drawn through rather than taken away.

**Outline** turns off the white line round a selected object. On a dense mesh every feature edge
is an edge, so the outline covers the very surface it is meant to bound - a lithophane comes back
as a white haze of its own picture.

**Overhangs** marks in colour every face leaning further from upright than an angle you set -
what the slicer will want to hold up with support. Faces lying flat on the bed are left out, since
the bed holds them up, and the area marked is given on the status line. The angle and the colour
are set in a side panel while it is on; the colour is mostly self-lit, so a face turned away from
the light still reads as the colour picked.

**Grid** opens the plate's settings. The **printable area** is your printer's build volume as
width, depth and height - a guide rather than a limit: **Fit** and **Distribute** use it, and
nothing else is held to it. The X and Y axes can be drawn across the plate, paler on the negative
side so which way is plus reads from any angle; an upright Z axis as tall as the printable height;
and distances along the axes in the current units. **Shadows** and **Reflections** are here too -
objects casting shadows onto the plate and each other, and the plate reflecting what stands on it
as a glossy bed does. Each costs a second render pass, so both start off. The printable area is
kept in the project; the rest is remembered on the machine.

**Plate** hides the build plate altogether. Its squares are 10 mm, so the board doubles as a
ruler.

### Smoothing

**Smooth...** on the Edit tab rounds a shape off, with the result shown on the plate as you set
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

**Leave open** takes one side away with the cavity - the underside of a roof, the back of a
facade - so the shell reaches daylight and the walls round it stand. Everything within one wall
thickness of that plane comes away; it is counted apart from the cavity, so a part too thin to
hollow is still handed back untouched rather than turned into a sheet with a hole in it.

With **Nothing** open the cavity is sealed. A resin print needs a drain hole, which a cylinder
and **Subtract** will cut. A part with no room for the wall you asked for is left solid and says
so.

Hollow rebuilds the surface on a voxel grid, so it rounds anything sharp. For a crisp prism -
a roof that has to keep its ridge, a box that has to mate with a lid - subtracting an inner solid
is still the better tool. This one is for organic and imported shapes.

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
| **Move** (`M`) | A double arrow on each of the six box faces. Drag one to slide along that axis only. | X / Y / Z in mm, and in metres if a scale is set |
| **Rotate** (`R`) | A ring per **world** axis. Drag a ring to turn around it - on the second turn as much as the first. One toggle snaps to 15° steps; the other squares the object up with the world without moving it. | Roll / Pitch / Yaw in degrees |
| **Resize** (`S`) | The object's **own** box with corner markers, turned with it, plus the six axis arrows. Resizing works about the centre, so both faces move, unless **One way only** (`O`) holds the far face still. The lock keeps proportions. | W / D / H in mm, and in metres if a scale is set |

**Keys while resizing.** Hold one while dragging a resize arrow and it overrides a setting for
as long as it is down:

| Key | While held |
|---|---|
| **Ctrl** | Flips **Keep proportions** - uniform if the lock is off, one axis if it is on |
| **Alt** | Flips **One way only** - holds the far face still if that is off, grows both ways if it is on |
| **Shift** | Snaps the size to the nearest whole millimetre, never below one |

They combine - **Alt + Shift** grows one way to a round number - and pressing or letting go of one
mid-drag shows at once, without the mouse having to move. The buttons are left as they were, so
the next drag does what they say. Shift snaps the *size*, not the distance dragged: snapping the
travel would take a 20.4 mm part to 21.4 and 22.4 and never land on a whole number.

**Several objects: as one, or each on its own.** With more than one object selected, a switch
at the end of the bar decides what the handles do and what the numbers mean. On - the default -
the selection is one object: it turns and resizes about the middle of the lot, gaps and all, and
the boxes read the middle and the extent of the lot. Off, each part turns and grows where it
stands, and the boxes show what the parts share.

| Box | As one | Each on its own |
|---|---|---|
| X Y Z shows | the middle of the lot | the value they share, or *mixed* |
| typing `20` | carries the lot so its middle is at 20 | puts every part's centre at 20 - they line up |
| W D H shows | the extent of the lot | the size they share, or *mixed* |
| typing `100` | scales the lot about its middle to 100, gaps included | makes every part 100, each where it stands |
| Roll Pitch Yaw shows | 0 | the angle they share, or *mixed* |
| typing `15` | turns the lot 15° about its middle; the box reads 0 again | sets every part to 15° |

A group of parts turned different ways has no single angle, so as one a rotation box can only
mean *turn by*. Moving a lot and moving each part by the same amount are the same thing, so the
switch changes what a typed position means, not what a drag does.

**Typing a change rather than a value.** Any transform box takes `+=5` or `-=5` to change the value
by that much, and `+5` as a shortcut for the first. A plain number sets the value, negatives
included - positions below zero are normal on a plate centred on the origin, so `-5` has to go on
meaning minus five. With several objects each on their own, the change goes to every one of
them. A box whose parts disagree shows nothing and a quiet *mixed* rather than a 0, which is a
value and invites typing over it as though it were true.

Turning and resizing work in different frames on purpose. The rings are world axes, because
turning *about Z* means the world's Z - it is what the plate is square to. The resize arrows are
the object's own, because the size boxes give its own width, height and depth, and the scale
behind them is applied before the turn. Pointing the arrows along the world while the drag
stretched the object along its own was simply wrong as soon as anything was rotated.

**Align to axes** - the second button in Rotate mode - sets the three angles back to zero
*without moving the object*: the turn is folded into the geometry instead. Use it when a part has
been turned into place and you now want to resize it along the plate. Zeroing the angles by hand
is not the same thing - that swings the object back to how it was built.

With **several objects selected** the readouts still fill in: position shows the middle of the
lot, rotation shows what they have in common, and size shows how far the whole selection reaches.
Typing carries the group there, keeping its arrangement.

Every drag is one undo step, however many mouse-move events it took, and a drag that changes
nothing adds no step at all. Typing into the readout boxes works the same way as the side
panel — the value commits when the field loses focus.

A selected object is drawn with a **white outline** along its own edges and lifted slightly in
colour. The outline traces the shape rather than the tessellation — a cube shows twelve edges, a
smooth sphere none — so it reads clearly without turning round objects into wireframe. Very
dense imports (over 200k triangles) skip the outline and use the colour lift alone.

The status bar always shows the selected object's size, triangle count, volume, and whether it
is watertight.

**Keep on bed**, in the Move and Resize bars, stops anything going below the plate: a part standing
on it grows upward only, dragged or typed alike. It starts on for resizing and off for moving.

The arrow keys nudge the selection along X and Y, and **Page Up** / **Page Down** along Z, by the
snap step or a millimetre with snap off. Holding a key down is still one undo step.

**Split**, **Engrave**, **Emboss** and the other tools that ask in the side panel take the object over while they run: the manipulator bar and
its handles stand down, and the tool's own handles have the object to themselves. Starting one
puts the others away, since each means something different by a click on the model. Cancel or
apply, and the manipulator comes back.

### Repeating along a line

**Repeat** on the Tools tab takes the selection, copies it *n* times with a step in X, Y and
Z, and optionally grows each copy by a fixed amount as it goes. One operation, one undo entry -
a flight of steps, a row of dowels, a run of courses.

**In a grid** is the third way it repeats: rows, columns and layers, spaced either by the gap
between copies or from centre to centre, the whole selection repeated as one block.

The subtlety is which face stays put. Resizing works about the centre, so a run of growing copies
stepped by their centres leaves half of each increment as a gap. Where the run is travelling, the
anchored face is the one it travels away from; where it is not travelling - a flight of steps
grows upward while it moves along - the lower face is the one standing on something, so that is
the one held still. A twelve-step flight lands exactly on `z = 0`.

### Repeating round a circle

The same dialog does rings. **Round a circle** asks for a centre, a radius, an angle between
copies, a rise, and whether each copy turns - a bolt circle, a ring of teeth, crenellations round
a tower, and with a rise, a spiral stair, which the straight repeat cannot make at all.

The radius is filled in from where the selection already stands, so choosing the circle and
pressing Repeat leaves it where it is. Type a different number and the whole ring moves in or
out, **the original with it** - a ring whose first object is still out at the old radius is not a
ring. That move and the copies are one undo step.

**Spread evenly over a full turn** divides 360 by the copies *plus one*, because the original is
one of the objects on the circle: five copies of one thing is six at 60 degrees, not five at 72
with a gap where the sixth should be. Untick it to set the angle yourself and sweep an arc
instead - the summary line says what came out either way.

**Turn each copy to face the centre** is on by default: gear teeth, crenellations, the treads of
a spiral stair. Untick it for a bolt circle, where a round hole does not care which way it
points.

A selection of several objects travels as one rigid group, so the three parts of a bracket keep
their arrangement rather than each snapping onto the circle separately. The ring turns about the
vertical axis only - transforms compose so that a turn about world Z is exactly an addition to
the Z angle, with no matrix to decompose back into Euler angles and none of the trouble that has
near the poles. A radius of nothing is not refused: an off-centre part spun about the middle is a
rosette.

### A stair

**Stair** on the Insert tab takes a rise, a run, a width and a number of risers, shows the
flight on the plate as the numbers change, and builds it as **one closed profile swept sideways**
rather than a stack of boxes. That matters
for what comes next: a stack of boxes has a coplanar seam at every tread, and coplanar seams are
what the boolean struggles with.

The dialog reports the riser and going in real millimetres for the scene's scale, and says
plainly when the result is not something anyone could climb - a riser of 150 to 190 mm on a going
of 240 mm or more is the range it checks against.

### Model scale, and working in real units

**Scale 1:** on the View tab says what the model is drawn to. The list names each standard
rather than leaving you to remember the number - `87 (HO)`, `160 (N)`, `76 (OO)`, `220 (Z)`,
`48 (O)`, `24 (G)`, `35 (military)`, `12 (dolls' house)`, `1 (full size)` - and any other number
can be typed in. Picking one takes effect at once; typing waits until the box is left, so `160`
is not read as 1 and then 16 on its way in.

Set one, and the manipulator bar grows a **second set of boxes in metres** beside the
millimetres, in Move and in Resize:

```
X  0.00   Y  0.00   Z  15.00  mm  |  X  0.000   Y  0.000   Z  1.305  m at 1:87   (Move)
W 110.00   D 12.00   H 30.00  mm  |  W 9.570   D 1.044   H 2.610  m at 1:87   (Resize)
```

The unit carries the scale with it. "m" on its own leaves the reader working out whose metres
they are looking at.

**Sizes read W, D, H; positions read X, Y, Z.** A dimension is not a coordinate, and for a single
object these are its own extents - `SizeX` is the local size, so it does not change when the part
is turned. The letters keep axis order, because the scene is Z-up: width along X, depth along Y,
height along Z, matching the boxes beside them and the coloured gizmo handles. It also means a
glance at the bar says which mode you are in, which two identical `X Y Z` triples did not.

With several objects selected the size boxes describe the bounding box of the lot, so "width"
there means how wide the group sits rather than any one part's own.

The field names automation uses are unchanged - `SizeX`, `SizeY`, `SizeZ` - because every model
script in `tools/ui` sets fields by those names.

They are fields, not a read-out, and **each follows the other**. Type `2.61` into the metres box
at 1:87 and the part moves to 30 mm; type `30` into the millimetres box and the metres box reads
`2.610`. Drag a handle and both keep up. Several objects selected works the same way: the boxes
describe the whole group, and typing into either one carries the parts with it.

Only the millimetres are stored. **The scale changes no geometry and the export is byte for byte
identical** - an STL has no notion of scale, and a slicer reads millimetres and nothing else. The
metres are a view of the same number, which is why they are hidden at 1:1, where the two would
say the same thing twice.

The stair tool uses the same scale to report its riser and going in real units, and the side
panel prints the selection's real size under the size boxes.

### Subtracting with a tolerance

**The last object you pick is the cutter.** Everything picked before it is cut, each on its own
and each kept as itself - same name, same colour, still a separate part. So two halves of an
assembly and one pin is a single operation that leaves two halves, not one fused lump. To do what
3D Builder does - take a shape out of whatever it touches - select all, then click the cutter
again so it is last.

**Subtract...** opens its options on the right rather than cutting straight away, because a
boolean can only be taken back by undo and two of its settings change what comes out.

| | |
|---|---|
| **Tolerance** | how much bigger than the cutter the opening comes out, on every side |
| **Keep what is taken away** | leaves the cutter on the plate afterwards |

> Model the pin at its real Ø3, take it away from the block with **Tolerance 0.2**, and the bore
> is Ø3.4. Change the pin to Ø4 and the bore follows to Ø4.4.

Before this, every fit in a model was two numbers typed in two places and a hope that they stayed
related. The hinged box has four of them - a 3 mm pin in a 3.4 mm bore, a 25.2 mm knuckle in a
26 mm notch, window frames 0.4 under their openings, panes 0.2 under their lights - and not one
of them was derived from the part it had to fit.

The cutter grows in **its own frame**, so a pin lying on its side gains the tolerance on its
radius and on each end rather than on the plate's axes. It does not move: a tolerance grows a
hole, it does not shift it. Zero subtracts exactly as before.

**Keeping the cutter** matters more than it sounds. A pin that bored its own hole is usually a
part in its own right, and the alternative - modelling it twice, once to cut with and once to
print - is precisely how the two stop matching.

**On anything but a cube, a cylinder or a sphere, it asks first.** Growing each dimension by twice
the tolerance is the true offset for those three and for nothing else - on a cone or a pyramid the
sloped surface ends up nearer than asked, by a factor of the cosine of the slope, and a gap
quietly smaller than the number typed is the one direction that jams a printed part. On something
roughly round about its own middle - a gear about its axis - it is a fair approximation all the
same, so rather than refusing, the app says which it is and leaves the choice to you.

A **group** may take one if every member could on its own - a row of dowel pins being the usual
case. Each piece is grown about its own centre rather than the group being scaled as one lump,
which would push the pins apart as well as fatten them and put the holes in the wrong places.
Groups made before this was added do not carry the fact; ungroup and group them again.

### Does it fit?

**Align to** is on the Align tab and **Fit check** on Tools, and the two are easy to confuse
until you notice that only one of them moves anything:

- **Align to** *moves* things. Pick two objects and the first is moved so its bounding box is
  centred on the second's. Nothing is rotated or resized.
- **Fit check** *measures* and changes nothing. It answers the question a render cannot: do these
  two actually meet? It reports either `overlap by 6 x 3 x 16 mm, 0.047 cm3 of shared material`
  or how far apart they are.

Watertight says a part will print. It says nothing about whether two parts go together, and a pin
with no hole under it and a lid resting on its own hinge both look perfectly right on screen.

### Laying a part on its face

**Lay on face** on the Align tab answers the printing question of which way up to put a part.
Press it, click the face you want on the bed, and the object tips over onto it and settles.

It takes the shortest turn that gets there, so the object looks tipped rather than spun, and it
works on a facet of anything round as well - laying it on the tangent there. One click is the
whole job; there is no panel and no second step.

### Rounding edges

**Round...** on the Edit tab rebuilds a **cube** or **cylinder** with rounded edges at a radius
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

**Bevel** instead of round gives each edge a single flat face - a chamfer. It is the same swept
profile run in one step rather than several, so it is exactly a chamfer rather than a coarse
curve.

Closing, opening or starting a new model with unsaved changes asks first, and cancelling the
prompt cancels the whole action — including the window close.

### Emboss: lettering and drawings

**Emboss** on the Tools tab raises words off a face or cuts them into it. Select the object,
press it, click the face, and type: the lettering appears on the face as you set it up, with the
font, height and depth alongside.

Letters are real outlines rather than a bitmap, so an O has a proper hole in it and the result is
a few hundred triangles rather than tens of thousands. **Raised instead of cut** stands the
lettering proud of the face rather than sinking it in. The panel offers every installed font with
a sample of the text in it, bold, italic and letter spacing.

**Keep as its own part** is for printing the lettering in another filament. Cut in, the object
gets the recess and the letters come back as the plug that fills it flush - the very solid that
cut it, so the two fit by construction. Raised, they stand on it as a part of their own. Either
way the letters go onto the next filament up, and the pair export to a 3MF that says so.

For lettering that is an object of its own - a sign, a name tag - use **Text** on the Insert tab
instead; see [Text](#text) below.

#### A drawing instead of words

**Drawing...** in the panel stamps the filled shapes of an **SVG** file - a logo, a badge, an
icon - and the panel shows the shape it read so you can see it arrived with its holes intact
before committing to it. **Use text** goes back to typing.

A drawing becomes the same thing typed letters become: an outline and the loops inside it. So
everything else in the panel applies to it unchanged - the size, the depth, the bevel, the wrap,
the handles on the face, and the choice of cut or raised.

Paths, rectangles, circles, ellipses, polygons and polylines are read, with `transform` followed
down the tree and curves and arcs flattened. Two things are not: **text**, which needs the same
fonts to mean anything (turn it into paths in the drawing program first), and `use`/`symbol`
references. A drawing of nothing but unfilled lines has no area and comes back empty.

#### Putting it where you want it

Two handles sit on the lettering itself: a **blue grip** in the middle slides it about, and an
**orange knob** turns it. A dashed box shows what it covers. **Across**, **Up** and **Turn** in
the panel are the same three numbers, so a drag can be tidied up by typing, and every field takes
arrow keys and the wheel.

Clicking the object again re-anchors the lettering to where you clicked; clicking a *different*
face starts fresh.

#### Wrap

| Wrap | What it does |
|---|---|
| **Planar** | Flat on the face, which is what most lettering wants |
| **Cylindrical** | Bent round the object as a barrel - a ring, a cup, a bottle |
| **Spherical** | Laid over it as a ball |

The curved ones are anchored where you clicked rather than at the object's middle, so a barrel
picked on its side is lettered through that exact point. Across is measured as **arc length**, so
letters keep their proportions whatever the radius, and a word long enough to go right round
meets itself instead of piling up.

Wrapped lettering is broken up finely enough to follow the curve - but only where it actually
strays from it. Nothing bends along a barrel's axis, so a step straight up one is left alone
however long it is. That is the difference between a couple of thousand triangles and a hundred
and fifty thousand.

#### Bevel

**Bevel** draws the far end of the lettering in, sloping its walls. It is worth having for
printing rather than for looks: upright walls leave raised lettering a step for the first layer
to bridge, and cut lettering a slot exactly one nozzle wide. A bevel wider than the stroke would
swallow it, so a stroke too thin to slope keeps its upright walls rather than closing up.

For an FDM print, the same limits apply as to engraving: **0.4 mm deep** is two layers, and
strokes thinner than a couple of nozzle widths will not slice. Bold at 8 mm or more is a safe
starting point.

### Engraving a pattern

**Engrave** on the Tools tab puts a repeating pattern on one flat face - brickwork on a
wall, boards on a shed, lap siding on a gable.

Select the object, press **Engrave...**, then **click the face** you want. It highlights in
blue, and the panel on the right fills in with the face's size and how many grooves the current
settings would cut. Click a different face at any time to move the pattern; **Cancel** leaves
the mode.

| Pattern | What you get | Size means | Starts at |
|---|---|---|---|
| **Brick** | Running-bond masonry: level courses, joints staggered half a brick | Brick length | 3 : 1 |
| **Roof tiles** | The same stagger, squarer, with a heavier line under each course where it laps the one below | Tile width | 1.5 : 1 |
| **Tiles** | Stack bond - courses and joints both lining up. Wall tiles, floor tiles, ashlar | Tile width | 1 : 1 |
| **Planks** | Long boards with their end joints staggered: decking, floorboards, siding | Board length | 8 : 1 |
| **Wood grain** | Flowing grain that parts around knots, with a ring or two marking each one | The spacing between grain lines | - |
| **Stripes** | Evenly spaced parallel grooves - lap siding, panelling, ribs | The gap from one groove to the next | - |

**Shape n : 1** is how many times longer each piece is than it is tall. Each bond starts at its
own - a brick is not shaped like a roof tile, and the first roof built here came out with
39 x 13 cm tiles because it was laid as brick. Changing the pattern resets it, so picking
**Roof tiles** gives roof tiles rather than bricks in a different colour.

**Line width** is how wide the cut lines are and **Depth** how far they go in. Planks, grain and
stripes can run either way across the face; courses of brick and tile are level by definition.

The pattern is drawn **on the face as you set it up**, from the same code that builds the cutter,
so what you see is what gets taken away. Every field takes **arrow keys and the mouse wheel** as
well as typing - Shift for 10 mm steps, Ctrl for 0.1 mm.

#### A wall that already has windows in it

A raised pattern goes on a face **after** its openings are cut. The face's boundary is walked as
several loops rather than one: the widest is the outline and the rest are holes, and the grid
takes each hole's own edges as grid lines, so the courses meet the reveal exactly and a brick
standing at the edge of an opening gets its return while the flat face beside it does not.

A hole has to be a rectangle standing clear inside the patterned area, which is what a window and
a door are. A **round** hole - a boss or a peg merged onto the face - is not, and the face is
refused rather than patterned badly. Pattern such a face before the round detail goes on.

#### Making a corner meet

Two walls will not line up by themselves. Each face measures its pattern from its own bottom-left
corner, and which corner that is depends on which way the face points, so the courses on adjacent
walls start at different places and the joints miss each other where they meet.

**Shift across** and **Shift along** slide the pattern over the face to fix it, and the **blue
grip** on the face is the same two numbers under the pointer. Engrave the first wall, then on the
second drag the grip - or nudge the fields with the arrow keys - until the preview's joints line
up with the wall you have already cut. Shifting by a whole repeat changes nothing, so there is
only ever a fraction of a brick to find.

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

Wood is built differently from the other two. Brick and stripes are rectangles; grain is a set
of curves. The curves are generated freely and then walked in order with a minimum gap enforced
between neighbours, because two of them crossing has no sensible answer. Where the gap bites, the
grain flattens slightly - it can never fold.

#### Raised instead of cut

**Raised instead of cut** stands the pattern off the face rather than sinking it in: bricks with
mortar between them, boards with a gap, grain standing proud like the hard rings of a weathered
plank. It prints better than a cut pattern - the nozzle lays a raised line down where it has to
miss a groove of the same size - and it is the mode to reach for on anything with four walls.

A raised pattern on a plain flat face is not a boolean at all. The face's own triangles are
thrown away and laid again around the pattern: a frame joining the pattern to the face's real
outline, the pattern itself at two levels, and a wall where the two meet. Every vertex on the
outline is one the model already had, so nothing else in the object is touched. The result is
watertight by construction rather than by repair - and, because nothing accumulates, the fourth
wall of a box is no harder than the first. It used to be impossible.

Cutting still goes through the boolean, because a cut groove is allowed to run off the edge of
its face and a raised one is not.

A pattern cut with the boolean leaves the object plain geometry - as after a Merge, it can no
longer be rounded.

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

Select one or more objects and press **Split** on the Edit tab. Everything else on the plate
stands down while you aim: a plane is aimed by eye, and on a plate of any depth the thing being
cut is behind something else. So does the rest of the app - the ribbon, the object list and the
property boxes grey out until you press **Split** or **Cancel** - because nothing should be able
to delete the object out from under a cut that is halfway aimed. `Esc` puts the tool down.

The plane has a bar of its own at the foot of the viewport, with the same two modes the old app
used:

- **Move** slides it along its own facing. Drag the blue arrows, or type the offset in
  millimetres.
- **Rotate** turns it. Three rings, one per world axis, and three boxes reading the same turn as
  **roll**, **pitch** and **yaw**, to a tenth of a degree. **Snap to 15°** is a toggle beside
  them, and turning it off is what a 22.5° joint needs. A plane has no third degree of freedom,
  so turning it about its own facing is allowed and changes nothing - in the same way that
  spinning a cylinder about its axis does not move it.

Clicking a face on the model puts the plane on that face, which is usually quicker than aiming
it. Then choose which side to keep; the buttons are named after the direction the plane actually
faces, so a horizontal plane offers **Top** and **Bottom** rather than an abstract front and back.

While you aim, the half that would be thrown away can be **shown**, **faded** or **hidden**, and
the face the cut exposes closed over or left open to look into. That preview is the cut itself,
not an impression of it: the same code, on a throttle set by how long the last one took, so a
light model follows the plane about and a heavy one catches up a few times a second.

The arrows and the rings keep the plane on the solid: however far it is dragged or tilted, it
stops at the last place it still cuts something, rather than sliding off into empty air.

The plane belongs to the scene rather than to an object, so it cuts **everything selected** in
one stroke and one undo step - which is how an assembly gets sliced in half. Its travel and its
handles are sized to the whole selection, and objects the plane misses are left alone rather than
being dropped. Both halves come out capped and closed.

### Split with connectors

**Split with connectors** on the Tools tab is the same split with one more section in its panel:
the two halves come back with something to hold them together, so they go back one way only.
Both halves are always kept.

- **Separate pins** - holes in both halves, and the pins made as parts of their own beside the
  model, lying down, so they print strong along their length. A broken pin is a reprint, not a
  lost half.
- **Pegs on one half** - pegs standing up out of the *lower* half and sockets in the upper one.
  Nothing loose to lose. Always the lower half: pegs on the upper one hang off its underside and
  cannot be printed without supports.
- **Brick studs** - studs on the lower half and a shallow brick underside in the upper one, on the
  8 mm grid, cut from the studs' own cells as a real brick's is. The halves hold by grip, as bricks
  do, and each takes real bricks along the join. A stud goes only where the other half can take its
  socket, and the narrowest wall that takes one is the real 7.55 mm. The grid repeats, so line the
  edges up by eye.

Pins and pegs can be **round** or **square**; a square one gets a square socket, and is kept further
from the edge for its corners.

They run **square to the cut**, **vertical** or **horizontal** - vertical keeps the top half of a
tilted cut lifting straight off. A direction that would run along the cut rather than through it
(vertical pins on a vertical cut) is refused before anything is cut.

| Setting | Meaning |
|---|---|
| **How many** | at most this many; a face with room for fewer gets fewer, and says so |
| **Diameter** | of the pin or peg itself |
| **Depth** | how far a connector goes into each half |
| **Clearance** | per side, as for Subtract: a 5 mm pin with 0.2 goes into a 5.4 mm hole |
| **From edge** | solid left between a hole and the outside of the part |

**Where they go.** Only where there is solid round them - inside the cut face, outside any hole in
it, with the wall asked for on every side, and with that much solid all the way down to the
connector's depth rather than only at the face. Then spread evenly: each pin goes to the middle of
its own share of the face, and from there out towards the edge until the wall left beside its hole
is the **From edge** asked for. Four on a square land towards its corners, three on a disc go
evenly round it, one stays in the middle. Registration is better the further apart they are.

While the panel is open every connector is marked on the cut face and the half above it is
faded, so what the settings do can be seen before anything is cut.

**Fit test** on the Insert tab is for choosing the fit before trusting a big print to it: four
small 2x2 brick plates at -0.2, -0.1, 0 and +0.1 mm, marked with one to four notches. Print them
in your filament and press each onto real bricks - or onto its twin, for two halves as Split
with connectors makes them - and type the fit that holds firmly but still comes apart by hand.

If not one connector fits anywhere, **nothing is split**, and the message gives the two numbers
that matter: how thick the solid would have to be, and how thick the cut face actually gets. A
hollow box with 3 mm walls will not take a 5 mm pin, and it says so rather than splitting plain
with no reason given.

### Connect objects

**Connect objects** on the Tools tab is Split with connectors with the split already done. Select
two parts that rest on each other - one standing on the other, or side by side - and pins or pegs
go through the face they share, with the same settings and the same rules. A connector goes only
where **both** parts have material: on a floor resting on a basement, that is the basement's walls.
Each part is kept as itself, with its name and colour.

The shared face is found from the two parts' boxes where it can be, for parts square to the plate
and no more than 0.5 mm apart. Where the boxes find nothing - a can standing in a dish, a lid in a
pot's mouth, a spigot in its counterbore, anything sitting down into a recess of the other - the
flat faces are read off both shapes themselves and matched up. Parts turned at an angle get no
shared face rather than a wrong one. A joint thinner than a printable pin - under 3 mm of wall, as
at the joints of a 1:87 house - gets a message rather than a pin cut through its side.

### Extrude down

**Extrude down** on the Tools tab is for a model with the right top and the wrong bottom: a
scanned bust ending in a ragged neck, a relief that is only a skin, a figure tilted until one toe
touches the plate. Set a height - drag the arrows on the plane or type it - and everything below it
is replaced by straight walls down to a flat base on the plate, following the model's outline at
that height. A section with a hole in it keeps the hole all the way down, so a hollow neck stays
hollow.

It cuts first rather than extending whatever the bottom happens to be. That is what 3D Builder
did, and it carries a ragged edge all the way to the plate; cutting above the ragged part and
building down from a clean section is the difference between a plinth and a skirt. A height that
misses the model, or a section that does not close, is refused - a base on a torn model is still a
torn model.

### Dropping files on the window

Drag a `.3dfc`, `.stl`, `.obj` or `.3mf` onto the window, one file or several, and it asks which of the
two things you meant: **open as a project**, putting its plate up in place of this one, or
**import onto this plate**, keeping both. It asks rather than guessing because guessing the first
way throws out work that was never saved. Opening is only offered for a single project file;
everything else can still be imported, and several files land in one undo step.

### Holes

**Hole** on the Insert tab makes a screw hole - **plain**, **countersunk** for a flat head, or
**counterbored** for a socket cap to sit below the surface, with a **nut pocket** at the far end if
asked - or a pocket for a **heat-set insert**, the brass thread pressed in with a soldering iron
that survives being undone many times. M2 to M8.

It comes as one hole, a **row** along the cutter's own X, or a **bolt circle**, all set out round the
same handles, so moving or turning the cutter moves the lot. **All the way through** is measured to
the far side along each hole's own axis, not the part's diagonal. **Clearance** goes on every
diameter, since printed holes come out small - but not on an insert's pocket, which should be
tight.

With one part selected the cutter starts sunk into its top, and **Cut** takes the holes out where
the cutter stands. **Add** puts the cutter on the plate instead, to Subtract later from this part
or another. A cut that would not leave the part closed is refused.

By hand, a hole is a cylinder and **Subtract**: the last object picked is the cutter, so select the
part, then the cylinder, and press it. The status bar should then read *"Watertight - ready to
print"*.

### Threads

**Thread** on the Insert tab makes a **threaded rod**, a **bolt** with a hexagon or round head, a
**nut**, or a **hole cutter** to take a threaded hole out of a part - ISO metric coarse M3 to M20,
which fills in the pitch and the ISO 4032 nut's size, or **Custom** for any diameter and pitch.

The profile is the ISO 68-1 basic one, right-handed, with its depth fading in over a pitch at each
end so the thread starts cleanly. **Clearance** is on the diameter, half off the rod and half on
the nut, so a printed rod and a printed nut of the same size screw together; 0.2 suits a well set
up printer, and 0.3 to 0.4 is the next thing to try if they bind. A bolt is made head down, still
right-handed.

With a part selected, a hole cutter starts sunk into its top and **Cut** takes the threaded hole out
where it stands. It is built as a surface of its own rather than with a boolean, so it closes by
construction and follows the numbers as they are typed. The panel says when a pitch under 1 mm will
be hard for a filament printer, when a nut was widened to keep its wall, and when a clearance is too
wide for the threads to engage.

### Gears

**Gear** on the Insert tab makes a **gear**, a **ring gear**, a **rack**, a **bevel**, a **worm** and
its wheel, or a **ratchet** and its pawl, with the result on the plate as the numbers change. Spur
gears, rings and racks can have **straight**, **helical** or **herringbone** teeth.

The teeth are not drawn from the involute formula but generated the way a gear is cut: by rolling
the standard rack round the pitch circle and keeping what it never reaches. Above the base circle
that gives the involute anyway; below it, it gives the curved root a real cutter leaves, and on a
small pinion the undercut that lets its partner's tips pass. A gear drawn from the formula alone has
straight roots, and a pinion of a dozen teeth jams against them. The solid is built straight to a
closed surface, with no boolean but the set-screw hole.

| Setting | Meaning |
|---|---|
| **Module** | The size of a tooth: the pitch diameter is the module times the teeth. Gears mesh only with the same module; 1 to 2 prints well on a 0.4 mm nozzle |
| **Pressure angle** | 20 degrees almost everywhere; two gears must share it |
| **Backlash** | The play between two meshing gears, along the pitch circle. Each is thinned by half of it |
| **Bore** | Round, D-shaped for a motor shaft, or hexagonal for a hex shaft or a nut pressed in |
| **Hub** | A collar on top round the bore, with a hole across it for a grub screw onto the flat |
| **Chamfer** | Draws the bottom edge of the teeth in, against the first layer spreading |

**A partner in mesh.** Ask for a meshing gear and it is made at the right distance and turned into
mesh - beside a gear, inside a ring or on a rack - leaning the other way for helical teeth, with the
backlash shared between the two. It can have its own thickness and shaft: a pinion on a 3 mm arbor
often drives a wheel on a 5 mm one. A bevel pair meets at a right angle, a worm's wheel is helical
at the worm's lead angle and as wide as the worm asks for, and a ratchet's pawl comes with its pivot
placed. A pair is shown as it goes together and made as it prints, which for a bevel, a worm and a
pawl are not the same place.

The gear marks its own bore and puts its pivot there, so it is measured and turned about its shaft
rather than about the outline of its teeth.

#### Cut away, and the frame it drives

**Cut away to** keeps teeth on part of the rim only and takes the rest down to the roots: the
gear that drives something one way, lets go, and catches it again. The first and last teeth are
shortened, to come back into mesh cleanly.

**Reciprocating frame** makes its mate the closed frame such a gear drives to and fro - the usual way
to get a back-and-forth stroke from a motor that only turns one way. The gear turns on the spot
inside it: it pushes one run, the frame parks while the bare rim goes by, and then it pushes the
other run back.

The frame is **generated rather than drawn**, the way the teeth are: the gear is run through a
whole turn of the motion it is meant to give, and the frame is everything it never reaches, less a
clearance. There is no phase between the two runs to get wrong, so an odd tooth count works as well
as an even one, and only the teeth that are actually used are cut. How far it slides comes from the
involute rather than a count of teeth: a sector stays in mesh past its last tooth by its contact
ratio, and that tooth's tip then drags the rack on until it slips off - five teeth of module 2
slide 42.7 mm, not the 31.4 that five pitches would say. A sector so long that it leaves the frame
no time parked is refused, with the most teeth that fit.

A parked frame would still slide back freely along the path it came by, so there is a **lock**, in a
layer of its own on top of both parts: a disc on the gear, and on the frame a rail either side of
the path with a pocket round each end. The disc's lobes sit in the pocket while the frame is parked
and hand it from one to the other, and notches in the disc let the rails through during a push -
the Geneva drive's trick, applied to a slide. The rails are drawn and the disc is generated from
them, which is what keeps the rails' corners whole to hold with.

| Setting | Meaning |
|---|---|
| **Ends** | **Round** or **square** outside; the inside is made from the gear's path either way |
| **Clearance** | The gap left all round - teeth, ends and lock alike - for the printer's slop. 0.2 to 0.3 mm suits a 0.4 mm nozzle |
| **Lock** | How tall the lock layer is; 0 for none, and then nothing but friction holds the frame while it is parked |

The panel says how far it slides, what share of each turn it moves and parks for, and how tightly
the lock holds it. The gear prints teeth down with its lock on top; the frame prints with its lock
side down and is turned over to go round the gear, so neither needs support. It is made to turn
anticlockwise, seen from above as it prints. Its arms are a plain bar: add one with a cube and
**Merge**.

### Sketch

**Sketch** on the Insert tab draws an outline on the plate, seen from above, and makes a solid of
it. **Line**, **arcs** that join the lines, smooth **curves** through clicked points, **freehand**
strokes trimmed where they overrun their start, **rectangles** and **circles** - snapped to a 0.5,
1 or 5 mm grid, or not at all.

An outline inside another becomes a hole in it, decided by which lies inside which rather than by
which way it was drawn; outlines that cross are refused, since a crossing has no inside. Any point
that was clicked can be dragged afterwards, and an outline that the move makes cross itself or
another is put back. **Enter** or the right button closes the outline, **Backspace** takes back the
last point, and **Load SVG** brings in a drawing's filled shapes at a width you give, its corner on
the origin so it revolves cleanly.

**Extrude** stands the outlines up to a height; **Revolve** turns them about Y, standing up, or
about X, lying along it, through part of a turn or the whole of it. Both are built as closed
surfaces with no boolean.

### Text

**Text** on the Insert tab is lettering as an object of its own - a sign, a name tag, a keychain -
in any installed font, bold or italic, with a height, a depth and extra spacing between the letters.
It lies flat on the plate to read from above, which is how lettering prints best, stands up to read
from the front, runs **round a circle** on the plate - a clock face, a coaster, a ring of words - or
wraps **round a cylinder** with each letter keeping its width, for a cup or a napkin ring. Round a
circle can face in, running round the bottom, for the other half of a badge or a seal.

To put letters on a face that is already there, use **Emboss** or **Engrave**.

### Custom shapes

**Custom** on the Insert tab is a cube, cylinder, cone, sphere or torus with its size, its number of
segments and the roundness of its edges chosen before it is made - more segments for a smoother
curve. Cylinders, cones and spheres from the ordinary buttons come with 100 segments already.

### Twist, taper and bend

Three tools on the Edit tab reshape a part as a whole, each previewed on the plate as it is set:
**Twist** turns it about its upright axis by more the higher up it goes - twisted columns, vases,
spirals; **Taper** narrows or widens it towards the top - spires, flared vases; **Bend** curves it
over towards X or Y as if round a pipe - arches, horns, hooks. The bottom stays put in all three.

### Hull

**Hull** on the Tools tab wraps the selection in one skin, as cling film pulled tight round it: two
cylinders make a slot, a row of spheres a rounded bar. It replaces what was selected.

### Lithophane

**Lithophane** on the Insert tab is a photograph carried as thickness in a thin plate: thin where the
picture is white and thick where it is dark, so it shows only when it is lit from behind. It is
still marked **beta** - check what it gives you before printing it.

Because a lithophane unlit is a grey slab, the panel shows a second preview lit from behind, from a
tone chain that inverts the way light fades through plastic, so what it shows is what a lamp will
make of it. **Brightness**, **contrast**, **gamma** and **negative** adjust the picture; **thinnest**
and **thickest** set the range, 0.8 mm suiting most printers at the thin end; a **frame** round the
edge stops the light and stiffens the plate; and it can be **flat** or **curved** round a cylinder
for a shade with the lamp inside. From the layer height you print at, it says how many greys will
survive. While the panel is open the plate shows the lithophane and nothing else.

### Voronoi

**Voronoi** on the Tools tab cuts the selection into a web of struts along the walls of a Voronoi
tessellation - also **beta**. **Shell** keeps a web following the surface with nothing behind it:
the openwork lamp, the vase, the bust. **Lattice** runs struts every way through the solid under a
skin, a foam that takes weight out of a part that still has to be stiff.

Set how many **cells**, how wide the **struts** are - two nozzle widths is the floor - and how much
of the bottom stays **solid** so it stands and has a first layer to print on. It is worked out on a
voxel grid, so it always comes back watertight, and anything finer than about three voxels comes
out lumpy; **Smooth the voxel steps off** takes the grid's ripple away without moving the shape.

### Setting a pivot

An object is measured and turned about the middle of its box - which is the wrong place for a gear,
whose shaft is at its centre, or for a door, which swings on its hinge. **Set pivot** on the Edit tab
lets you click the point instead: a shaft hole, a corner, a hinge line. The position boxes then read
that point, a turn goes round it, and lining it up with another part compares it rather than the
middle of the box. **Pivot to centre** puts it back. The pivot is kept with the project.

### Aligning to a face

**Align to face** on the Align tab lines a selection up on a face: pick a face on any object, then
choose for X, Y and Z whether the selection's near edge, middle or far edge should land on the
middle of that face. **Centre face to face** picks a face on the selection and then one on something
else, and moves the selection so the two faces' middles meet on the axes you tick - how a lid is
centred on an opening or a flange on its mate. The face under the pointer lights up as you pick,
and both stay marked, green on the selection and amber on the other. In both, the selection moves
together, keeping its own arrangement, and nothing is turned or resized.

The axis buttons further along the Align tab match everything to **the last object picked**, the
same one Subtract and Align to treat as the anchor. With one object selected they align it to the
bed instead.

### Fitting the bed

**Fit** on the Align tab shrinks the selection to the printable area from **Grid**, 20 mm clear of the
edge, stands it in the middle of the bed and zooms to it, saying by how much it was scaled. It only
ever shrinks: nothing is made bigger. **Distribute** sets the selection out in rows across the bed a
gap you choose apart, moving as the gap is typed, packed into whichever width comes out closest to
square rather than always filling the bed's whole width.

### Blueprint

**Blueprint** on the File tab (`Ctrl+P`) lays out a drawing of the selection, or of everything: front,
top and right views and an isometric, first or third angle, with hidden lines dashed and lines that
coincide drawn once. Every straight edge square to an axis is dimensioned in a chain along each
view, with the overall size stacked further out; a curved or slanted edge - a hole, a fillet - is
left alone rather than dimensioned wrongly. It is laid out at a standard scale, or real size, in the
current units, with a title block, and printed as vectors - to paper, or to a PDF through Microsoft
Print to PDF.

### Making a mould

**Mould...** on the Tools tab takes one object and builds a mould to pour silicone into: a block
with the model taken out of it, cut so the casting comes out, with registration keys, a pour hole
and a vent at each pocket it can see would trap air.

It works out how the mould should come apart before it asks anything. Every line of sight through
the model is followed along each axis, and a line that leaves the material and enters it again is
an undercut - somewhere the mould would have to lift off a surface that has something over it. The
axis with fewest of those wins, and the cut goes at the widest cross-section, which is what
practice says and what keeps the opening as large as it can be.

That test is worth stating because it gets rings right. A ring pulled *across* its hole is an
undercut on almost every line; pulled *along* the hole there is not one, and it splits perfectly.
The same reading tells you when nothing works - a closed shell is trapped whichever way it is
pulled - and then a second cut is offered, and a third, giving four pieces or eight.

The dialog says what it found before you commit to anything:

```
Cut on X at -19.44 mm - 0% of it is undercut, which silicone will flex out of. Two parts.
X: 0% undercut, opening 14408.5 cm2   Y: 2% undercut   Z: 45% undercut
```

Undercuts are reported rather than refused, because the casting is silicone and silicone bends out
of a shallow one. A rigid two-part mould would not.

**Air is a drainage question, not a high-points question.** Air under the cavity ceiling slides
along it, so a flat region joined to something taller is not a trap at all - it runs sideways and
escapes up. What traps it is a stretch of ceiling with nothing higher anywhere along its edge, and
that is where the vents go. The pour hole goes at the model's own highest point, and on a ring that
means on the ring rather than over the hole in the middle of it.

That test finds flat traps and misses curved ones. A nose, or the underside of a collar, is a high
point of the ceiling without being a level stretch of it, so the scan walks past it and reports no
trap at all - which is why a bust can come back with no vents in it. Look over the overhangs on the
cut face before pouring; the rule wants replacing with a local-maximum test, and has not been.

**Two routes, chosen by whether the model closes.** A watertight model is cut exactly at any
size, and the cavity keeps every triangle it had: the block with the model inside it turned inside
out is two shells, one within the other, and there is no arithmetic in it to be slow or to tear.

It used to be chosen by size, and the boolean set the limit - a thousand triangles took half a
second, sixteen thousand took forty-seven, and a three hundred thousand triangle scan ran for
twenty minutes and twenty gigabytes before it had to be killed. That boolean is gone from the
cavity, and with it the limit.

What still goes to the grid is a model that will not close, because a surface with a hole in it has
no inside for the void to be. The grid does not care: it asks whether points are in the material
and rebuilds a surface from the answers, at the cost of the detail the sampling drops. The dialog
says which route this model will take and what the sampled detail works out to in millimetres, so
repairing it first is what gets an exact cavity.

The pour hole and the keys are **square**, which is not laziness. A round bore meets a curved cavity
along every one of forty-eight facet edges at whatever angle the surface makes there, and a ball
meets a flat face tangentially all the way round its rim - both the contacts this boolean handles
worst. Four flat faces meet a curved surface in four clean curves. Making them square took a
two-part mould from both halves torn to neither.

### Stopping a long operation

Merging, engraving, lettering, splitting, hollowing, simplifying, rebuilding and repairing all
run off the UI thread. While one does, a panel covers the window with what is running, how long
it has been going, and an **Abort** button. Escape does the same.

**Aborting changes nothing.** Every one of these builds its result on a background thread and
only reaches the scene once that returns, so there is never a half-applied model to recover from
- the status bar reads *"Subtract aborted - nothing was changed"* and the objects are exactly as
they were. That is why the button is offered without a warning first.

Stopping is cooperative, so it takes as long as the work takes to reach its next look at the
token: inside a boolean that is once per node of the tree and every thousand polygons within a
node, during a rebuild or a hollow it is once per grid slice, while repairing it is between each
mending step, and while simplifying it is every thousand collapses. In practice it is immediate.
The panel says *Stopping* rather than simply vanishing, because a button that appears to do
nothing gets pressed again.

The window is genuinely blocked, not merely dimmed. The panel takes the mouse; the keyboard is
shut separately, because a shortcut does not need the pointer - `Ctrl+Z` during a rebuild would
otherwise undo the step the rebuild is about to replace.

### If the app does not close properly

A copy of the scene is kept while you work, and taken away when you save or confirm closing. So
whatever is left behind is the work of a run that never got to close - a crash, a power cut - and
the next start offers it back. Two copies of the app open at once do not offer each other's live
work.

### Export session and Record

**Export session** on the File tab writes out every undo step since the plate was last empty, one
file per step, oldest first and named for what that step did - as `.3dfc`, `.3mf` or `.stl`. It
does it by stepping back through the real undo history and forward again, and leaves the plate
exactly as it found it.

**Record** takes a screenshot after every action, to a folder you choose, until it is turned off -
and for a tool with an Apply button, one just before it too, so the tool's panel is caught as it was
set rather than after it has done its work. The two share a number and the step's own name. It is
for building up a set of pictures of the tools in use.

## Formats

| Format | Notes |
|---|---|
| `.stl` (binary, default) | Smallest and fastest. Objects merge into one triangle soup — STL has no notion of separate parts. |
| `.stl` (ASCII) | Human-readable, roughly five times larger. |
| `.obj` | Keeps objects named and separate, and writes a `.mtl` sidecar with colours. |
| `.3mf` | 3D Builder's own format and what slicers prefer: separate parts, names, colours, units and each part's filament in one file, with a picture of the contents so Windows shows a thumbnail. |
| `.3dfc` | The project format: GZip-compressed JSON that keeps objects and transforms editable. |

**Import** takes all four. An STL, OBJ or 3MF arrives as new objects on the plate - a 3MF with its
parts' own names and colours, placed where its build list put them, assemblies saved by a slicer
flattened into one part each, and a file in inches or metres converted to millimetres; a `.3dfc`
**joins** what is already there, keeping its own objects, names, colours and positions - which is
how two projects are brought together, since **Open** would replace the plate instead.

**Export** opens an options dialog before the file dialog:

- **What to export** — everything on the plate (the default) or just the selection. Scope is
  asked for rather than inferred from what happens to be selected, because a model that quietly
  lost half its parts is only discovered in the slicer.
- **Format** — binary STL, ASCII STL, OBJ or 3MF.
- **Drop to the build plate** — moves the whole export together so its lowest point rests on
  Z = 0, keeping the parts in their relative positions.

The dialog reports the triangle count, volume, bounding size and whether the result is
watertight before anything is written. Exporting warns — but does not block — if it is not.

## How it works

The modelling core (`Geometry`, `Model`, `Io`) contains no renderer types at all; only
`Render/` knows about Direct3D. That separation is what keeps the viewport swappable.

**Boolean operations** go to [Manifold](https://github.com/elalish/manifold) first, through the
ManifoldRust package, and fall back to a hand-written BSP-tree CSG engine (`Geometry/Csg/`) when
the native library will not load or a solid is not closed. Manifold does not slice the solid along
the tool's planes, which is what made the BSP engine tear fine detail against a curved surface -
and slow: it answers a heart embossed on a cylinder in milliseconds where the BSP took half a
second and tore. The BSP engine remains for everything Manifold declines.

**Splitting with a plane does not use it.** It did - the cut was a boolean against an oversized
half-space box, so the cut faces came out capped for free - and on anything dense that went badly:
a 214,000 triangle scan cut through the middle took twenty seconds, gave back two and a half
million triangles, and left nearly twenty thousand open edges in one half. The BSP is fragile
against dense curved surfaces and a plane meets a scan everywhere at once. `PlaneClip` cuts the
triangles instead, in a twenty-fifth of a second, and stitches the opening shut from the edges the
cut left - chained into rings and filled by the same triangulator the lettering uses, holes and
all. The boolean is kept as a fallback for the one thing it is better at: it rebuilds the surface
rather than cutting it, so it can close a solid that arrived slightly open, which is what the
mould needs when it splits its own output.

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

**Lettering never learns about the shape it is going onto.** Outlines are laid out flat, in
millimetres, and a separate `IPlacementSurface` says where a flat point ends up in space. That is
what lets the same letters go onto a face, round a barrel or over a ball without the glyph code
knowing which, and it is what lets the placement handles serve the engraving tool as well — the
gizmo asks the surface where a layout point lands and how far a millimetre of layout carries on
screen, and nothing in it knows whether it is placing a word or a brick course.

The surface also answers **how far a straight step strays from it**, which is what decides where
the lettering gets broken up. Measuring the step's *length* instead is the obvious thing and is
wrong: nothing bends along a barrel's axis, so a long step that way is already perfect while a
short one round the barrel is not. Splitting by length quadrupled the whole solid to fix a
handful of edges — 147,456 triangles and 24.8 seconds for one short word, with thousands of torn
edges in the result. Splitting only what strays gives 1,168 triangles and 124 ms.

**A boolean leaves T-junctions, and no amount of repairing will mend them.** Where it splits one
polygon it need not split the one beside it in the same place, so a long edge on one side ends up
facing two shorter ones with a vertex partway along it. The surface is closed to look at and
prints perfectly; it is not closed as a list of triangles. Repair could see nothing wrong -
there is no hole and nothing is inside out - so a fifth of all rounded-box booleans came back
reported as broken and could not be fixed. `MeshStitch` splits those edges so both sides share
their corners, which cannot change the shape: the corners were on the edge already.

Two attempts that did not work are recorded where they were made. Splitting one edge per triangle
per pass never settles, because the two triangles either side of an edge each split whichever of
their own edges came first. And sizing the vertex grid by the cube root of the count leaves
scores of vertices in every cell, since a mesh's vertices sit on a surface rather than filling
the space - 139 seconds on an 84k mesh, against 569 ms once it was square-rooted.

**GPU use is confined to rendering.** Direct3D 11 via `HelixToolkit.SharpDX.Core.Wpf` gives
MSAA and handles imported meshes of millions of triangles. Booleans stay on the CPU
deliberately: BSP tree work is serial and branch-heavy, and GPU voxel/SDF booleans — robust as
they are — produce resolution-limited, stair-stepped output, which is the wrong trade for clean
primitives headed to a slicer.

## Known limits

- Boolean operations on imported meshes above ~200k triangles can be slow, above all when
  Manifold declines them and they fall to the BSP engine (the app warns on import). The GPU
  renders them fine; the boolean is the bottleneck. **Abort** stops one that is
  taking longer than it is worth, and leaves the model untouched.
- `SharpDX` 4.2.0 is unmaintained upstream. It works on Windows 11 and is fully managed, but it
  is the one long-term liability — the renderer-agnostic core is the insurance.
- Engraving covers the face's **rectangular extent**. On anything convex the overshoot
  hangs in mid-air and cuts nothing, so a gable end or a round cap comes out right, but a
  separate face lying in the same plane and inside that rectangle is engraved too, and an
  inside corner loses half a millimetre off the neighbouring face.
- **A fine pattern cut into a face that has been cut about already** can refuse to go on. Every
  earlier pattern left a notch wherever a groove met an edge, and the new one has to be cut
  around them; where two faces meet exactly rather than crossing, the boolean tears. It is
  retried from a slightly different pattern first - the status bar says when that happened -
  but it does not always get through. A coarser pattern, a shallower depth, or nudging
  **Shift across** by a tenth of a millimetre usually does. **Raised instead of cut** does not
  have this problem at all, since it never touches the boolean.
- **A drawing whose shapes overlap each other** is stamped the slow way, and may not go on.
  Which loops are holes is decided by asking whether one lies inside another, and for outlines
  that cross, that question has no answer - so it falls back to the boolean, which builds each
  shape separately and puts the walls of one inside the other. Merge overlapping shapes in the
  drawing program first.
- **Letters with an enclosed middle - O, B, A, D - wrapped round a barrel** are past what the
  boolean will do: the cutter is sound but the result comes back torn. It is refused rather than
  shipped, so the object is left as it was. Lettering without counters wraps fine, and **Rebuild**
  on the Tools tab remakes a shape the boolean has given up on.
- Engraving and lettering are refused outright rather than applied badly whenever the result
  would not be watertight. The object is never left in a worse state than it started.
- **A face with a round hole in it cannot take a pattern.** Holes have to be rectangles, so
  pattern a wall before merging a boss or a peg onto it.
- **The Front view lights the model from behind**, so a front elevation renders nearly black
  while the left and right ones are lit. Cosmetic, but the front elevation is the drawing anyone
  asks for first.
- **Selecting several objects through UI Automation stops working after an export** in a session.
  Ctrl+click in the object list is unaffected, and so is everything done with a mouse.
- **Undo keeps about a gigabyte of history and lets the oldest steps go beyond that.** A move or a
  colour remembers only numbers, so that is thousands of them; a mould of a scan is two parts of
  about twenty-six megabytes, so it is around twenty of those. Whatever was just done is always
  undoable however big it is. When older steps are let go, a notice offers to save a version -
  which is what still gets you back afterwards.
- **Lithophane** and **Voronoi** are marked beta: new, and still being proved out. Check what they
  give you before printing it.
- No printer integration.

## Layout

```
src/FastCraft3D/
  Geometry/   meshes, primitives, transforms, repair, plane split, gears and their frames,
              threads, lithophanes, Voronoi, hulls,
              Csg/ (booleans: Manifold where it loads, the BSP engine where it does not),
              Engraving/ (patterns, lettering, the surfaces they are laid on),
              Sketches/ (outlines and the solids made from them), Drawings/ (blueprints),
              Moulding/
  Text/       the one place that asks the operating system about fonts
  Model/      scene objects, scene, bed placement, Commands/ (undo-redo)
  Io/         STL, OBJ, 3MF, SVG outlines, pictures, .3dfc project files, export composition,
              crash recovery, settings remembered on the machine
  Render/     the only Direct3D-aware layer, plus the on-screen manipulators
  View/       dialogs, tool panels, the key table, the object list and its selection mirroring
  ViewModels/ commands and application state
tests/FastCraft3D.Tests/   geometry, CSG, IO, workflow and manipulator cover
tools/      verify-stl.py and fit-check.py - checking an export by measurement rather than by eye
tools/ui/   PowerShell for driving the running app, and the models built with it
```

## Licence

**BSD 3-Clause with the Commons Clause.** Use it, change it, pass it on — freely, for anything,
including inside a business. The one thing withheld is *selling the software itself*, or selling a
service whose value is the software; a commercial licence lifting that can be granted separately,
so ask.

Your models are your own either way. The licence covers the software, not what you make with it.

See [LICENSE](LICENSE) for the terms and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the
MIT-licensed components it is built on. **About** on the File tab shows the same, along with the
version and a Copy details button for bug reports.

## Contributing

Contributions are welcome — bug reports, models that break something, and patches alike.
[CONTRIBUTING.md](CONTRIBUTING.md) says what the codebase asks of a change, and what submitting
one grants.
