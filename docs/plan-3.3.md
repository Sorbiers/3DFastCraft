# Plan - the next five things, after 3.2

Written after the reciprocating frame and the README pass for 3.2.0, from a review of the whole
codebase. Five pieces of work, in the order I would do them, each with its own estimate - they are
different kinds of work and deserve different kinds of number.

**What the estimates are.** Focused days of work with Claude Code doing the typing and someone
reviewing, building on Windows and trying it in the app - not calendar days. Each says what it
rests on and what would blow it up. Confidence is how sure I am of the range, not of the result.

| # | Work | Estimate | Confidence | Depends on |
|---|---|---|---|---|
| 1 | Split out the core library and add CI | 2 - 3 days | High | - |
| 2 | A shared preview helper | 2 - 3 days | Medium | - |
| 3 | Objects that remember how they were made | 4 - 6 days | Medium | 2 helps |
| 4 | A printer profile | 3 - 4 days | Medium-high | 3 helps |
| 5 | Best face down | 2 - 3 days | High (algorithm), medium (scoring) | - |

Thirteen to nineteen days in all. 5 stands on its own and is the quickest visible win, so it can go
first if something is wanted to show; 1 should go before anything large, because everything after
it is safer with CI behind it.

---

## 1 - Split out the core library and add CI

**Why.** Nothing checks a change but someone running the tests by hand on Windows, and the
dialog written for the frame reached `main` without ever being compiled. The rule in CLAUDE.md -
`Geometry/`, `Model/` and `Io/` never reference renderer types - is kept by habit rather than by the
compiler.

**What was found.** `Geometry/`, `Model/` and `Io/` already build as a plain `net8.0` library on
Linux, with ManifoldRust's native library loading there too, except for three files that use WPF's
imaging and fonts:

| File | Uses | Called from |
|---|---|---|
| `Io/MeshThumbnail.cs` | `System.Windows.Media` to render the 3MF's picture | `Io/ThreeMfIo.cs` |
| `Io/PictureReader.cs` | WPF imaging to decode PNG, JPEG and the rest | `View/LithophaneDialog.xaml.cs`, `ViewModels/MainViewModel.cs` |
| `Text/GlyphOutlines.cs` | `FormattedText` and `Typeface` for letter outlines | `Text/TextObject.cs`, `Render/BuildPlateVisual.cs`, `ViewModels/MainViewModel.cs` |

Of the 1,209 tests, 465 are in the 47 test files that touch `View/`, `ViewModels/`, `Render/` or WPF;
the other 744 or so are pure core.

### 1.1 Put the three behind interfaces
`IThumbnailRenderer`, `IPictureDecoder` and `IGlyphSource` in the core, with the WPF versions in the
app, handed in at start-up. The core keeps working without them where it can: a 3MF without a
thumbnail is still a 3MF.
**Estimate:** half a day.

### 1.2 `FastCraft3D.Core`
A `net8.0` class library holding `Geometry/`, `Model/`, `Io/` and the parts of `Text/` that are not
WPF. Namespaces stay as they are, so no file outside the csproj changes for the move. The app
references it; `3DFastCraft.sln` gets the new project.
**Estimate:** half a day. The risk is a stray WPF type in `Model/` the Linux build did not reach -
it did compile, so small.

### 1.3 Split the tests
`FastCraft3D.Core.Tests` (`net8.0`) takes the core-only tests; the rest stay in the existing
Windows project. Shared helpers (`Closed`, `Overlap`, the frame's drive simulator) move to a small
test-support file both can include.
**Estimate:** half a day.

### 1.4 GitHub Actions
- **Linux, every push:** build the core, run its tests. A few minutes.
- **Windows, every push:** build the whole solution, run every test.
- **Windows, on a tag:** the self-contained publish `build.bat` does, uploaded as the release asset
  - what the `ship` skill does by hand today.

**Estimate:** half to one day. The unknown is the WPF tests on a hosted Windows runner: the ones
that open a `ToolPanel` on an STA thread need a desktop session, which hosted runners usually give
but not always. If they will not run, mark them and run them locally, as now.

### 1.5 Paperwork
`build.bat`, the `ship` and `drive-app` skills, the layout in CLAUDE.md and the README.
**Estimate:** two hours.

**Done when** a push to any branch shows the core tests passing on Linux and the full build on
Windows, and a WPF-only mistake like the frame dialog would have been red.

---

## 2 - A shared preview helper

**Why.** Eleven panels show their result on the plate as the numbers change - Stair, Lithophane,
Thread, Custom, Hole, Text, Gear, Round, Voronoi, Deform and Smooth (`MainViewModel.cs` around
lines 5166 to 7529). Two of them, Deform and Lithophane, wait for the typing to settle; the rest
rebuild on the UI thread at every keystroke. The gear panel runs `Gears.Build` synchronously, and a
60-tooth frame is now about 240 ms of a frozen window per key.

### 2.1 `PreviewRunner`
One class, used the same way by every panel:
- **Settle** - wait about 150 ms after the last change before starting.
- **Cancel** - a new change cancels the build in flight through its `CancellationToken`.
- **Off the UI thread** - build in `Task.Run`, hand the mesh back to the dispatcher.
- **Stale guard** - a sequence number, so a slow old build can never overwrite a newer one.
- **Busy** - a small "working" mark in the panel while it runs, rather than nothing.

**Estimate:** half a day, with tests of the ordering (a slow first build and a fast second one
must end on the second).

### 2.2 Move the panels onto it
One at a time, Gear first since it is the slowest and the best tested. Most builders already take a
token; the ones that do not get one. Lithophane and Deform lose their own timers.
**Estimate:** a day and a half for all eleven.

**What could blow it up.** A builder that is not safe off the UI thread. The likely one is lettering:
`GlyphOutlines` builds `FormattedText`, and WPF text objects may want the thread that made the font.
If so, fetch the outlines on the UI thread and build the solid off it - the outlines are cheap.

**Done when** holding a key down in the gear panel's Teeth box leaves the window responsive and the
preview catching up behind it.

---

## 3 - Objects that remember how they were made

**Why.** This is the biggest gap for a printing tool. A gear, a thread, a hole cutter, a text, a
stair or a sketch becomes a plain mesh the moment it is added, so when a test print shows the
clearance is a tenth too tight, the part is made again from nothing - and placed again, and
coloured again. A `SceneObject` keeps only `Origin` (its primitive kind) and `IsPristine`.

**The shape of it.** The options records already exist - `GearOptions`, `ThreadOptions`,
`HoleOptions`, `TextOptions`, `StairSettings`, `CustomShape`, `LithophaneOptions` - and each tool
already keeps a `last...` copy of its own in `MainViewModel`. What is missing is somewhere on the object to keep it.

### 3.1 The recipe
`SceneObject.Recipe`: the tool's name, a version, the options as JSON, and for a part of a set (a
gear and its partner, a gear and its frame) the part's role and a shared set id. Saved as one more
optional field on the project file's `ObjectDto`, which already takes additive fields - so an old
file reads with no recipe, and an old app reads a new file and ignores it.
**Estimate:** a day, most of it the serialiser and its tests.

### 3.2 Losing it honestly
Anything that changes the mesh itself - a boolean, smooth, split, engrave, emboss, hollow, rebuild -
drops the recipe, exactly as `IsPristine` is dropped today and in the same places (25 of them). A
move, a turn, a colour or a filament keeps it. Undo puts it back with the mesh.
**Estimate:** half a day. The risk is a mesh-changing path that forgets to clear it; a test that
walks every command and checks is the guard.

### 3.3 Re-editing
Double-click the object, or an **Edit** button in the side panel, and its tool reopens filled in.
**Apply** replaces the mesh in place: same name, colour, filament, position and turn, measured
from the pivot rather than the box, since a regenerated gear with one more tooth has a different
box but the same shaft. One undo step. For a set, editing one part rebuilds the set, since a
partner's placement depends on its mate.
**Estimate:** two days - Gear, Thread, Hole cutter, Text, Stair and Custom.

### 3.4 Sketch and Lithophane
Sketch needs its outlines kept, not just its options; Lithophane needs its picture, which should be
embedded in the project rather than referred to by path, or a moved file breaks the recipe.
**Estimate:** one to one and a half days. Can be left for later without spoiling the rest.

**What could blow it up.** Placement. If the regenerated part does not land where the old one stood,
the feature is worse than useless. The pivot and the bore anchor the gear already carries are the
reference to hold.

**Done when** a gear and frame printed too tight can be opened, given 0.1 more clearance and put
back where they were, in one step that Ctrl+Z undoes.

---

## 4 - A printer profile

**Why.** The printer is assumed in a dozen places, each with its own number and its own idea of
what a clearance is:

| Where | Number | Means |
|---|---|---|
| `GearOptions.Backlash` | 0.15 | along the pitch circle, split between two gears |
| `GearOptions.FrameClearance` | 0.25 | normal gap all round |
| `HoleCutter.ExtraClearance` | 0.2 | on the diameter |
| Thread clearance | 0.2 | on the diameter, half each side |
| Connector clearance | 0.2 | per side |
| Subtract tolerance | typed | per side |
| `BrickStuds.FdmFit` | -0.1 | the stud's own fit |
| Two nozzle widths | 0.8 | Engrave's line warning, Voronoi's strut floor, a nut's wall, Hollow's wall |
| Two layers | 0.4 | Engrave and Emboss depth warnings |
| `LithophaneOptions.LayerHeight` | 0.1 | layer height |

Every one of those was right for someone's printer.

### 4.1 The profile
Nozzle, layer height and one **XY clearance** - the normal gap two printed surfaces need to slide -
kept on the machine in `LocalSettings`, since the printer belongs to the desk, not to the model. A
**Printer** panel, beside Grid on the View tab. Several named profiles only if someone asks.
**Estimate:** half a day.

### 4.2 One mapping, in one place
Each tool's default comes from the profile through one table that converts the XY clearance into
that tool's own meaning - on a diameter, per side, split between two parts, along a pitch circle.
The panels still let the number be changed for this part, and say when it came from the profile.
The warnings read the nozzle and the layer height instead of 0.4 and 0.2. A recipe (3) records the
numbers it was made with, so re-editing a part does not quietly change them.
**Estimate:** a day and a half - eight tools and five warnings, each small.

### 4.3 A calibration print
**Fit test** on the Insert tab grows a second coupon: pins in holes at clearances from 0.05 to 0.4,
a slot, and a wall one and two nozzles thick. Print it, find the loosest pin that still turns, type
its number into the profile. The brick coupon stays as it is.
**Estimate:** a day.

**What could blow it up.** Changing defaults under people's feet. A user who has typed 0.3 into the
thread panel should keep it; the profile sets where a tool starts, not what it was last told.

**Done when** setting the XY clearance to 0.3 once makes a new gear frame, a thread, a hole and a
split's pins all come out 0.3-right without touching their panels.

---

## 5 - Best face down

**Why.** Lay on face answers "put it on *this* face". The question before that - which face - is
answered by eye. The pieces for answering it are already here: `RestingFaces.Find` gives every face
the part can stand on (from the convex hull, with the centre of mass over it), `Overhangs.Area`
measures what would need support, and `LayOnFace` turns the part onto a face.

### 5.1 Scoring
For each resting face: turn the part onto it, then measure
- **support** - `Overhangs.Area` at the Overhangs panel's angle, the bed's own faces left out;
- **height** - which is most of the print time;
- **footprint** - the face's own area, for adhesion;
- **stability** - how far the centre of mass sits inside the footprint, against its height.

Rank by support first, and height and footprint after, with a choice of what to favour: **less
support**, **shorter print** or **firmer footing**. Faces that come out the same by symmetry are
shown once. Off the UI thread, and capped at the largest faces for a scan with thousands.
**Estimate:** one day with tests - a block with a peg goes on its base; an L-bracket on its long
leg; a cup upright, not on its rim.

### 5.2 The panel
**Best face down** on the Align tab, beside Lay on face: the best three to five with their numbers
(support in cm2, height, footprint), each tipping the part over on the plate when hovered and
putting it down on click - one undo step, through `LayOnFace`.
**Estimate:** one day.

### 5.3 Say it in the Overhangs panel too
With Overhangs on, a line saying how much a better face would save.
**Estimate:** two hours.

**What could blow it up.** Nothing structural. The scoring is a matter of taste, which is why the
choice of what to favour is in the panel rather than baked into the weights.

**Done when** the test shapes land where a person would put them, and on any part the face it
picks never needs more support than the one the part is already standing on.
