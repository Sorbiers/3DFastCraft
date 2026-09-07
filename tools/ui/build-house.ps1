# Builds the 1:87 house, part by part, in the running application.
#
# Dot-source drive.ps1 and house.ps1 first, then call one function per part. Each ends by
# exporting its STL, and each is meant to be run on an empty plate - the app is driven, not
# scripted around, so every box here is an Insert followed by the property fields, and every
# join is a real Subtract or Merge off the ribbon.
#
# Why a script rather than typing: the first house was built by hand and five of its ten openings
# came out somewhere else without anyone noticing. A script can be read, re-run and checked
# against tools/verify-stl.py, and the numbers below are the model in one place.

# --- The building, in millimetres at 1:87 ---------------------------------------------
$Width = 110.0        # 9.57 m across the front
$Depth = 90.0         # 7.83 m front to back
$Wall = 3.5           # 30 cm exterior
$Part = 1.5           # 13 cm partition
$Slab = 2.5           # 22 cm floor slab
$FloorHeight = 30.0   # 2.61 m ceiling
$WallTop = 32.5       # $Slab + $FloorHeight

$SillZ = 10.5         # 70 cm sill above a 22 cm slab
$WindowH = 13.0       # 1.13 m
$DoorZ = 2.5
$DoorH = 23.0         # 2.0 m

$PinR = 1.3           # 2.6 mm dowels
$Pins = @(@(14.0, 1.75), @(96.0, 1.75), @(14.0, 88.25), @(96.0, 88.25))

# Bricks one wall. Every face can be reached head-on now, so nothing has to be turned round and
# turned back - which is what used to shift the model a few tenths of a millimetre each time.
function Add-Brick($w, [string]$view, [double]$size, [double]$line, [double]$depth) {
    $name = @(Get-ObjectNames $w)[0]

    Select-Tab $w "View" | Out-Null
    Invoke-ByName $w $view | Out-Null
    Start-Sleep -Milliseconds 600
    Invoke-ByName $w "Zoom to fit" | Out-Null
    Start-Sleep -Seconds 1

    Select-Objects $w @($name) | Out-Null
    Invoke-Tool $w "Edit" "Engrave..." | Out-Null
    Start-Sleep -Seconds 1

    # The middle of a wall is where the front door is, so clicking the middle of the viewport
    # picks nothing at all. Try a few places and stop at the first that lands on a face.
    $picked = $false
    foreach ($p in @(@(590, 420), @(430, 420), @(760, 420), @(590, 300), @(430, 560))) {
        & "F:\3DFastCraft\tools\ui\click.ps1" -X $p[0] -Y $p[1] | Out-Null
        Start-Sleep -Milliseconds 900

        if (@(Get-StatusText $w) -match "^Face \d") { $picked = $true; break }
    }
    if (-not $picked) { throw "no face was picked from the $view view" }

    Set-Number $w "EngraveSize" $size
    Set-Number $w "EngraveWidth" $line
    Set-Number $w "EngraveDepth" $depth

    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "EngraveRaised")
    $cb = $w.FindFirst($TS::Descendants, $cond)
    $toggle = $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -eq "Off") { $toggle.Toggle() }
    Start-Sleep -Milliseconds 500

    Invoke-ByName $w "Engrave" | Out-Null
    Wait-Idle $w 300
    Start-Sleep -Seconds 2

    Write-Output "bricked from $view -> $(@(Get-ObjectNames $w)[0])"
}

# --- Part two: the living floor -----------------------------------------------------------

function Add-Shell($w) {
    Add-Box $w "slab"  0 0 0 $Width $Depth $Slab | Out-Null
    Add-Box $w "outer" 0 0 $Slab $Width $Depth $FloorHeight | Out-Null
    Add-Box $w "inner" $Wall $Wall ($Slab - 1) ($Width - 2 * $Wall) ($Depth - 2 * $Wall) ($FloorHeight + 2) | Out-Null

    Select-Objects $w @("outer", "inner") | Out-Null
    Invoke-Tool $w "Object" "Subtract" | Out-Null
    Wait-Idle $w 120
    Start-Sleep -Seconds 2

    Select-Objects $w @("slab", "Subtract") | Out-Null
    Invoke-Tool $w "Object" "Merge" | Out-Null
    Wait-Idle $w 120
    Start-Sleep -Seconds 2

    Write-Output "shell"
}

function Add-Partitions($w) {
    Add-Box $w "p1" $Wall (48 - $Part / 2) $Slab ($Width - 2 * $Wall) $Part $FloorHeight | Out-Null
    Add-Box $w "p2" (44 - $Part / 2) $Wall $Slab $Part (48 - $Wall) $FloorHeight | Out-Null
    Add-Box $w "p3" (70 - $Part / 2) $Wall $Slab $Part (48 - $Wall) $FloorHeight | Out-Null
    Add-Box $w "p4" (44 - $Part / 2) 48 $Slab $Part ($Depth - $Wall - 48) $FloorHeight | Out-Null
    Add-Box $w "p5" 44 (60 - $Part / 2) $Slab ($Width - $Wall - 44) $Part $FloorHeight | Out-Null
    Add-Box $w "p6" (66 - $Part / 2) 60 $Slab $Part ($Depth - $Wall - 60) $FloorHeight | Out-Null

    # The body picks up a new name from every boolean, so it is found by what it is not rather
    # than by guessing which of Union, Union 2 or Subtract 3 it happens to be this time.
    $parts = @("p1", "p2", "p3", "p4", "p5", "p6")
    Select-Objects $w (@(Body $w $parts) + $parts) | Out-Null
    Invoke-Tool $w "Object" "Merge" | Out-Null
    Wait-Idle $w 300
    Start-Sleep -Seconds 3

    Write-Output "partitions"
}

function Add-Openings($w) {
    # Windows and the front door, through the outside walls.
    Add-Box $w "w1" 12 -1 $SillZ 16 ($Wall + 2) $WindowH | Out-Null
    Add-Box $w "wd" 51 -1 $DoorZ 11 ($Wall + 2) $DoorH | Out-Null
    Add-Box $w "w2" 78 -1 $SillZ 16 ($Wall + 2) $WindowH | Out-Null
    Add-Box $w "w3" 12 ($Depth - $Wall - 1) $SillZ 18 ($Wall + 2) $WindowH | Out-Null
    Add-Box $w "w4" 50 ($Depth - $Wall - 1) $SillZ 10 ($Wall + 2) $WindowH | Out-Null
    Add-Box $w "w5" 80 ($Depth - $Wall - 1) $SillZ 16 ($Wall + 2) $WindowH | Out-Null
    Add-Box $w "w6" -1 14 $SillZ ($Wall + 2) 16 $WindowH | Out-Null
    Add-Box $w "w7" -1 58 $SillZ ($Wall + 2) 16 $WindowH | Out-Null
    Add-Box $w "w8" ($Width - $Wall - 1) 14 $SillZ ($Wall + 2) 16 $WindowH | Out-Null
    Add-Box $w "w9" ($Width - $Wall - 1) 66 $SillZ ($Wall + 2) 16 $WindowH | Out-Null

    # Doorways through the partitions, 78 cm wide.
    Add-Box $w "d1" 46 46.5 $Slab 9 3 $DoorH | Out-Null
    Add-Box $w "d2" 42.5 10 $Slab 3 9 $DoorH | Out-Null
    Add-Box $w "d3" 68.5 10 $Slab 3 9 $DoorH | Out-Null
    Add-Box $w "d4" 42.5 50 $Slab 3 9 $DoorH | Out-Null
    Add-Box $w "d5" 48 58.5 $Slab 9 3 $DoorH | Out-Null
    Add-Box $w "d6" 88 58.5 $Slab 9 3 $DoorH | Out-Null

    # And the hole the stair comes up through, with headroom over the flight.
    Add-Box $w "sw" 52 14 -1 16 27 ($Slab + 2) | Out-Null

    Select-Objects $w @("w1","wd","w2","w3","w4","w5","w6","w7","w8","w9",
                        "d1","d2","d3","d4","d5","d6","sw") | Out-Null
    Invoke-Label $w "Group" | Out-Null
    Start-Sleep -Seconds 1

    Select-Objects $w (@(Body $w @("Group")) + @("Group")) | Out-Null
    Invoke-Tool $w "Object" "Subtract" | Out-Null
    Wait-Idle $w 600
    Start-Sleep -Seconds 4

    Write-Output "openings"
}

function Add-Dowels($w, [double]$topZ) {
    # Sockets underneath, for the basement's dowels to stand in.
    foreach ($i in 0..3) {
        Add-Shape $w "Cylinder" "socket$i" ($Pins[$i][0] - $Width / 2) ($Pins[$i][1] - $Depth / 2) 1.5 `
            3.0 3.0 4.0 | Out-Null
    }
    Select-Objects $w @("socket0", "socket1", "socket2", "socket3") | Out-Null
    Invoke-Label $w "Group" | Out-Null
    Start-Sleep -Seconds 1

    Select-Objects $w (@(Body $w @("Group")) + @("Group")) | Out-Null
    Invoke-Tool $w "Object" "Subtract" | Out-Null
    Wait-Idle $w 300
    Start-Sleep -Seconds 3

    # And pins on top, for the roof.
    foreach ($i in 0..3) {
        Add-Shape $w "Cylinder" "pin$i" ($Pins[$i][0] - $Width / 2) ($Pins[$i][1] - $Depth / 2) ($topZ - 0.2 + 1.6) `
            2.6 2.6 3.2 | Out-Null
    }
    $pins = @("pin0", "pin1", "pin2", "pin3")
    Select-Objects $w (@(Body $w $pins) + $pins) | Out-Null
    Invoke-Tool $w "Object" "Merge" | Out-Null
    Wait-Idle $w 300
    Start-Sleep -Seconds 3

    Write-Output "dowels"
}

# --- The parts, one call each ----------------------------------------------------------------
#
# Each starts on an empty plate and ends with an export. Run them in order, or one at a time
# after a failure - nothing here depends on state left by an earlier call.

$Out = "F:\3DFastCraft\build\house2"

function Build-LivingFloor($w) {
    Clear-Plate $w | Out-Null
    Start-Sleep -Seconds 1

    Add-Shell $w
    Add-Partitions $w
    Add-Openings $w

    # Bricked after the openings are cut, which the retiling could not do until now. Every wall
    # is reached head-on, so nothing is turned round and turned back.
    Add-Brick $w "Front" 4 0.4 0.3
    Add-Brick $w "Back"  4 0.4 0.3
    Add-Brick $w "Left"  4 0.4 0.3
    Add-Brick $w "Right" 4 0.4 0.3

    Add-Dowels $w $WallTop
    Export-Part $w "$Out\living-floor.stl"
}

# --- Part three: the roof ----------------------------------------------------------------------
#
# Two wedges turned a quarter and merged make the gable; a second pair, dropped by the skin
# thickness over the cosine of the pitch, hollows it. Hollow can leave a side open now, but it
# rebuilds the surface on a voxel grid - which rounds a ridge that wants to stay sharp and leaves
# no flat rectangle for the tiles to sit on. For a crisp prism the two-wedge route is still right.

$RoofLength = 116.0
$RoofSpan = 100.0
$RoofRise = 34.0
$RoofSkin = 2.5
$RoofDrop = 3.023      # $RoofSkin / cos(pitch), pitch = atan(34/50)

function Build-Roof($w) {
    Clear-Plate $w | Out-Null
    Start-Sleep -Seconds 1

    Add-Shape $w "Wedge" "outerA" 0 25 ($RoofRise / 2) 50 $RoofLength $RoofRise 90 | Out-Null
    Add-Shape $w "Wedge" "outerB" 0 -25 ($RoofRise / 2) 50 $RoofLength $RoofRise -90 | Out-Null
    Select-Objects $w @("outerA", "outerB") | Out-Null
    Invoke-Tool $w "Object" "Merge" | Out-Null
    Wait-Idle $w 120
    Start-Sleep -Seconds 2

    Add-Shape $w "Wedge" "innerA" 0 25 ($RoofRise / 2 - $RoofDrop) 50 ($RoofLength - 6) $RoofRise 90 | Out-Null
    Add-Shape $w "Wedge" "innerB" 0 -25 ($RoofRise / 2 - $RoofDrop) 50 ($RoofLength - 6) $RoofRise -90 | Out-Null
    Select-Objects $w @("innerA", "innerB") | Out-Null
    Invoke-Tool $w "Object" "Merge" | Out-Null
    Wait-Idle $w 120
    Start-Sleep -Seconds 2

    Select-Objects $w (@(Body $w @("Union 2")) + @("Union 2")) | Out-Null
    Invoke-Tool $w "Object" "Subtract" | Out-Null
    Wait-Idle $w 300
    Start-Sleep -Seconds 3

    # A rim on the underside that nests inside the walls. Not dowels: a 2.5 mm skin has nowhere
    # at the eaves to sink a socket without coming out through the tiles.
    Add-Shape $w "Cube" "rimO" 0 0 0.5 102.2 82.2 7 | Out-Null
    Add-Shape $w "Cube" "rimI" 0 0 0.5 98.2 78.2 9 | Out-Null
    Select-Objects $w @("rimO", "rimI") | Out-Null
    Invoke-Tool $w "Object" "Subtract" | Out-Null
    Wait-Idle $w 120
    Start-Sleep -Seconds 2

    $rim = @(@(Get-ObjectNames $w) | Where-Object { $_ -like "Subtract*" } | Select-Object -Last 1)
    Select-Objects $w (@(Body $w $rim) + $rim) | Out-Null
    Invoke-Tool $w "Object" "Merge" | Out-Null
    Wait-Idle $w 300
    Start-Sleep -Seconds 3

    # 30 x 20 cm pantiles - the proportions are askable for now, where the courses used to be
    # fixed at three times as long as they are tall.
    Add-Roofing $w
    Export-Part $w "$Out\roof.stl"
}

function Add-Roofing($w) {
    foreach ($view in @("Isometric", "Isometric")) {
        $name = @(Get-ObjectNames $w)[0]

        Select-Tab $w "View" | Out-Null
        Invoke-ByName $w $view | Out-Null
        Start-Sleep -Milliseconds 600
        Invoke-ByName $w "Zoom to fit" | Out-Null
        Start-Sleep -Seconds 1

        Select-Objects $w @($name) | Out-Null
        Invoke-Tool $w "Edit" "Engrave..." | Out-Null
        Start-Sleep -Seconds 1

        & "F:\3DFastCraft\tools\ui\click.ps1" -X 560 -Y 520 | Out-Null
        Start-Sleep -Seconds 1

        Set-Number $w "EngraveSize" 3.5
        Set-Number $w "EngraveAspect" 1.5
        Set-Number $w "EngraveWidth" 0.4
        Set-Number $w "EngraveDepth" 0.35

        $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, "EngraveRaised")
        $cb = $w.FindFirst($TS::Descendants, $cond)
        $toggle = $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($toggle.Current.ToggleState -eq "Off") { $toggle.Toggle() }
        Start-Sleep -Milliseconds 500

        Invoke-ByName $w "Engrave" | Out-Null
        Wait-Idle $w 300
        Start-Sleep -Seconds 2

        # Turned half round for the far slope, and turned back afterwards.
        Select-Objects $w @(@(Get-ObjectNames $w)[0]) | Out-Null
        Set-Number $w "RotationZ" 180
        Start-Sleep -Seconds 1
    }
}

# --- Part four: the joinery ---------------------------------------------------------------------

function Build-Joinery($w) {
    Clear-Plate $w | Out-Null
    Start-Sleep -Seconds 1

    # Three window sizes and the front door, each 0.4 mm smaller than its opening all round.
    $frames = @(@("a", -60.0, 15.6, 12.6), @("b", -35.0, 17.6, 12.6), @("c", -10.0, 9.6, 12.6))

    foreach ($f in $frames) {
        Add-Shape $w "Cube" "$($f[0])_o" $f[1] -30 1 $f[2] $f[3] 2 | Out-Null
        Add-Shape $w "Cube" "$($f[0])_i" $f[1] -30 1 ($f[2] - 2.4) ($f[3] - 2.4) 4 | Out-Null
        Select-Objects $w @("$($f[0])_o", "$($f[0])_i") | Out-Null
        Invoke-Tool $w "Object" "Subtract" | Out-Null
        Wait-Idle $w 60
        Start-Sleep -Milliseconds 900
    }

    Add-Shape $w "Cube" "d_l" 15 -30 1 10.6 22.6 2 | Out-Null
    Add-Shape $w "Cube" "d_u" 15 -24.4 1.75 7.4 7.2 1 | Out-Null
    Add-Shape $w "Cube" "d_d" 15 -35.6 1.75 7.4 7.2 1 | Out-Null
    Select-Objects $w @("d_u", "d_d") | Out-Null
    Invoke-Label $w "Group" | Out-Null
    Start-Sleep -Seconds 1
    Select-Objects $w @("d_l", "Group") | Out-Null
    Invoke-Tool $w "Object" "Subtract" | Out-Null
    Wait-Idle $w 60
    Start-Sleep -Seconds 1

    # A glazing bar across each light.
    $bars = @(@("a", -60.0, 13.2), @("b", -35.0, 15.2), @("c", -10.0, 7.2))
    foreach ($b in $bars) {
        Add-Shape $w "Cube" "$($b[0])_bar" $b[1] -30 1 $b[2] 0.7 2 | Out-Null
    }

    $names = @(@(Get-ObjectNames $w) | Where-Object { $_ -like "Subtract*" -or $_ -like "*_bar" })
    Select-Objects $w $names | Out-Null
    Invoke-Tool $w "Object" "Merge" | Out-Null
    Wait-Idle $w 300
    Start-Sleep -Seconds 3

    Export-Part $w "$Out\frames-and-door.stl"

    # And the panes, on their own, for transparent filament.
    Clear-Plate $w | Out-Null
    Add-Shape $w "Cube" "g16" -60 -30 0.4 13.0 10.0 0.8 | Out-Null
    Add-Shape $w "Cube" "g18" -35 -30 0.4 15.0 10.0 0.8 | Out-Null
    Add-Shape $w "Cube" "g10" -10 -30 0.4 7.0 10.0 0.8 | Out-Null
    Export-Part $w "$Out\glazing.stl"
}
