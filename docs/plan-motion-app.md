# Plan - a companion app for mechanisms: generators and a motion check

A second application beside 3DFastCraft, for the one question the modelling app cannot answer:
**does this thing move?** Working name **3DFastCraft Motion**, to be replaced.

It takes parts - made here, in 3DFastCraft, or anywhere else - joins them with hinges, slides and
gear couplings, turns the input, and reports the first place anything collides, how much play there
is, and where. Around that it has the generators whose correctness is a matter of motion rather than
of shape: cams, Geneva drives, print-in-place hinges, snap-fits, gear trains, linkages.

The reciprocating frame is the proof that this is worth a separate app. Its old version passed every
static test and jammed both ways round the moment it was turned; what caught it, and what made the
new one right, was a two-dimensional drive simulator - turn the gear a little, let the frame move
only when pushed, report a jam. That simulator lives in a test file today. Generalised, it is this
app's engine.

**What the estimates are.** As in `plan-3.3.md`: focused days with Claude Code doing the typing and
someone reviewing and trying it. Each phase and each generator has its own, because they are not the
same kind of work - a generator with a textbook formula behind it is days, a solver for closed loops
is research.

---

## Why a separate app, and not more tabs

- **A different job.** 3DFastCraft makes a part and gets it onto a plate. This one assembles parts
  that already exist and asks how they behave. Its screen is a timeline and a report, not a ribbon.
- **A different pace.** A motion check sweeps a whole cycle, thousands of poses; it wants a progress
  bar and a result page, not a preview that has to keep up with typing.
- **Scope.** The main app's `MainViewModel` is already 9,000 lines. A mechanism engine inside it
  would make that worse.
- **Shared where it counts.** Once `FastCraft3D.Core` exists (plan 3.3, item 1), both apps build on
  the same meshes, booleans, gears, threads, file formats and tests. Nothing is written twice.

What stays in 3DFastCraft: every generator whose output is simply a printable part - gears, racks,
threads, holes, the frame as it is. What goes here: whatever needs its motion checked to be trusted,
and the check itself. A gear pair made in 3DFastCraft opens here to be proved.

---

## Decisions to make first

| Decision | Recommendation | Why |
|---|---|---|
| Same repository, or a new one | **Same repository**, a second app project in `3DFastCraft.sln` | One core, one CI, one set of tests; a change to the gear code is checked against both apps at once |
| User interface | **WPF**, reusing `Render/` | The renderer, the manipulators and the look already exist; Avalonia would be cross-platform but means a new renderer |
| Exchange with 3DFastCraft | **`.3dfc` both ways**, 3MF and STL in | A `.3dfc` keeps names, colours, pivots and anchors; the anchors are what let joints be found automatically |
| Name | to choose | Working name 3DFastCraft Motion |

---

## Architecture

```
FastCraft3D.Core     (net8.0)          meshes, booleans, gears, threads, file formats - plan 3.3, item 1
FastCraft3D.Render   (net8.0-windows)  the Direct3D layer and manipulators, moved out of the app
FastCraft3D.Motion   (net8.0)          the mechanism engine: bodies, joints, drive, collision, report
3DFastCraft          (WPF app)         unchanged, apart from its references
3DFastCraft.Motion   (WPF app)         the companion app
FastCraft3D.Motion.Tests (net8.0)      the engine's tests, run on Linux in CI
```

The engine has no user interface in it, so it is tested on Linux like the core, and a mechanism can
be checked from a test the same way the frame is today.

### The model

| Piece | What it is |
|---|---|
| **Body** | A part: its mesh, where it is, and what it is fixed to |
| **Joint** | How a body may move: **revolute** about an axis, **prismatic** along one, or **fixed** |
| **Coupling** | How one joint drives another: a **gear ratio**, a **rack**, or **contact** - moved only when pushed, as the frame is |
| **Drive** | What turns: a joint and a motion over time, one turn at constant speed by default |
| **Run** | Stepping through the drive and recording every body's pose, every contact, every jam |

Joints are found from the parts where they can be: a gear's **Bore** anchor is a revolute joint to
the frame of the machine, and a **Shaft** anchor meets a bore. Anything else is picked in the view,
the way a pivot is set in 3DFastCraft.

### The two checks

- **Planar, fast.** Most printed mechanisms move in a plane - gears, racks, frames, cams, Genevas,
  linkages. Cut every part at a few heights, and do the check on the outlines, as the frame's test
  does: segments in a grid, a crossing is a collision. Thousands of poses in seconds.
- **Solid, general.** For anything that moves out of the plane - a hinge, a bevel pair, a worm - a
  bounding-volume tree over each mesh, triangle against triangle, with a nearest-distance query for
  clearance. Slower, and used only where the planar check does not apply.

### What a run reports

- **Jam** - the first pose at which nothing can move without two parts passing through each other,
  with the pair, where, and how deep.
- **Clearance** - the smallest gap over the whole cycle, per pair, drawn as a graph against the
  input's angle.
- **Play** - how far a driven part can be pushed by hand at each pose; for a parked frame, whether
  it is held.
- **Travel** - how far each driven part goes, and what share of the cycle it moves for.

---

## Phases

| Phase | Work | Estimate | Confidence |
|---|---|---|---|
| 0 | Foundations | 2 - 3 days | High |
| 1 | The engine and the planar check | 5 - 7 days | Medium-high |
| 2 | The solid check and the app itself | 6 - 9 days | Medium |
| 3 | Generators, one at a time | 14 - 19 days in all | Varies - see below |
| 4 | Linkages, with a constraint solver | 6 - 10 days | Low |
| 5 | Exchange and releasing it | 2 - 3 days | High |

**About 35 to 51 days in all.** A first release worth having comes after phases 0 to 2, 5 and two
generators - the gear train and the frame - which is **about 19 to 28 days**.

### Phase 0 - Foundations
Needs plan 3.3's item 1, the core library, done first. Then `Render/` moves into
`FastCraft3D.Render` - it already references nothing in `View/` or `ViewModels/`, so it moves
cleanly - and an empty app project opens a `.3dfc` and shows it.
**Estimate:** 2 - 3 days. The render split is the only real work; the rest is project files.

### Phase 1 - The engine and the planar check
Bodies, joints, couplings and a drive, stepped at a fixed angle. The contact coupling is the frame
test's simulator generalised: after each step, a body that would collide is moved along its free
direction as little as clears it, and a step that nothing clears is a jam. Sections at several
heights for the planar check. The frame's tests move over as its first tests, and a gear pair, a
rack and a ratchet join them.
**Estimate:** 5 - 7 days. The risk is contact that is really rotational - a pawl on its pivot, a
Geneva wheel - where "move along the free direction" means turning, not sliding. The same search
works on an angle, but it has to be written for both.

### Phase 2 - The solid check and the app itself
The bounding-volume tree and the triangle tests; nearest distance for clearance. The app: open parts,
joints found from anchors or picked, a play button and a timeline to scrub, the colliding pair shown
red at the jam, the clearance graph, and a report that can be saved. Record-style pictures of the
worst poses.
**Estimate:** 6 - 9 days. The risk is speed on a dense mesh; the fallback is to check the solid
only near the poses the planar check calls close.

### Phase 3 - Generators
Each one makes its parts, the joints and couplings between them, and runs the check before handing
anything back - a generator whose output jams refuses, as the booleans do.

| Generator | What it makes | Estimate | Confidence |
|---|---|---|---|
| **Gear trains** | Compound trains to a target ratio, searching tooth counts; planetary sets with the assembly condition checked (sun and ring teeth summed, divided by the planets, a whole number) | 3 - 4 days | High - textbook |
| **Reciprocating frame** | The one from 3DFastCraft, with a **turns either way** option that sweeps both directions, now checked by the engine rather than by a test | 1 - 2 days | High - the code exists |
| **Cams** | A disc cam from a motion drawn as rise, dwell and return, with the standard laws (cycloidal, harmonic, 3-4-5 polynomial) and a roller or flat follower; generated by the frame's own sweep; pressure angle and undercut reported | 4 - 5 days | Medium - the envelope is proven, the follower variants are work |
| **Geneva drive** | An external Geneva of 3 to 8 slots: driver pin and locking disc, wheel with slots and locking arcs, from the standard formulas; the entry checked for the pin going in square | 2 - 3 days | High |
| **Print-in-place hinge** | Knuckles, pin and gaps from the printer profile's clearance, with a check that it swings its full angle | 2 - 3 days | Medium - depends on the profile |
| **Snap-fit** | A cantilever hook and its catch, sized from the strain the material takes, with a check that it passes the catch and holds | 2 days | Medium |

### Phase 4 - Linkages
Four-bar and crank-slider linkages, with the Grashof condition checked and the coupler curve drawn,
need what nothing before them does: a loop that closes, so each pose is solved rather than stepped.
A small Newton solver on the loop equations, with a clear report when a pose has no solution - which
is a linkage that locks.
**Estimate:** 6 - 10 days, low confidence. This is the one piece that is research rather than
engineering, and the phase to cut if time is short: everything before it works without it.

### Phase 5 - Exchange and releasing it
**Open in 3DFastCraft** and **Open in Motion**, each passing a `.3dfc`; a release artefact of its own
beside the main one, built by the same CI; a README section; the `ship` skill taught to publish both.
**Estimate:** 2 - 3 days.

---

## What could go wrong

- **Contact is not dynamics.** "Moved only when pushed" is friction without inertia. That is right
  for a printed toy turned slowly and wrong for anything fast, where a part coasts. The report
  should say so, and the lock tests are the guard: a mechanism that only works because nothing
  coasts is one the lock should be holding.
- **Scope.** Every mechanism anyone has ever drawn is a candidate generator. The ones above were
  chosen because each is either common in printing or already half built here; anything else waits
  until the engine has proved itself on them.
- **Two apps to keep in step.** The core and the shared CI are what stop them drifting. Without plan
  3.3's item 1 this plan should not start.
