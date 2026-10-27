# Project mode

A `.3mf` is what slicers read, and a slicer that saves over one drops everything only this app
reads - the editable objects, their recipes, the kept versions. Project mode keeps a second file
beside it that no slicer touches, and grows into the place the history lives.

## Simple project mode (branch `project_mode_simple`)

The user works with `house.3mf` as now. With project mode on, `house.3dfp` is kept beside it:
same name, same folder.

### Rules

- **Asked at New**: "Keep a project file?" with Remember my choice. Turned on or off later in
  File > Project.
- **Save writes the `.3mf`**, as now, and brings the `.3dfp` up to date beside it. There is no
  way to save a `.3dfp` on its own.
- **The `.3mf` stays whole without it.** It keeps the current editable project and the kept
  versions, as now. Deleting the `.3dfp` loses only what is in it alone; it can be made again.
- **An id ties the two**: a new one for each project, written into both. Save As starts a new
  `.3dfp` with a new id, so two copies never share a history.
- **Opening a `.3dfp`** opens the `.3mf` it belongs to. If that is gone, the project is rebuilt
  from the `.3dfp` and Save writes the `.3mf` again.

### What opening a `.3mf` checks

| The `.3mf` | The `.3dfp` | What happens |
|---|---|---|
| a project, linked | found | opens with it - the view as it was left |
| a project, linked | missing | offers to make it again |
| a project, not linked | one of the same name | offers to link it |
| no project in it - a slicer saved over it | one of the same name | says so, and offers to bring the project back from the `.3dfp` |
| a project, linked | holds work a run left unsaved | offers it back, unless another window has it open |

A `.3dfp` renamed beside its `.3mf` is still found by its id. The `.3dfp` keeps a fingerprint of
the `.3mf` as last saved, so a `.3mf` changed by another app can be told from one that was not.

### What the `.3dfp` holds

A 3MF package: every part of the `.3mf` as last saved - the model, its picture, the project part -
with the content types made to cover the parts below, so Windows' own 3MF thumbnail and preview
handler draws a `.3dfp` as it draws a `.3mf`. The app registers `.3dfp` for the user at each
start (HKCU, only when something differs): opened by the app, with that handler for the picture.

- `3DFastCraft/manifest.json` - format, id, the `.3mf`'s name, its fingerprint, when, by which version
- `3DFastCraft/project.json` - the project part exactly as written into the `.3mf`, versions and all:
  what brings a project back after a slicer saved over it
- `3DFastCraft/view.json` - the camera, perspective or orthographic, as last saved
- `3DFastCraft/session.json` - the index of the session: each step's number, label, time, object
  count and meshes; the work not yet saved and which run left it; the camera as last left
- `3DFastCraft/steps/000001.json`... - the plate after each step, every object naming its mesh by key
- `3DFastCraft/meshes/<key>.bin` - each mesh once, keyed by a hash of its content
- `3DFastCraft/unsaved.json` - the plate as it was when last written, while it had changes not saved

The session is appended where it lies: .NET rewrites a zip opened for update only from its first
changed entry, so a step costs milliseconds however big the file. Reading an entry in update mode
counts as changing it, so everything is read with the file opened for reading first.

### Phases

1. **The `.3dfp`** *(done)*: the file, the id and link, the checks on opening, opening a `.3dfp`,
   Save As, the question at New and the switch in File > Project, the view.
2. *(dropped for now: versions stay in the `.3mf`)*
3. **The session, kept as it goes** *(done)*: the plate after every step - each new step, undo and
   redo - held as it happens and appended to the `.3dfp` once the window is still for a moment,
   with the crash file's timing. Each mesh is stored once, by its content, and a step names the
   meshes it uses, so a move adds a few hundred bytes rather than a copy of the model. A project
   opened adds an "Opened" step unless it is the step the session ended on. Steps taken before
   the first save are held and written then. File > Session (Advanced) lists them newest first,
   draws the one chosen, goes back to it as one undo step, and forgets the steps before it. Past
   2,000 steps or 256 MB the oldest tenth goes.
4. **Crash recovery into the `.3dfp`** *(done)*: a project with a `.3dfp` keeps its unsaved work
   there, beside the session; the recovery folder holds only a note pointing at it, so the next
   start still offers it. Opening the project offers it too, when the start did not. Turned down
   or taken, it comes out of the `.3dfp`; the steps stay. A project without a `.3dfp` keeps using
   the folder.

### Limits

- The `.3mf`'s fingerprint is kept but not yet used: a slicer save is told by the project part
  being gone, which is the case that loses work.
- A `.3dfp` renamed beside its `.3mf` is found when opening, but saves write the one of the same
  name, and its session continues there.
- Only `.3dfp` is registered with Windows; the `.3mf` is left to whatever the user opens it with.
  Where Windows' 3MF handler is missing, a `.3dfp` shows the app's icon instead of a picture.

## Full project mode (later)

Not a separate app: the tools are the app, and a second one would have to share all of them. A
project folder, opened by the same app:

- **Parts**, each once, shared by everything below
- **An assembly**: the parts put together - the whole house
- **Plates**: as many as needed, each with some of the parts laid out to print, every part with
  its own place on its plate; each plate exports as its own `.3mf` for the slicer
- Editing a part changes it on the assembly and on its plate; a part can be marked printed

Builds on phases 1 and 3: the id, meshes stored by content, and steps as scene snapshots.
