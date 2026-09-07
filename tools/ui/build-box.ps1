# Builds a box with a hinged lid and a turn latch, in the running application.
#
# Dot-source drive.ps1, house.ps1 and this, then call one function per part on an empty plate.
# Everything below is done through the ribbon and the property fields - no geometry is written
# by hand - so what comes out is a test of the tools as much as it is a box.
#
#   . tools\ui\drive.ps1 ; . tools\ui\house.ps1 ; . tools\ui\build-box.ps1
#   $w = Get-AppWindow
#   Build-Box $w
#
# The model, in millimetres:
#
#   body      80 x 60 x 30 outside, 2.4 walls, open at the top
#   lid       80 x 60 x 3 plate with a lip that drops into the box on three sides
#   hinge     three knuckles on the back-top edge, r 3, on a 3 mm pin
#   latch     a peg on the front of the box and a bar that turns on it over the lid's lip
#
# The lip stops 10 mm short of the hinge side deliberately. A lip that ran all the way round
# would foul the back wall the moment the lid began to turn: a point 2.6 mm in front of the
# hinge axis and 3 mm below it swings *down* into the wall before it swings up.

$Out = "F:\3DFastCraft\build\box"

# --- The box, in millimetres ------------------------------------------------------------
$BoxX = 80.0
$BoxY = 60.0
$BoxZ = 30.0          # to the rim
$Wall = 2.4

$CavityX = $BoxX - 2 * $Wall   # 75.2
$CavityY = $BoxY - 2 * $Wall   # 55.2

$LidZ = 3.0
$LipZ = 3.0
$Slip = 0.4           # the gap everything that has to move gets, across the whole dimension

$HingeY = $BoxY / 2   # the axis sits on the back-top edge, so the knuckles never foul anything
$HingeZ = $BoxZ       # - a cylinder centred on its own axis cannot swing into anything
$Knuckle = 3.0        # knuckle radius
$Bore = 3.4           # the hole through them
$PinD = 3.0           # and the pin that goes in it

$NotchX = 26.0        # the gap in the box for the lid's knuckle
$LidKnuckleX = $NotchX - 2 * $Slip
$KnuckleX = 18.0      # how long each of the box's own knuckles is

$PegZ = 24.0          # the latch peg, on the front face
$PegD = 2.6
$ButtonHole = $PegD + $Slip

# --- Helpers ------------------------------------------------------------------------------

# A cylinder lying along X. Insert leaves it standing on Z, so it is turned first and placed
# afterwards - setting the position before the rotation moves the wrong thing.
function Add-Rod($w, [string]$name, [double]$diameter, [double]$length,
                 [double]$cx, [double]$cy, [double]$cz) {
    Add-Shape $w "Cylinder" $name 0 0 ($length / 2) $diameter $diameter $length | Out-Null
    Set-Number $w "RotationY" 90
    Start-Sleep -Milliseconds 400
    Set-Number $w "PositionX" $cx
    Set-Number $w "PositionY" $cy
    Set-Number $w "PositionZ" $cz
    Start-Sleep -Milliseconds 300
    Write-Output "rod $name"
}

# The same lying along Y, for the latch peg.
function Add-Peg($w, [string]$name, [double]$diameter, [double]$length,
                 [double]$cx, [double]$cy, [double]$cz) {
    Add-Shape $w "Cylinder" $name 0 0 ($length / 2) $diameter $diameter $length | Out-Null
    Set-Number $w "RotationX" 90
    Start-Sleep -Milliseconds 400
    Set-Number $w "PositionX" $cx
    Set-Number $w "PositionY" $cy
    Set-Number $w "PositionZ" $cz
    Start-Sleep -Milliseconds 300
    Write-Output "peg $name"
}

function Join-Onto($w, [string[]] $others, [string]$how) {
    Select-Objects $w (@(Body $w $others) + $others) | Out-Null
    Invoke-Tool $w "Object" $how | Out-Null
    Wait-Idle $w 300
    Start-Sleep -Seconds 2
}

# Puts planks on one face, using the pattern the Engrave panel now offers by name.
function Add-Planks($w, [string]$view, [double]$size, [double]$shape) {
    $name = @(Get-ObjectNames $w)[0]

    Select-Tab $w "View" | Out-Null
    Invoke-ByName $w $view | Out-Null
    Start-Sleep -Milliseconds 700
    Invoke-ByName $w "Zoom to fit" | Out-Null
    Start-Sleep -Seconds 1

    Select-Objects $w @($name) | Out-Null
    Invoke-Tool $w "Edit" "Engrave..." | Out-Null
    Start-Sleep -Seconds 1

    $picked = $false
    foreach ($p in @(@(590, 450), @(470, 450), @(700, 450), @(590, 380))) {
        & "F:\3DFastCraft\tools\ui\click.ps1" -X $p[0] -Y $p[1] | Out-Null
        Start-Sleep -Milliseconds 900
        if (@(Get-StatusText $w) -match "^Face \d") { $picked = $true; break }
    }
    if (-not $picked) { throw "no face was picked from the $view view" }

    Set-Pattern $w "Planks"
    Set-Number $w "EngraveSize" $size
    Set-Number $w "EngraveWidth" 0.5
    Set-Number $w "EngraveDepth" 0.35
    Set-Number $w "EngraveAspect" $shape
    Set-Raised $w $true

    Invoke-ByName $w "Engrave" | Out-Null
    Wait-Idle $w 300
    Start-Sleep -Seconds 2

    Write-Output "planked from $view -> $(@(Get-ObjectNames $w)[0])"
}

function Set-Pattern($w, [string]$label) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "EngravePattern")
    $box = $w.FindFirst($TS::Descendants, $cond)
    if (-not $box) { throw "no pattern picker" }

    $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 500

    $item = $box.FindFirst($TS::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $label)))
    if (-not $item) { throw "no pattern called '$label'" }

    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 600
}

function Set-Raised($w, [bool]$wanted) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "EngraveRaised")
    $cb = $w.FindFirst($TS::Descendants, $cond)
    $toggle = $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($toggle.Current.ToggleState -eq "On") -ne $wanted) { $toggle.Toggle() }
    Start-Sleep -Milliseconds 400
}

# --- Part one: the body -------------------------------------------------------------------

function Build-Body($w) {
    Clear-Plate $w | Out-Null
    Start-Sleep -Seconds 1

    # The shell. A subtraction rather than Hollow: Hollow rebuilds the surface on a voxel grid,
    # which rounds the corners of something that has to mate with a lid.
    Add-Shape $w "Cube" "outer" 0 0 ($BoxZ / 2) $BoxX $BoxY $BoxZ | Out-Null
    Add-Shape $w "Cube" "cavity" 0 0 ($Wall + ($BoxZ + 4) / 2) $CavityX $CavityY ($BoxZ + 4) | Out-Null
    Select-Objects $w @("outer", "cavity") | Out-Null
    Invoke-Tool $w "Object" "Subtract" | Out-Null
    Wait-Idle $w 120
    Start-Sleep -Seconds 2

    # Planks on the faces that are still flat. The front is patterned before the latch peg goes
    # on, because a face with a round hole in it is not one the retiler will take.
    Add-Planks $w "Left"  20.0 8.0
    Add-Planks $w "Right" 20.0 8.0
    Add-Planks $w "Front" 20.0 8.0

    # The hinge: a gap in the middle of the back-top edge for the lid's knuckle, and a knuckle
    # of the box's own either side of it.
    Add-Shape $w "Cube" "notch" 0 $HingeY $HingeZ $NotchX ($Knuckle * 3) ($Knuckle * 3) | Out-Null
    Join-Onto $w @("notch") "Subtract"

    Add-Rod $w "k1" ($Knuckle * 2) $KnuckleX -22 $HingeY $HingeZ
    Add-Rod $w "k2" ($Knuckle * 2) $KnuckleX  22 $HingeY $HingeZ
    Join-Onto $w @("k1", "k2") "Merge"

    Add-Rod $w "bore" $Bore ($BoxX + 20) 0 $HingeY $HingeZ
    Join-Onto $w @("bore") "Subtract"

    # The latch: a boss on the front face with a peg standing out of it.
    Add-Peg $w "boss" 8 3 0 (-($BoxY / 2) - 1.5) $PegZ
    Add-Peg $w "peg" $PegD 5 0 (-($BoxY / 2) - 4.5) $PegZ
    Join-Onto $w @("boss", "peg") "Merge"

    Export-Part $w "$Out\box-body.stl"
}

# --- Part two: the lid --------------------------------------------------------------------

function Build-Lid($w) {
    Clear-Plate $w | Out-Null
    Start-Sleep -Seconds 1

    Add-Shape $w "Cube" "plate" 0 0 ($BoxZ + $LidZ / 2) $BoxX $BoxY $LidZ | Out-Null

    # The lip, on three sides only - see the note at the top of this file.
    $lipX = $CavityX - $Slip
    $lipY = $CavityY - $Slip
    $back = 10.0
    Add-Shape $w "Cube" "lipF" 0 (-($lipY / 2) + 1.25) ($BoxZ - $LipZ / 2) $lipX 2.5 $LipZ | Out-Null
    Add-Shape $w "Cube" "lipL" (-($lipX / 2) + 1.25) (-($back / 2)) ($BoxZ - $LipZ / 2) `
        2.5 ($lipY - $back) $LipZ | Out-Null
    Add-Shape $w "Cube" "lipR" (($lipX / 2) - 1.25) (-($back / 2)) ($BoxZ - $LipZ / 2) `
        2.5 ($lipY - $back) $LipZ | Out-Null

    # The catch the latch turns over, on the front edge.
    Add-Shape $w "Cube" "catch" 0 (-($BoxY / 2) - 1.25) ($BoxZ + $LidZ / 2) 20 2.5 $LidZ | Out-Null

    Select-Objects $w @("plate", "lipF", "lipL", "lipR", "catch") | Out-Null
    Invoke-Tool $w "Object" "Merge" | Out-Null
    Wait-Idle $w 300
    Start-Sleep -Seconds 2

    Add-Planks $w "Top" 20.0 8.0

    Add-Rod $w "k3" ($Knuckle * 2) $LidKnuckleX 0 $HingeY $HingeZ
    Join-Onto $w @("k3") "Merge"

    Add-Rod $w "bore" $Bore ($BoxX + 20) 0 $HingeY $HingeZ
    Join-Onto $w @("bore") "Subtract"

    # Relief over the box's own knuckles. The lid is a full-width plate and the knuckles stand
    # proud of the back-top edge, so without this the plate lands on them and the lid will not
    # shut. That is exactly what Fit check reported the first time round - "box-lid and box-body
    # overlap by ... 0.174 cm3 of shared material", on a pair that has to be free to move.
    Add-Rod $w "relief1" ($Knuckle * 2 + 2 * $Slip) ($KnuckleX + 2 * $Slip) -22 $HingeY $HingeZ
    Add-Rod $w "relief2" ($Knuckle * 2 + 2 * $Slip) ($KnuckleX + 2 * $Slip)  22 $HingeY $HingeZ
    Join-Onto $w @("relief1", "relief2") "Subtract"

    Export-Part $w "$Out\box-lid.stl"
}

# --- Parts three and four: what moves ------------------------------------------------------

function Build-Fittings($w) {
    Clear-Plate $w | Out-Null
    Start-Sleep -Seconds 1

    Add-Rod $w "pin" $PinD ($BoxX + 4) 0 0 ($PinD / 2)
    Export-Part $w "$Out\hinge-pin.stl"

    Clear-Plate $w | Out-Null
    Start-Sleep -Seconds 1

    Add-Shape $w "Cube" "bar" 0 0 1.5 16 6 3 | Out-Null
    Add-Shape $w "Cylinder" "eye" 0 0 1.5 $ButtonHole $ButtonHole 6 | Out-Null
    Select-Objects $w @("bar", "eye") | Out-Null
    Invoke-Tool $w "Object" "Subtract" | Out-Null
    Wait-Idle $w 120
    Start-Sleep -Seconds 2

    Export-Part $w "$Out\latch-button.stl"
}

function Build-Box($w) {
    if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Force $Out | Out-Null }

    Build-Body $w
    Build-Lid $w
    Build-Fittings $w

    Write-Output "the box is built"
}
