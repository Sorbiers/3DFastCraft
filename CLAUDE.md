# 3DFastCraft

A WPF/.NET 8 replacement for the retired Microsoft 3D Builder. Millimetres, Z-up, 200 mm plate.
Built entirely with the `dotnet` CLI - there is no Visual Studio on this machine.

## Working rules

**Do not test unless asked.** Build to prove it compiles, and run `dotnet test` when the change
touches geometry that the suite covers. Do not drive the app to check a change by eye: say what
to look at and ask the user to verify. If a check is worth automating, put it in the `drive-app`
skill or a unit test rather than doing it by hand each time.

**Do not commit unless asked in that message.** Not after finishing, not because tests pass, not
to keep the branch tidy. The same goes for `git push`, tags and releases. Leave the work
uncommitted and say what changed.

**Do not update README.md as you go.** It is brought up to date in one pass before the context is
compacted, not after every change. The same for the screenshot.

**Prefer one large tool call to several small ones.** Batch edits into a single script; do not
re-read a file to confirm an edit landed.

## Traps that have cost time

**Bash heredocs mangle backslashes.** `\a` in a Python heredoc becomes a bell character and the
csproj stops parsing; `\n` becomes a real newline inside a C# string literal. For anything with
backslashes or escapes, use the Write/Edit tools or PowerShell's `[regex]::Replace`, and in C#
prefer `Environment.NewLine` to `\n`.

**Close the app before building.** A running instance locks `3DFastCraft.exe` and the build fails
with an MSBuild stack trace that never mentions why.

**A C# namespace cannot start with a digit.** Hence `RootNamespace=FastCraft3D` with
`AssemblyName=3DFastCraft`.

**Record struct `new()` skips the primary constructor's defaults.** It gives the zero value, so
options types need an explicit `Default` property.

**The BSP boolean is the fragile part.** Fine detail against a curved or faceted surface tears.
Anything built on it checks the result is watertight and refuses rather than shipping a broken
model - never relax that into a warning.

**Aggressive mesh healing makes things worse.** Both the sliver threshold and the weld tolerance
were tried wide and had to be pulled back in. If a repair is worth trying, offer it as a
candidate and keep it only if the result is measurably better.

## Layout

```
src/FastCraft3D/
  Geometry/   meshes, primitives, transforms, repair, sweeps, Csg/, Engraving/
  Model/      scene objects, scene, Commands/ (undo-redo)
  Io/         STL, OBJ, .3dfc project files
  Render/     the only Direct3D-aware layer, plus the on-screen manipulators
  View/       dialogs and selection mirroring
  ViewModels/ commands and application state
tools/ui/     PowerShell for driving the running app - see the drive-app skill
tests/FastCraft3D.Tests/
```

`Geometry/`, `Model/` and `Io/` must never reference renderer types.

## Style

Comments explain **why**, not what, and only where the reason is not obvious from the code -
usually because the obvious approach was tried and failed. Say what was tried and what went
wrong. British spelling. Test names are sentences about behaviour, not method names.
