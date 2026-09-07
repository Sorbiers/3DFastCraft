# Building blocks for driving a model into the running app.
#
# Dot-source drive.ps1 first. Everything here works in the model's own coordinates - the corner
# of the building at (0,0,0) - and converts to the plate, which has its origin in the middle.
#
# Every numeric parameter is typed, and that is not decoration. PowerShell parses a bare negative
# number in argument position as a *string*, so an untyped $y receiving -1 holds "-1" - and
# "-1" + 2.75 concatenates to "-12.75" instead of adding to 1.75. Every opening in the first
# house model whose coordinate was negative landed somewhere else, and the whole front and left
# elevations came out blank. Typing the parameters converts on the way in and the class of bug
# cannot recur.
#
# The properties panel reads: Name, PosX, PosY, PosZ, SizeX, SizeY, SizeZ, RotX, RotY, RotZ.
# Position is the object's centre, so a box's corner has to be turned into one.

Add-Type -AssemblyName System.Windows.Forms
if (-not ("Wheel" -as [type])) {
  Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class Wheel {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, IntPtr e);
}
"@
}

$HouseWidth = 110.0
$HouseDepth = 90.0

# Finds a field by the name the app gives it rather than by where it happens to sit.
#
# Counting text boxes down the panel does not work: the count shifts with the ribbon tab (the
# View tab puts the bed size first), with the manipulator mode, and with whichever tool panel is
# open - so "set position X" would quietly write the object name instead. Every field now
# carries an AutomationId.
function Set-Field($w, $id, $value) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  $e = $w.FindFirst($TS::Descendants, $cond)
  if (-not $e) { throw "no field called '$id'" }

  Set-EditValue $e $value
}

function Get-Field($w, $id) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  $e = $w.FindFirst($TS::Descendants, $cond)
  if (-not $e) { return $null }

  return $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Set-Number($w, [string]$id, [double]$value) {
  Set-Field $w $id ([string]::Format([cultureinfo]::InvariantCulture, "{0:0.###}", $value))
}

function Add-Box($w, [string]$name, [double]$x, [double]$y, [double]$z,
                 [double]$sx, [double]$sy, [double]$sz) {
  Add-Shape $w "Cube" $name ($x + $sx / 2.0 - $HouseWidth / 2.0) ($y + $sy / 2.0 - $HouseDepth / 2.0) `
    ($z + $sz / 2.0) $sx $sy $sz 0
}

# One primitive, sized, turned and placed - all in plate coordinates, which have their origin in
# the middle of the bed.
function Add-Shape($w, [string]$shape, [string]$name, [double]$cx, [double]$cy, [double]$cz,
                   [double]$sx, [double]$sy, [double]$sz, [double]$rz = 0) {
  Select-Tab $w "Insert" | Out-Null
  Invoke-ByName $w $shape | Out-Null
  Start-Sleep -Milliseconds 250

  Set-Field $w "ObjectName" $name

  # Size before position: resizing works about the centre, so doing it after would move it.
  Set-Number $w "SizeX" $sx
  Set-Number $w "SizeY" $sy
  Set-Number $w "SizeZ" $sz

  if ($rz -ne 0) { Set-Number $w "RotationZ" $rz }

  Set-Number $w "PositionX" $cx
  Set-Number $w "PositionY" $cy
  Set-Number $w "PositionZ" $cz

  Start-Sleep -Milliseconds 150
  Write-Output "$shape $name"
}

function Get-ObjectList($w) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::List)
  return @($w.FindAll($TS::Descendants, $cond)) | Select-Object -First 1
}

# Wrap every call in @() - a single name comes back as a bare string, and $names[0] on a string
# gives "U" rather than "Union". Returning ,@() from here would only move the problem, because
# the callers wrap too and the array ends up inside another one.
function Get-ObjectNames($w) {
  $list = Get-ObjectList $w
  if (-not $list) { return @() }
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
  return @($list.FindAll($TS::Descendants, $cond)) | ForEach-Object { $_.Current.Name }
}

# Picks objects by name in the list on the right. The first replaces the selection, the rest
# are added to it - which is what a boolean or a group needs.
function Select-Objects($w, [string[]] $names) {
  # Tried more than once. The list is still settling after a boolean when the next selection
  # arrives, and a row picked mid-update comes back unselected - which turns a two-object
  # operation into a one-object one with nothing said about it.
  for ($try = 1; $try -le 3; $try++) {
    try {
      Set-Selection $w $names
      Write-Output "selected: $($names -join ', ')"
      return
    } catch {
      if ($try -eq 3) { throw }
      Start-Sleep -Milliseconds 800
    }
  }
}

function Set-Sticky($w, [bool]$wanted) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "Sticky selection")

  foreach ($e in @($w.FindAll($TS::Descendants, $cond))) {
    try {
      $t = $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
      $on = $t.Current.ToggleState -eq "On"
      if ($on -ne $wanted) { $t.Toggle(); Start-Sleep -Milliseconds 300 }
      return
    } catch { }
  }
}

function Set-Selection($w, [string[]] $names) {
  $list = Get-ObjectList $w
  if (-not $list) { throw "no object list" }

  # The list is virtualised, so a row scrolled out of sight is not in the automation tree at all
  # and FindAll simply does not see it. ItemContainerPattern is the way in: it realises the item
  # by name whether or not it is on screen.
  $container = $list.GetCurrentPattern([System.Windows.Automation.ItemContainerPattern]::Pattern)

  # Cleared first, then picked one by one, then counted.
  #
  # Selections went wrong often enough during the build that two explanations got written down
  # here - that sticky on makes a second pick toggle the first off, and that sticky off makes an
  # add replace rather than join. Both were tested directly afterwards and neither reproduces:
  # Select then AddToSelection gives two selected rows either way, and selecting an already
  # selected row leaves it selected. Sticky is about clicks in the *viewport*, which is what its
  # own summary says, and it does not reach the list at all.
  #
  # What was really happening is below - the list is still being rebuilt for a moment after a
  # boolean, and a row picked during that window comes back unselected. Clearing first and
  # counting afterwards is what makes it reliable; the sticky call is left only so the run starts
  # from a known state.
  Set-Sticky $w $true
  try { Invoke-Label $w "Deselect all" | Out-Null } catch { }

  $first = $true
  foreach ($name in $names) {
    $item = $container.FindItemByProperty($null, $AE::NameProperty, $name)
    if (-not $item) { throw "object '$name' not in the list" }

    try {
      $item.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView()
    } catch { }

    $p = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($first) { $p.Select() } else { $p.AddToSelection() }
    $first = $false
    Start-Sleep -Milliseconds 150

    if (-not $p.Current.IsSelected) { $p.AddToSelection(); Start-Sleep -Milliseconds 150 }
  }

  $got = @($list.GetCurrentPattern(
    [System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection())

  # Selecting through the automation pattern stops working once the session has exported
  # anything - the first row goes, the second replaces it rather than joining it, and it stays
  # that way until the app is restarted. Ctrl+click behaves in every state that has been tried,
  # so it is the fallback rather than the first choice: it needs the row on screen, and it moves
  # the pointer, which the pattern does not.
  if ($got.Count -ne $names.Count) {
    Select-ByClicking $w $names
    $got = @($list.GetCurrentPattern(
      [System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection())
  }

  if ($got.Count -ne $names.Count) {
    throw "wanted $($names.Count) selected but got $($got.Count): $(($got | ForEach-Object { $_.Current.Name }) -join ', ')"
  }
}

# Picks the rows the way a person would: click the first, Ctrl+click the rest.
function Select-ByClicking($w, [string[]] $names) {
  $list = Get-ObjectList $w
  $container = $list.GetCurrentPattern([System.Windows.Automation.ItemContainerPattern]::Pattern)

  $first = $true
  foreach ($name in $names) {
    $item = $container.FindItemByProperty($null, $AE::NameProperty, $name)
    if (-not $item) { throw "object '$name' not in the list" }

    try {
      $item.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView()
      Start-Sleep -Milliseconds 200
    } catch { }

    $r = $item.Current.BoundingRectangle
    [Wheel]::SetCursorPos([int]($r.X + 30), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 200

    if (-not $first) { [Wheel]::keybd_event(0x11, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 120 }
    [Wheel]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [Wheel]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 200
    if (-not $first) { [Wheel]::keybd_event(0x11, 0, 2, [IntPtr]::Zero) }

    $first = $false
    Start-Sleep -Milliseconds 250
  }
}

function Invoke-Tool($w, $tab, $button) {
  Select-Tab $w $tab | Out-Null
  Start-Sleep -Milliseconds 200
  Invoke-ByName $w $button | Out-Null
  Write-Output "$tab > $button"
}

# Waits for a long boolean to finish rather than clicking again into a busy app.
function Wait-Idle($w, $seconds = 120) {
  for ($i = 0; $i -lt $seconds * 2; $i++) {
    Start-Sleep -Milliseconds 500
    $text = Get-StatusText $w
    if ($text -notmatch "\.\.\.$") { return }
  }
  Write-Output "still busy after $seconds s"
}

# Clicks something that only exposes a text label.
#
# The entries in the Selection menu - Group, Ungroup, Select all - come through UI Automation as
# bare Text with no Invoke on them, so there is nothing to press. Clicking where the label is
# works; exposing them properly would be better, and is written up in the notes.
function Invoke-Label($w, $name) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)

  # The Selection menu is properly named now, so the ordinary route works. Everything that only
  # exposes a label still falls through to the click below.
  foreach ($e in @($w.FindAll($TS::Descendants, $cond))) {
    try {
      $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      Start-Sleep -Milliseconds 450
      Write-Output "invoked: $name"
      return
    } catch { }
  }

  $e = $w.FindFirst($TS::Descendants, $cond)
  if (-not $e) { throw "label '$name' not found" }

  $r = $e.Current.BoundingRectangle
  $win = $w.Current.BoundingRectangle
  $x = [int]($r.X + $r.Width / 2 - $win.X)
  $y = [int]($r.Y + $r.Height / 2 - $win.Y)

  & "F:\3DFastCraft\tools\ui\click.ps1" -X $x -Y $y | Out-Null
  Start-Sleep -Milliseconds 450
  Write-Output "clicked label: $name"
}

# Exports the plate to an STL. The save dialog is a Win32 one, so the path is typed rather than
# set through automation, and an existing file asks to be replaced.
function Export-Stl($w, $path) {
  Add-Type -AssemblyName System.Windows.Forms
  Select-Tab $w "File" | Out-Null
  Start-Sleep -Milliseconds 400
  Invoke-ByName $w "Export STL / OBJ..." | Out-Null
  Start-Sleep -Seconds 2

  & "F:\3DFastCraft\tools\ui\click.ps1" -X 791 -Y 596 | Out-Null
  Start-Sleep -Seconds 3

  [System.Windows.Forms.SendKeys]::SendWait($path)
  Start-Sleep -Milliseconds 700
  [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
  Start-Sleep -Seconds 2
  [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")   # replace, if it is already there
  Start-Sleep -Seconds 2

  Write-Output "exported $path"
}

# Empties the plate without going near the unsaved-changes prompt that File > New raises.
function Clear-Plate($w) {
  Invoke-Label $w "Select all" | Out-Null
  Start-Sleep -Milliseconds 300
  Invoke-Tool $w "Object" "Delete" | Out-Null
  Start-Sleep -Milliseconds 500
}

# --- Building up a model -----------------------------------------------------------------
#
# These three were written for the house and are not about houses at all, so they live here with
# the rest of the driver rather than in one model's script.

# The one object on the plate that is not in the given list - the thing being built up, whatever
# the last boolean decided to call it.
function Body($w, [string[]] $others) {
    $all = @(Get-ObjectNames $w)
    $found = @($all | Where-Object { $_ -notin $others })
    if ($found.Count -lt 1) { throw "nothing on the plate to work on" }
    return [string]$found[0]
}

function Wait-Ready($w, $seconds = 30) {
    for ($i = 0; $i -lt $seconds * 2; $i++) {
        try {
            # A dialog left open disables every ribbon button with nothing on screen to say so,
            # which is worth failing loudly on rather than puzzling over later.
            $cond = New-Object System.Windows.Automation.PropertyCondition(
                $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
            $open = @($w.FindAll($TS::Descendants, $cond))
            if ($open.Count -eq 0) { return }
            Write-Output "waiting on dialog: $($open[0].Current.Name)"
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    throw "the app never came back to itself"
}

function Export-Part($w, $path) {
    if (Test-Path $path) { [System.IO.File]::Delete($path) }

    Add-Type -AssemblyName System.Windows.Forms
    Select-Tab $w "File" | Out-Null
    Start-Sleep -Milliseconds 500
    Invoke-ByName $w "Export STL / OBJ..." | Out-Null
    Start-Sleep -Seconds 2

    & "F:\3DFastCraft\tools\ui\click.ps1" -X 791 -Y 596 | Out-Null
    Start-Sleep -Seconds 4

    [System.Windows.Forms.SendKeys]::SendWait("^a")
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait($path)
    Start-Sleep -Milliseconds 900
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    Start-Sleep -Seconds 4

    Wait-Ready $w
    Write-Output "exported $path"
}

# --- Dialogs -----------------------------------------------------------------------------
#
# A dialog is its own top-level window, so it has to be found from the desktop rather than from
# inside the main window - which is also why a dialog left open makes every ribbon button look
# mysteriously disabled.

function Get-Dialog($title, $seconds = 10) {
  $cond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $title)),
    (New-Object System.Windows.Automation.PropertyCondition(
      $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))

  for ($i = 0; $i -lt $seconds * 4; $i++) {
    # A WPF dialog owned by the main window shows up inside its subtree rather than beside it on
    # the desktop, so both places have to be looked in.
    foreach ($root in @($AE::RootElement, (Get-AppWindow))) {
      $d = $root.FindFirst($TS::Descendants, $cond)
      if ($d) { return $d }
    }
    Start-Sleep -Milliseconds 250
  }

  throw "dialog '$title' did not appear"
}

function Set-DialogField($dialog, $id, $value) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  $e = $dialog.FindFirst($TS::Descendants, $cond)
  if (-not $e) { throw "no field '$id' in the dialog" }
  Set-EditValue $e ([string]$value)
}

function Invoke-DialogButton($dialog, $name) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
  $e = $dialog.FindFirst($TS::Descendants, $cond)
  if (-not $e) { throw "no button '$name' in the dialog" }

  # Open in the shell's file dialog is a split button, which carries no Invoke pattern at all -
  # so a click on its rectangle is the fallback rather than an error.
  try {
    $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  } catch [System.InvalidOperationException] {
    $r = $e.Current.BoundingRectangle
    [Wheel]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 200
    [Wheel]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [Wheel]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  }
  Start-Sleep -Milliseconds 600
}

# Inserts a flight of steps through the new tool, which builds it as one solid.
function Add-Stair($w, [string]$name, [double]$rise, [double]$run, [double]$width, [int]$steps,
                   [double]$cx, [double]$cy, [double]$cz, [double]$rz) {
  Invoke-Tool $w "Insert" "Stair..." | Out-Null
  $d = Get-Dialog "Stair"
  Set-DialogField $d "StairRise" $rise
  Set-DialogField $d "StairRun" $run
  Set-DialogField $d "StairWidth" $width
  Set-DialogField $d "StairSteps" $steps
  Invoke-DialogButton $d "StairAccept"
  Start-Sleep -Milliseconds 800

  Set-Field $w "ObjectName" $name
  if ($rz -ne 0) { Set-Number $w "RotationZ" $rz }
  Set-Number $w "PositionX" $cx
  Set-Number $w "PositionY" $cy
  Set-Number $w "PositionZ" $cz
  Write-Output "stair $name"
}

# Repeats the selection, which is how a row of dowels is made now.
function Invoke-Repeat($w, [int]$copies, [double]$sx, [double]$sy, [double]$sz, [bool]$anchor = $false) {
  Invoke-Tool $w "Object" "Repeat..." | Out-Null
  $d = Get-Dialog "Repeat"
  Set-DialogField $d "RepeatCount" $copies
  Set-DialogField $d "RepeatStepX" $sx
  Set-DialogField $d "RepeatStepY" $sy
  Set-DialogField $d "RepeatStepZ" $sz
  if ($anchor) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "RepeatAnchor")
    $cb = $d.FindFirst($TS::Descendants, $cond)
    if ($cb) { $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
  }
  Invoke-DialogButton $d "RepeatAccept"
  Start-Sleep -Milliseconds 800
  Write-Output "repeated x$copies"
}

# Imports a model file.
#
# The common-item dialog is a shell window, not ours, and it fights back. SetValue on its name
# box times out. Typing and pressing Enter takes whatever the box autocompleted from history,
# which silently imported the previous file a second time. Its file name box is not exposed as
# an Edit at all - the first Edit in the tree is a *cell of the file list*, so clicking "the
# first edit" put focus on the list, where typing does type-ahead selection and a stray {DEL}
# sends the selected files to the recycle bin. Three exported parts went that way.
#
# So: click the pane the dialog calls 1148, which is the file name box; never send DEL; and
# confirm afterwards that the object that arrived is the one that was asked for.
function Import-Model($w, [string]$path) {
  if (-not (Test-Path $path)) { throw "no such file: $path" }
  $full = (Resolve-Path $path).Path
  $want = [System.IO.Path]::GetFileNameWithoutExtension($full)
  $before = @(Get-ObjectNames $w).Count

  Invoke-Tool $w "Insert" "Import..." | Out-Null
  $d = Get-Dialog "Import model" 20

  $box = $d.FindFirst($TS::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "1148")))
  if (-not $box) { throw "the import dialog has no file name box" }

  $r = $box.Current.BoundingRectangle
  [Wheel]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
  Start-Sleep -Milliseconds 250
  [Wheel]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  [Wheel]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 400

  [System.Windows.Forms.SendKeys]::SendWait("^a")
  Start-Sleep -Milliseconds 150
  [System.Windows.Forms.SendKeys]::SendWait($full)
  Start-Sleep -Milliseconds 700

  Invoke-DialogButton $d "Open"
  Start-Sleep -Seconds 2
  Wait-Idle $w 240

  # A dialog left open makes every ribbon button read as disabled, so make sure it is gone.
  try { $still = Get-Dialog "Import model" 1 } catch { $still = $null }
  if ($still) {
    try { Invoke-DialogButton $still "Cancel" } catch {}
    throw "the import dialog is still up - '$full' was not opened"
  }

  $names = @(Get-ObjectNames $w)
  if ($names.Count -le $before) { throw "nothing was imported from '$full'" }
  if ($names[-1] -notlike "$want*") {
    throw "the import brought in '$($names[-1])', not '$want' - the file name box was not focused"
  }
}
