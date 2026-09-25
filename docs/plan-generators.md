# Plan - a library of generators

A large, growing library of parametric parts inside 3DFastCraft: boxes, trays, organisers, hinges,
clips, fasteners, enclosures and mechanisms. Each one is a set of numbers someone changes to fit
the thing in their hand, and a part that comes out right for those numbers.

This replaces the companion-app idea in `plan-motion-app.md` as the next large piece of work. The
mechanism engine from that plan is still here, as phase 7, but as a check that generators run, not
as a second application.

**What the estimates are.** As in `plan-3.3.md`: focused days with Claude Code doing the typing
and someone reviewing and trying it. The framework has one estimate per piece; each generator has
its own, because a box is an afternoon and a cam is most of a week.

---

## Why a framework first

A generator today costs far more in plumbing than in geometry. The gear is the extreme case:

| Piece | Where | Lines |
|---|---|---|
| The options and the builder | `Geometry/Gears.cs` | 1,783 |
| The panel | `View/GearDialog.xaml` and `.xaml.cs` | 559 |
| Preview, placement and insert | `ViewModels/MainViewModel.cs`, around line 6914 | about 150 |

Thread and Stair are the same shape at a smaller size. Every new generator built this way adds a
XAML panel and another stretch of `MainViewModel`, which is 9,739 lines already. Dozens of them
built that way is not a library, it is a maintenance problem.

The builders already have the right shape. `Gears.Build` takes a `GearOptions` record and returns
a `GearResult` of parts, notes and an optional refusal. Nothing about that is specific to gears.

Three items in `plan-3.3.md` are still open, and they are exactly what a generator needs: **2**, a
shared preview helper; **3**, objects that remember how they were made; and **4**, a printer
profile. Written once, inside the framework, every generator gets all three without being asked.
This plan takes those three over for generated parts.

---

## Decisions to make first

| Decision | Recommendation | Why |
|---|---|---|
| Where generators live | **Done:** `FastCraft3D.Generators`, a project of its own on `FastCraft3D.Geometry`, which was split out of the app for it. The app keeps a Library button and one partial file | The app is large already and must not grow with the library; with the geometry inside the app, the two would have had to reference each other |
| How parameters are described | Attributes on a settings **record class** | One file per generator. A record class, not a record struct: `new()` on a struct skips the defaults |
| Hand-written panels | Allowed, as an escape hatch | The gear's panel has readouts and linked fields that a generated panel may not match at first. A generator with its own panel still gets the recipe, the catalogue and the sweep |
| What the button is called | **Library**, on the Insert tab | To choose |
| New generators and the UI level | Marked beta, and listed only in **Extended** until someone has printed one | The same rule `UiLevel` already applies to ribbon buttons |
| Generators written by users | Not in this plan | C# plug-ins or OpenSCAD import can come once the built-in ones have settled the contract |

---

## What a generator is

```csharp
public sealed class LiddedBox : Generator<LiddedBox.Settings>
{
    public override string Id => "box.lidded";   // never changes once shipped
    public override int Version => 1;            // goes up when the same settings make a different part
    public override string Category => "Boxes";
    public override string Title => "Box with a lid";

    public sealed record Settings(
        [Length(10, 200), Group("Size")] float Width = 60,
        [Length(10, 200), Group("Size")] float Depth = 40,
        [Length(5, 150), Group("Size")] float Height = 30,
        [Wall] float Wall = Profile.Walls2,
        [Clearance] float Fit = Profile.XyClearance,
        [Choice] LidKind Lid = LidKind.Sliding,
        [Length(1, 10), ShowWhen(nameof(Lid), LidKind.Snap)] float SnapDepth = 1.5f);

    public override IEnumerable<string> Check(Settings s) { /* rules across fields */ }

    public override Generated Build(Settings s, Printer printer, CancellationToken ct) { /* ... */ }
}
```

### The rules
- **Build is pure.** Settings and printer in, parts out. No scene, no UI, no statics that change.
  The same settings always give the same mesh. It can run off the UI thread and can be cancelled.
- **Refuse, don't break.** A combination that cannot be made returns a refusal that says why, as
  `GearResult.Refusal` does now. Throwing is a bug, and so is a mesh that is not watertight.
- **Parts come out ready to print**: flat on the bed, the right way up. Where the parts go
  together differently from how they print, each part also carries its assembled placement, as a
  gear's `InMesh` does, and the preview shows them assembled.
- **Anchors are marked** wherever they will matter later: bores, shafts, mating faces, the axis of
  a hinge. They are what a re-edit places by, and what the motion check finds joints from.
- **Settings only grow.** A new field gets a default, so an old recipe still reads. `Id` never
  changes. `Version` goes up when the same settings would give a different part.

### Parameter kinds
| Kind | Shown as | Notes |
|---|---|---|
| `Length` | a number in the chosen unit | Through `MeasureUnit`, so inches and fractions work as they do in the transform boxes |
| `Angle`, `Count`, `Ratio` | a number | `Count` is whole |
| `Choice` | a list | An enum; each value can carry its own label and tooltip |
| `Toggle` | a tick box | |
| `Clearance`, `Wall` | a number with a "from profile" mark | Their defaults come from the printer profile (2.3) |
| `Size` | a list from a table | ISO thread sizes, battery sizes, board outlines. The table is data with its source noted |
| `Text` | a line of text | For labels on a lid or a drawer front |

---

## What makes a good generator

A generator earns its place when people change its numbers to fit something: the box sized to the
thing going into it, the hinge to the printer's clearance, the gear pair to a ratio. A part nobody
resizes is a model to download, not a generator, and it does not belong here. That rule is the
guard against scope: every organiser anyone has ever printed is otherwise a candidate.

---

## Phases

| Phase | Work | Estimate | Confidence |
|---|---|---|---|
| 1 | The framework, the generated panel and the sweep | 5 - 6.5 days | Medium-high |
| 2 | Recipes and the printer profile | 3.5 - 4 days | Medium |
| 3 | The catalogue | 2 - 3 days | High |
| 4 | Stair, Thread and Gear moved onto it | 3 - 4 days | Medium |
| 5 | Boxes and organisers | 11.5 - 16 days | High |
| 6 | Hinges, clips and fasteners | 7.5 - 10 days | Medium |
| 7 | The motion library | 5 - 7 days | Medium-high |
| 8 | Mechanisms | 13.5 - 19 days | Varies - see below |

**About 51 to 70 days in all**, but only phases 1 to 4 have to be done in one go. After those,
every generator is its own small piece of work that can ship alone. **A first release worth having
is phases 1 to 4 and the first three boxes: about 19 to 25 days.**

Plan 3.3's item 1, the core split with CI, is not needed first but is strongly advised. The sweep
in 1.4 will soon be the biggest load on the test suite, and it should be running on every push
rather than when someone remembers.

### Phase 1 - The framework, the generated panel and the sweep

#### 1.1 The contract
`Generator<TSettings>`, the parameter attributes, and `Generated`: a list of parts (name, mesh,
anchors, role, assembled placement), notes, and an optional refusal. It is `GearResult`
generalised. A registry finds every generator by reflection at start-up, keyed by `Id`.
**Estimate:** one and a half to two days, most of it the attribute reading and its tests.

#### 1.2 The preview runner
Plan 3.3's item 2, written here for the generated panel and open for the old panels to use later.
It waits about 150 ms after the last change, cancels a build that is still running, builds off the
UI thread, and guards against a slow old build overwriting a newer one. The panel shows a small
"working" mark while it runs.
**Estimate:** half a day, with tests of the ordering.

#### 1.3 The generated panel
A `ToolPanel` built from the settings. Fields are grouped under headings, each with its unit, its
range, a tooltip and a reset to the default, and `ShowWhen` hides fields that do not apply. Below
the fields go refusals and warnings from `Check`, and the size of the result, how many parts it
makes, and whether it fits the plate. **Insert** puts every part on the plate at once, with unique
names, laid out by `BedPlacement.Fit`, as one undo step. One method in `MainViewModel`,
`InsertGenerated`, replaces the per-tool insert code.
**Estimate:** two to two and a half days. The unknown is how much of the layout `ToolPanel`'s
existing panels settle by hand that a generated one has to settle by rule.

#### 1.4 The sweep
One test, run for every registered generator:
- **Quick, on every run:** every parameter at its minimum, default and maximum, one at a time.
- **Full, under a trait, before a release and in CI:** every pair of parameters at their ends
  together (pairwise), and a few hundred seeded random combinations on top.

Each build must either refuse with a reason, or produce parts that are:
- **watertight**, with positive volume;
- **inside the plate**, or saying they do not fit;
- **deterministic**: the same settings, built twice, give the same mesh;
- **clear of each other as assembled**, where the parts of a set are meant not to touch;
- **fast enough to preview**: at the defaults, built inside a second.

When the sweep finds a failure it prints the settings, so the failure can be turned into a test of
its own. A new generator gets all of this without writing a test. Its own tests are for what the
sweep cannot know, such as whether a lid actually fits its box.
**Estimate:** one to one and a half days.

**Done when** a trivial generator, a plain box, is one file, appears with a working panel and live
preview, inserts in one undo step, and is swept by the test suite without a line of test code.

### Phase 2 - Recipes and the printer profile

#### 2.1 The recipe
Plan 3.3's item 3, for generated parts. `SceneObject.Recipe` holds the generator's `Id`, its
`Version`, the settings as JSON, and for a part of a set, its role and a shared set id. It is saved
as one more optional field on `ObjectDto`, so old files read with no recipe and an old app ignores
it. Anything that changes the mesh drops the recipe, in the same places `IsPristine` is dropped
now. Undo puts it back.
**Estimate:** one and a half days, most of it the serialiser, and a test that walks every command
that changes a mesh and checks it drops the recipe.

#### 2.2 Re-editing
Double-click a generated part, or press **Edit** in the side panel, and its generator reopens
filled in. **Apply** replaces the part in place, keeping its name, colour, filament and turn. It is
placed by its anchor or pivot rather than its bounding box, since a box made 5 mm wider should grow
about where it stood. Editing one part of a set rebuilds the whole set. It is one undo step. A
recipe from an older `Version` reopens with a note that applying will rebuild it with the current
one.
**Estimate:** one to one and a half days.

#### 2.3 The printer profile
**Done, differently from first planned.** Nozzle, layer height and one XY clearance, kept by the
generator library in a file of its own (`printer.json` beside `settings.json`) rather than in
`LocalSettings`, and set in a folded **Printer** section at the foot of every generator's panel
rather than a panel on the View tab - the app does not grow, and the moment the printer matters
is when a fit is being chosen. `Clearance` parameters start at the printer's gap; `Wall`
parameters at whole nozzle widths no thinner than the generator asks. A setting still on the
printer's number says so and follows a change to the printer; one somebody typed is left alone.
A recipe stores the numbers actually used, so a re-edit never quietly changes a fit. The
calibration print (3.3's 4.3) stays in that plan.

**Done when** a box printed with its lid too tight can be opened, given 0.1 mm more clearance and
put back where it stood, in one step that Ctrl+Z undoes.

### Phase 3 - The catalogue

**Library** on the Insert tab opens a panel with categories down the side, tiles with a picture and
a name, and a search box. Favourites and recently used generators go at the top. Pictures are
rendered from the default settings with the renderer `Io/MeshThumbnail.cs` already has for 3MF, and
cached by `Id` and `Version`, so they are made once, not at every start. Presets are named
settings: some ship with the generator ("AA battery", "M3", "Gridfinity 2x1"), and the user can save
their own, kept in `LocalSettings`. Beta generators carry the same mark `ToolPanel.IsBeta` shows.
**Estimate:** two to three days.

**Done.** Pictures are kept in `%LocalAppData%DFastCraft\Library`, drawn off the UI thread the
first time a tile is shown. `MeshThumbnail` moved from the app into the generator library to
draw them, keeping its namespace, and the 3MF writer reaches it there. Favourites, recent use and
the person's own presets share one file, `library.json`, beside `printer.json`. Search matches
every word typed against the name, the category, the description and the preset names, and
Enter takes the first thing found.

### Phase 4 - Stair, Thread and Gear moved onto it

In order of difficulty, so the contract meets its hardest case last and is sound by then:
- **Stair**: a straight move. Its panel goes.
- **Thread**: a straight move, apart from the sizes table, which becomes a `Size` parameter.
- **Gear**: the proof. Pairs, racks, bevels, worms, ratchets and the reciprocating frame, each a
  set with roles and anchors, and a panel with readouts such as centre distance and ratio. If the
  generated panel can take readouts cleanly, it replaces `GearDialog`. If not, the gear keeps its
  own panel through the escape hatch, and still gains the recipe, the catalogue and the sweep.
  `GearDialogTests` are the guard either way.

**Estimate:** three to four days, two of them the gear.

**First draft done, not yet tried in the app.** All three are generators in the library behind
their old ribbon buttons, and `StairDialog`, `ThreadDialog`, `GearDialog` and about 500 lines of
`MainViewModel` are gone. The gear did not need the escape hatch: the framework gained what it
lacked instead - a generator's own rule for which rows show, settings that fill in others (a
metric size brings its pitch), readouts at the model's scale, a pivot per part, cutters that cut
into the selected part, and a single part's preview that can be moved while the numbers are set.
The sweep found real interference in bevel pairs of unequal counts and in worm drives, in the
tooth geometry itself; it is pinned as known in the sweep, with a test that fails once fixed. Gear
sizes are now held to what a printer could take: more than 600 mm across is refused.

**What could blow it up.** The gear's placement code in `MainViewModel` (`Place`, `OnItsAxis`)
depends on the gear. It has to become the general rule for placing any set by its anchors. If it
will not generalise, the gear keeps its own placement for now.

### Phase 5 - Boxes and organisers

Every generator here is a build from simple solids, with no motion to check. They are the fastest
way to make the library feel large.

| Generator | What it makes | Estimate | Confidence |
|---|---|---|---|
| **Box with a lid** | Lift-off, sliding, snap and screw-down lids; rounded corners; a label recess | 2 - 3 days | High |
| **Divided tray** | A grid of compartments, uneven if wanted, with scooped bottoms and a label slot | 1 - 1.5 days | High |
| **Gridfinity bins and baseplates** | Bins by grid units and height units, with dividers and a label lip; baseplates to fill a drawer | 2 - 3 days | Medium-high - an open standard, but the profile has to match its published dimensions to a tenth. Check the licence of the spec before shipping |
| **Screw-top jar** | Jar and lid on the existing thread code | 1 day | High |
| **Holders from a table** | Batteries (AA, AAA, 18650, coin cells), cards, bits, pens | 1 day | High |
| **Drawers and a cabinet** | A shell and drawers that slide in it, with the profile's clearance | 1.5 - 2 days | High |
| **Pegboard hooks and wall mounts** | Hooks, shelves and holders for 25.4 mm pegboard | 1 - 1.5 days | High |
| **Electronics enclosure** | A box with standoffs for common boards, screw bosses and cut-outs on chosen faces | 2 - 3 days | Medium - the board outlines and hole positions have to be collected and checked |

### Phase 6 - Hinges, clips and fasteners

| Generator | What it makes | Estimate | Confidence |
|---|---|---|---|
| **Print-in-place hinge** | Knuckles, pin and gaps from the profile's clearance | 2 - 3 days | Medium - depends on the profile being right. It gains a swing check in phase 7 |
| **Pin hinge and living hinge** | A hinge with a separate pin; a thin flexing web | 1 - 1.5 days | Medium - a living hinge depends on the material, and the panel says so |
| **Snap-fit** | A cantilever hook and its catch, sized from the strain the material takes | 2 days | Medium |
| **Bolts, nuts, washers and knobs** | ISO metric sizes from a table, on the existing threads | 1.5 - 2 days | High |
| **Clips and spacers** | Cable clips, spring clips, standoffs, spacers and bushings | 1 - 1.5 days | High |

### Phase 7 - The motion library

The engine from `plan-motion-app.md`, phase 1, as a library, with no application around it. It is
the frame test's drive simulator (`GearTests.cs`, around line 1003) generalised: bodies, revolute
and prismatic joints, couplings by gear ratio, rack or contact, and a drive stepped at a fixed
angle. It reports a jam, the least clearance, the play and the travel. It runs as one more check
in the sweep, for any generator whose parts declare joints. A generator whose output jams refuses,
as the booleans do.

The planar check does more than the motion plan gave it credit for. Any motion about one axis is
planar in sections taken across that axis, so a hinge's swing is checked the same way a gear pair
is. Only non-parallel axes, bevels and worms, need the solid check, and that waits until a
generator actually needs it.
**Estimate:** five to seven days. The risk, as in the motion plan, is contact that is really
rotational (a pawl, a Geneva wheel), where "move along the free direction" means turning. The same
search works on an angle, but has to be written for both.

### Phase 8 - Mechanisms

| Generator | What it makes | Estimate | Confidence |
|---|---|---|---|
| **Frame, checked by the engine** | The reciprocating frame's test moves into the engine, with a **turns either way** option | 1 - 2 days | High - the code exists |
| **Gear trains** | Compound trains to a target ratio by searching tooth counts; planetary sets with the assembly condition checked | 3 - 4 days | High - textbook |
| **Geneva drive** | 3 to 8 slots, from the standard formulas, with the pin's entry checked | 2 - 3 days | High |
| **Disc cam and follower** | Rise, dwell and return with cycloidal, harmonic or 3-4-5 polynomial laws; a roller or flat follower; pressure angle and undercut reported | 4 - 5 days | Medium |
| **Four-bar and crank-slider** | With the Grashof condition checked and the coupler curve drawn | 2 - 3 days | Medium-high - both have closed-form positions, so no general solver is needed |
| **Print-in-place bearing** | Races and rolling elements with the profile's clearance | 1.5 - 2 days | Medium |

### Phases 5 to 8 - first draft done, not yet tried in the app

Every generator in the tables above exists and passes the sweep: open box, box with a lid
(lift-off or sliding), divided tray, Gridfinity bin and baseplate, screw-top jar, electronics
enclosure, battery holder, drawers and cabinet, pegboard hook, knuckle hinge (print in place or
with a separate pin), living hinge, snap-fit, cable clip, washer, knob, spacer, gear train,
planetary set, Geneva drive, disc cam, four-bar, crank and slider, and a print-in-place bearing.

- **Shared solids** (`Shapes`): prisms, lofts, turned profiles, rods, and Manifold booleans that
  refuse rather than fall back to the BSP engine. A helper anywhere in a build can refuse with a
  reason by throwing `Refusal`.
- **The motion library** is `FastCraft3D.Geometry.Motion`: `PlanarMotion.Drive` turns a driver in
  steps and pushes revolute or prismatic bodies only as far as clears them, layer by layer, and
  `Sections.At` cuts a solid into the loops it works on. The Geneva drive and every reciprocating
  frame are turned a whole turn each way by it before they are handed back; a jam is a refusal.
  The solid check was not needed: every mechanism here moves in a plane.
- **Left for later:** Gridfinity's stacking lip and label lip, scooped tray bottoms, the
  enclosure's socket cut-outs, a guide for the cam's follower and the slider, pins with heads
  for the linkages, and a swing check for the hinge.
- **Sweep time.** The Geneva's motion check makes it the slowest to sweep, about half a minute.

---

## What could go wrong

- **A generated panel worse than a hand-made one.** Linked fields and readouts are where a panel
  built by rule falls short. The escape hatch means that never blocks a generator, but if most
  generators end up needing it, the framework has failed at its main job. That should be judged
  after the gear, in phase 4.
- **The sweep's running time.** Forty generators with hundreds of combinations each, some of them
  heavy on booleans, is a long run. That is why the quick sweep runs every time and the full one
  runs under a trait.
- **Booleans.** Fine detail on a curved surface tears the BSP boolean. Generators should build
  directly where the shape allows: extrusions, sweeps, and solids assembled from faces. The sweep
  will find where a boolean tears, and the answer is to build it another way, not to relax the
  watertight rule.
- **Standards.** Gridfinity, pegboard pitch, ISO sizes and board outlines are data someone else
  owns. Each table notes its source, and a generator that follows a standard has a test pinning
  its key dimensions.
- **Scope.** See "What makes a good generator". A generator nobody resizes should not be written.

---

## Relation to the other plans

- **`plan-3.3.md`.** Item 1, the core split, is still advised first. Items 2 and 3 are done here
  for generated parts. The tools that are not generators, such as Text, Sketch, Lithophane and the
  hole cutter, can join the same recipe format later. Item 4 is done here as far as generators need
  it, and its calibration print stays there. Item 5 is done.
- **`plan-motion-app.md`.** Its engine is phase 7 here and its generators are phase 8. The separate
  application waits until someone needs to check an assembly of arbitrary parts rather than
  generated ones. There is no sign of that need yet.
