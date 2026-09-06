---
name: drive-app
description: Drive the running 3DFastCraft window - click ribbon buttons, set property fields, click and drag in the 3D viewport, and capture screenshots. Use when a change has to be seen or exercised in the real app rather than in a unit test.
---

# Driving 3DFastCraft

The scripts live in `tools/ui/`. They talk to the app through UI Automation for anything with a
name, and through real cursor moves for the 3D viewport, which exposes nothing to automation.

Dot-source `drive.ps1` in every PowerShell call - types and functions do not survive between
calls.

## Starting

```powershell
Start-Process "F:\3DFastCraft\src\FastCraft3D\bin\Debug\net8.0-windows\win-x64\3DFastCraft.exe"
Start-Sleep -Seconds 6
. .\tools\ui\drive.ps1
$w = Get-AppWindow
```

Build first, and **close any running instance before building** - it holds a lock on the exe and
the build fails with an MSBuild stack trace that says nothing about why.

## The ribbon and panels

| Call | Does |
|---|---|
| `Select-Tab $w "Object"` | Switches ribbon tab |
| `Invoke-ByName $w "Cube"` | Clicks a button by its visible text |
| `Get-EditBoxes $w` | Every text box in document order |
| `Set-EditValue $b[3] "17"` | Sets one, committing on focus loss |
| `Get-StatusText $w` | All the text labels, for reading the status bar |

**Re-fetch `Get-EditBoxes` before every `Set-EditValue`.** The list goes stale as panels appear
and disappear, and a stale element writes into the wrong field or silently does nothing. Read the
values back afterwards to confirm - `SetValue` occasionally prepends instead of replacing.

**The indices shift with the ribbon tab.** On the View tab an extra box (the bed size) comes
first, so the properties panel starts at 1 rather than 0. Print the values before relying on an
index.

A combo box needs its own dance:

```powershell
$cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
$combo = @($w.FindAll($TS::Descendants, $cond))[1]
$combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 500
$lc = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$pick = @($combo.FindAll($TS::Descendants, $lc)) | Where-Object { $_.Current.Name -eq "Cylindrical" } | Select-Object -First 1
$pick.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
```

Search the items **under the combo**, not under the window: a name like "Cylindrical" also
matches the combo's own text and selecting that throws "Unsupported Pattern".

## The viewport

Nothing in the 3D view has a name, so it takes real cursor input. Coordinates are relative to the
window's top-left corner.

```powershell
.\tools\ui\click.ps1 -X 600 -Y 465                              # pick a face, select an object
.\tools\ui\click.ps1 -X 550 -Y 487 -Drag -ToX 900 -ToY 620      # drag a gizmo handle
.\tools\ui\scroll.ps1 -X 600 -Y 500 -Notches 4                  # zoom out; -In to zoom in
```

Take a screenshot first and read the handle positions off it. Arrow colours are **red X, green Y,
blue Z**, and which one lies where on screen depends entirely on the camera - in the Front view
the horizontal arrows are not necessarily X. Use the Isometric view when the axis matters.

`scroll.ps1 -Notches 0` is a cheap way to park the cursor somewhere harmless before a screenshot,
so no tooltip is caught in it.

## Screenshots

```powershell
.\tools\ui\shot.ps1 -Out "check.png" -Dir $env:TEMP
```

It fronts the window, captures it, and trims the frame - the window rect otherwise takes in a
sliver of whatever is behind. Crop and magnify a region to inspect small things:

```powershell
python -c "from PIL import Image; Image.open('check.png').crop((480,380,760,570)).resize((1120,760)).save('zoom.png')"
```

## Things that will waste a turn

- A modal message box is a separate window; `Get-AppWindow` cannot see it. Dismiss it by clicking
  where its OK button is, then carry on.
- A dialog also swallows viewport clicks aimed underneath it.
- After a long boolean the app stays busy. Wait and re-screenshot rather than clicking again.
