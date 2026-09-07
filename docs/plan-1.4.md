# Plan — fixing what the house build found

Written after modelling a 1:87 house end to end inside the application. The build is in
`build/house/`; the findings are in `build/house/REPORT.md`. This is the work that follows from
them, in the order I would do it.

Three fixes are already in the working tree and uncommitted: the CSG plane epsilon, the Group
warning, and 22 `AutomationId`s. Everything below is still to do.

---

## Phase 0 — Fix the model that was shipped

Not app work, but it comes first because the model is the deliverable and it is wrong.

| # | What | Why |
|---|---|---|
| 0.1 | Rebuild the living floor | Five of ten openings never got cut — the whole front and west elevations are blank, including the front door |
| 0.2 | Rebuild the dowels | The four Ø3 sockets bore straight through the walls, bottom to top; two Ø2.6 pins stand inside the north windows |
| 0.3 | Add the basement dowels | The basement was exported without them, so the floor's sockets have nothing to sit on |
| 0.4 | External entrance steps | The front door is 2.1 m above grade with no way to reach it |
| 0.5 | Basement windows | Two metres of blank concrete reads as a plinth, not a storey |
| 0.6 | Verify numerically, not visually | Ray-test every opening and every dowel before exporting. Renders hid all of the above |

0.6 is the one that matters beyond this model. Add a small STL-inspection script beside
`tools/ui/` that takes a list of expected voids and reports any that are solid. Every part gets
checked before it is called done.

**Estimate:** half a day, mostly re-running the driver script with the named-field helper.

---

## Phase 1 — Silent failures

These cost the most during the build, because each one hid a problem rather than reporting it.

### 1.1 Repair must say when it declines
Handed 5 open and 6 non-manifold edges it ran, changed nothing, changed no triangle count, and
said nothing. A user reasonably concludes it worked.

*Make `MeshHealer` return why it declined and put that in the status bar: "Repair could not mend
this — 6 non-manifold edges remain."*
**Estimate:** 1 hour.

### 1.2 `Zoom to fit` blanks the viewport in Top view
Switch to Top, then fit, and the viewport goes empty — the build plate disappears too. Fit first
and *then* switch to Top and it is fine. Both floor plans in the report had to be captured the
second way.

*Almost certainly the camera's near/far planes or its up vector degenerating when the view
direction is straight down. Reproduce with one object and a Top+fit.*
**Estimate:** 2 hours.

### 1.3 A modal left open disables the ribbon with no visible cause
After an export the dialog stayed open; every following operation failed as "not enabled" with
nothing on screen to say why unless you look at the right window.

*Two parts: make sure the export dialog closes on every path, and give the main window a
disabled-state hint so an operation refused because a dialog is open says so.*
**Estimate:** 2 hours.

### 1.4 The health check believes unwelded coincident faces
`Mesh.Combine` does not weld, so two shells meeting on a face have every edge counted exactly
twice and the group reports **watertight — ready to print** when it is not a solid at all.
Group now warns (already fixed), but the status bar still lies for any such mesh.

*Weld on a copy inside `CheckHealth`, or cache a welded health per mesh version. Cost matters:
this runs on every selection change and meshes reach 300k triangles.*
**Estimate:** half a day, mostly on making it cheap enough.

---

## Phase 2 — Reachability

Accessibility and scriptability are the same problem here, and the house build could not have
been driven without working round all three.

### 2.1 The Selection menu is not reachable
Group, Ungroup, Select all, Deselect all, Invert selection, Sticky selection come through as bare
text with no Invoke. No automation, no keyboard, no screen reader. They had to be clicked by
pixel position.

*Make them real `Button`s (or `ToggleButton` for Sticky) with the `PanelButton` style and
`AutomationProperties.Name`.*
**Estimate:** 1 hour.

### 2.2 Two checkboxes share one name
"Raised instead of cut" appears in both the Engrave and Emboss panels, so finding it by name is
ambiguous even when only one is on screen.

*Rename the engrave one to "Raised pattern" or give both distinct `AutomationId`s.*
**Estimate:** 15 minutes.

### 2.3 The Name field commits on focus loss only
Set it last and it never commits.

*`UpdateSourceTrigger=PropertyChanged`, or commit on Enter.*
**Estimate:** 15 minutes.

### 2.4 Engrave forgets "Raised"
It does not survive from one use of the tool to the next, so a four-walled building means ticking
it four times — and forgetting once gives one cut wall among three raised.

*Keep `EngraveOptions` on the view model across `BeginEngrave`, or persist it with the app
settings.*
**Estimate:** 1 hour.

### 2.5 No Back, Left or Bottom view
With only Front, Right and Top, two of a building's four walls cannot be brought to face the
camera. Cladding the north and west walls meant rotating the *object* 180°, engraving, and
rotating it back — which bakes the rotation into the mesh and shifts the object's centre by a
few tenths of a millimetre each time. That drift is what misplaced the openings.

*Add Back, Left and Bottom. Trivial next to what their absence costs.*
**Estimate:** 1 hour.

---

## Phase 3 — The tools that were missing

Ordered by what they would have saved.

### 3.1 Repeat / array — **the biggest single win**
Twelve stair steps were twelve boxes, each with a name, three sizes and three positions typed in:
about a hundred field entries for one staircase. The same again for four dowels, four sockets and
seventeen opening cutters.

*A **Repeat** tool: take the selection, repeat it *n* times with a step in X/Y/Z, and optionally a
size increment per copy. A staircase becomes one box and one dialog. Undo as a single
`AddObjectsCommand`.*

**Estimate:** 1 day. No geometry risk — it is transforms and copies.

### 3.2 Scene scale in real units
Every dimension in the report was divided by 87 by hand. For architectural work that is the job.

*A scene-level scale (1:87, 1:100, 1:50, 1:1) and a toggle so position and size read in real
units. The geometry never changes — only the labels and the parsing. Store it in `.3dfc`.*

**Estimate:** 1 day. Touches every numeric field, so it wants care rather than cleverness.

### 3.3 Raised patterns on a face with holes
`FaceRelief` needs a face that fills its own bounding rectangle, and a wall with windows does not.
So the brick had to go on **before** the openings were cut, and the openings then cut through the
relief. It works, but it forces an order nobody would guess and you cannot re-clad a wall that
already has windows.

*`TryOutline` walks the face's boundary as a single loop. Accept several: the outer one bounds the
area, the inner ones are holes the tiling must respect. `FaceTiling` already handles inner
boundaries — it is the rectangle path and the outline walk that assume one loop.*

**Estimate:** 1–2 days. Real geometry work, but well-bounded and testable.

### 3.4 Split on a picked face
Split cuts only on X, Y or Z. Any sloping cut — a roof, a chamfer, a hip — means rotating a cutter
box and working out where its face lands. I built the roof from two Wedges instead, which only
works because Wedge happens to be a right triangular prism.

*Let Split take the face you click as its plane, the way Lay on Face already takes a face. The CSG
engine cuts against an arbitrary half-space already; it is the UI that is axis-bound.*

**Estimate:** half a day.

### 3.5 Hollow with an open face
Hollow closes everything. A roof needs a shell open underneath, so it was built as one prism minus
a second, lower one — and the arithmetic (the inner prism drops by *skin ÷ cos pitch*) is not
something a user should be doing.

This also cost the roof its dowels: with a 2.5 mm skin there is nowhere at the eaves to sink a
3.4 mm socket, so the roof locates on a nesting rim instead. A good join, but forced.

*Let Hollow take the face you click and leave it open.*

**Estimate:** half a day.

### 3.6 Pattern proportions
Brick courses are fixed at three times as long as they are tall. Real brick is about 3.3:1, so
that is close — but a roof tile is nearer 1.5:1 and there is no way to say so. The tiles on this
roof are 39 × 13 cm where a pantile should be about 30 × 20.

*An aspect field beside Size, or a **Tiles** pattern kind with its own proportions and an overlap.*

**Estimate:** 2 hours for the aspect field; a day for a proper Tiles pattern with courses that
overlap the way real ones do.

### 3.7 A stair tool
Getting a stair right is arithmetic, not modelling: total rise, riser count, going, headroom. All
of it was done by hand and checked against building-regulation ranges by hand.

*A Stair primitive taking rise, run, width and tread count, reporting the riser and going in real
units at the current scale, and refusing a flight nobody could climb. Depends on 3.2 for the
units and is largely 3.1 underneath.*

**Estimate:** half a day once Repeat and scale exist.

### 3.8 Align to selection, and a fit check
The dowels and sockets line up because I computed both from the same four coordinates. Nothing in
the app checks that a pin has a hole above it, or that a frame fits its opening — and when the
dowels came out wrong, nothing said so.

*Start with **Align to selection**: move A so its bounding box matches B's on chosen axes. Then a
**Fit check** between two selected objects: do they intersect, and if so by how much. That would
have caught the bored walls immediately.*

**Estimate:** half a day for align; a day for the fit check.

---

## Phase 4 — Smaller things

- **Duplicate with a typed offset** rather than placing beside and dragging.
- **Per-face colour**, so one object can show brick and stone.
- **`Drop to plate` on a multi-selection drops each object separately**, pulling a stacked
  assembly apart. Offer "drop as a group".
- **Persistent dimension annotations** — Measure is transient.
- **`File ▸ New`'s unsaved-changes prompt** is a separate window; give it a default button and a
  keyboard path.

---

## Suggested order

**Sprint 1 — trust.** 0.1–0.6, then 1.1, 1.2, 2.1, 2.2, 2.3, 2.4, 2.5.
Everything that hides a failure or cannot be reached. About three days, no geometry risk, and the
model comes out correct at the end of it.

**Sprint 2 — the two big tools.** 3.1 Repeat, then 3.2 Scene scale.
Two days that change what the application is for. Repeat first: it is self-contained and every
later feature leans on it.

**Sprint 3 — geometry.** 3.3 patterns on holed faces, 3.4 Split on a face, 3.5 Hollow open.
Three to four days. These are the ones with real failure modes, so they go after the trust work,
when a bad result will announce itself.

**Sprint 4 — the rest.** 3.6, 3.7, 3.8, then Phase 4.

## The thread running through all of it

The house was verified by looking at pictures, and the pictures hid five missing openings and four
walls bored end to end. Numeric verification found every one of them in four minutes.

The application has exactly one honest verifier — the watertight check — and it is the most
useful thing in it. Every item in Phase 1 is a case of the app knowing something was wrong and not
saying so; every item in 3.8 is a case of it not knowing. That is the direction worth pushing in:
**a modelling tool for printable parts should be hard to leave broken quietly.**
