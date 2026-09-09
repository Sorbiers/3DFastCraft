# Contributing

Contributions are welcome — bug reports, models that break something, and patches alike.

## Reporting something

A bug report is most useful with the model that caused it. **About** on the File tab has a
**Copy details** button that puts the version, the graphics adapter and the .NET runtime on the
clipboard; paste that in. If a shape came out wrong, attach the `.3dfc` project — it carries the
whole plate, so the failure can be reproduced exactly rather than guessed at.

## Patches

```bash
dotnet build
dotnet test
```

Both must pass. There is no Visual Studio in this project's history and none is needed; everything
is done with the `dotnet` CLI.

A few things the codebase asks of a change:

- **`Geometry/`, `Model/` and `Io/` must never reference renderer types.** The geometry has to be
  testable without a graphics device, and it is.
- **A change to geometry comes with a test.** Test names are sentences about behaviour, not method
  names — `ASplitOfAScanLeavesBothHalvesClosed`, not `TestSplit`.
- **Comments explain why, not what**, and only where the reason is not obvious from the code —
  usually because the obvious approach was tried and failed. Say what was tried and what went
  wrong.
- **British spelling**, in code and in prose.
- **The boolean engine is the fragile part.** Anything built on it checks that its result is
  watertight and refuses rather than shipping a broken model. Do not relax that into a warning.

## Licensing of contributions

The project is BSD 3-Clause with the Commons Clause, and commercial licences are granted
separately — see [LICENSE](LICENSE). By opening a pull request you grant the copyright holder a
perpetual, irrevocable, worldwide, royalty-free licence to use, modify, relicense and distribute
your contribution as part of the software, including under that commercial licence. You keep your
own copyright in what you wrote.

If that is not something you want to grant, say so in the pull request — an idea described in an
issue is worth having too.
