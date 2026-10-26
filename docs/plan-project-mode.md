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

A `.3dfp` renamed beside its `.3mf` is still found by its id. The `.3dfp` keeps a fingerprint of
the `.3mf` as last saved, so a `.3mf` changed by another app can be told from one that was not.

### What the `.3dfp` holds

A zip, like a 3MF:

- `3DFastCraft/manifest.json` - format, id, the `.3mf`'s name, its fingerprint, when, by which version
- `3DFastCraft/project.json` - the project part exactly as written into the `.3mf`, versions and all:
  what brings a project back after a slicer saved over it
- `3DFastCraft/view.json` - the camera, perspective or orthographic
- later: `3DFastCraft/steps/` and `3DFastCraft/meshes/` - the session (phase 3)

### Phases

1. **The `.3dfp`**: the file, the id and link, the checks on opening, opening a `.3dfp`, Save As,
   the question at New and the switch in File > Project, the view.
2. *(dropped for now: versions stay in the `.3mf`)*
3. **The session, kept as it goes**: what Export session writes - the plate after every step -
   appended to the `.3dfp` after each step instead of exported at the end. Each mesh is stored
   once, by its content, and a step names the meshes it uses, so a drag adds a few bytes rather
   than a copy of the model. Read back as a timeline to look at or step back to; a cap and a
   Compact command keep it from growing without end.
4. **Crash recovery** writes into the `.3dfp` instead of a folder of its own.

## Full project mode (later)

Not a separate app: the tools are the app, and a second one would have to share all of them. A
project folder, opened by the same app:

- **Parts**, each once, shared by everything below
- **An assembly**: the parts put together - the whole house
- **Plates**: as many as needed, each with some of the parts laid out to print, every part with
  its own place on its plate; each plate exports as its own `.3mf` for the slicer
- Editing a part changes it on the assembly and on its plate; a part can be marked printed

Builds on phases 1 and 3: the id, meshes stored by content, and steps as scene snapshots.
