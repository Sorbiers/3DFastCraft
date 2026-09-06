---
name: ship
description: Cut a 3DFastCraft release - build the self-contained exe, tag, and publish it to GitHub with notes. Use only when the user explicitly asks to release or publish a version.
---

# Shipping a release

Only ever run this when the user has asked for it in so many words. It pushes and publishes.

## 1. Check, then build

```powershell
Stop-Process -Name "3DFastCraft" -Force -ErrorAction SilentlyContinue
dotnet test --nologo
.\build.bat
```

`build.bat` publishes self-contained single-file to `build\3DFastCraft.exe` (~160 MB, not in
git). It refuses if the app is running, because a running instance locks the exe.

Never add `PublishTrimmed`: WPF resolves XAML types reflectively and trimming breaks the app at
runtime.

## 2. Tag

Versions are `vMAJOR.MINOR.PATCH`. New tools are a minor bump.

```powershell
git tag -a v1.2.0 -m "3DFastCraft 1.2.0"
git push origin main
git push origin v1.2.0
```

## 3. Publish

Write the notes to a file first - the body is long and awkward as an argument.

```bash
gh release create v1.2.0 "build/3DFastCraft.exe" --title "3DFastCraft 1.2.0" --notes-file notes.md
gh release view v1.2.0 --json assets --jq '.assets[] | "\(.name) \(.size)"'
```

Model the notes on the previous release (`gh release view v1.1.0`). What each one carries:

- The epigraph: *In loving memory of Windows 3D Builder. Rest in peace.*
- A **Download** section linking the asset by its full release URL, saying it is a single
  self-contained file needing no .NET, and warning that SmartScreen will object to an unsigned
  exe the first time (*More info* → *Run anyway*).
- **New in x.y.z**, grouped by what the user would go looking for rather than by commit.
- **Known limits** - anything the app now refuses to do, stated plainly.
- The licence line: free for personal non-commercial use, models made with it are the user's own.

Get the change list from `git log --oneline vPREVIOUS..HEAD`.

## 4. Say what shipped

Give the release URL, the tag, and the asset size. Mention any limit worth knowing before they
test it.
