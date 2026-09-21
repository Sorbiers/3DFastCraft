Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function Get-AppWindow {
  # The title now carries the file name, so match on the process instead of an exact title.
  $p = Get-Process -Name "3DFastCraft" -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $p) { throw "3DFastCraft is not running" }
  $w = $AE::FromHandle($p.MainWindowHandle)
  if (-not $w) { throw "3DFastCraft window not found" }
  return $w
}

function Find-ByName($root, $name) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
  return $root.FindFirst($TS::Descendants, $cond)
}

function Invoke-ByName($root, $name) {
  $e = Find-ByName $root $name
  if (-not $e) { throw "element '$name' not found" }
  $p = $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
  $p.Invoke()
  Start-Sleep -Milliseconds 350
  Write-Output "clicked: $name"
}

function Select-Tab($root, $name) {
  $e = Find-ByName $root $name
  if (-not $e) { throw "tab '$name' not found" }
  $p = $e.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
  $p.Select()
  Start-Sleep -Milliseconds 300
  Write-Output "tab: $name"
}

# Lists every edit box in document order; the properties panel order is
# Name, PosX, PosY, PosZ, SizeX, SizeY, SizeZ, RotX, RotY, RotZ.
function Get-EditBoxes($root) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
  return @($root.FindAll($TS::Descendants, $cond))
}

function Set-EditValue($element, $text) {
  $p = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
  $p.SetValue($text)
  Start-Sleep -Milliseconds 150
}

function Get-StatusText($root) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
  $all = @($root.FindAll($TS::Descendants, $cond))
  return ($all | ForEach-Object { $_.Current.Name } | Where-Object { $_ -and $_.Length -gt 3 })
}

# --- By automation id ----------------------------------------------------------------
# Every box a tool panel or the properties panel owns carries one, and they do not move
# about as rows are shown and hidden - which indexes into Get-EditBoxes do.

function Find-ById($root, $id) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  return $root.FindFirst($TS::Descendants, $cond)
}

function Set-ById($root, $id, $text) {
  $e = Find-ById $root $id
  if (-not $e) { throw "field '$id' not found" }
  $p = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
  $p.SetValue($text)
  Start-Sleep -Milliseconds 120
  Write-Output "$id = $text"
}

function Get-ById($root, $id) {
  $e = Find-ById $root $id
  if (-not $e) { throw "field '$id' not found" }
  return $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Select-ById($root, $id) {
  $e = Find-ById $root $id
  if (-not $e) { throw "'$id' not found" }
  $e.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 250
  Write-Output "chose: $id"
}

function Set-Toggle($root, $id, [bool]$on) {
  $e = Find-ById $root $id
  if (-not $e) { throw "'$id' not found" }
  $p = $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
  $tries = 0
  while (($p.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) -ne $on -and $tries -lt 3) {
    $p.Toggle(); Start-Sleep -Milliseconds 200; $tries++
  }
  Write-Output "$id = $on"
}

# The object list holds one item per object, named as the object is. Matched among the
# list's own items and not by name alone: the ribbon has a button called Cube as well.
function Select-Shape($root, $name) {
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
  $e = @($root.FindAll($TS::Descendants, $cond)) | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1
  if (-not $e) { throw "object '$name' not found" }
  $e.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 250
  Write-Output "selected: $name"
}

# Where an object stands and how big it is, in one go.
function Set-Shape($root, $name, $x, $y, $z, $w, $d, $h) {
  Select-Shape $root $name | Out-Null
  if ($null -ne $w) { Set-ById $root "SizeX" $w | Out-Null }
  if ($null -ne $d) { Set-ById $root "SizeY" $d | Out-Null }
  if ($null -ne $h) { Set-ById $root "SizeZ" $h | Out-Null }
  if ($null -ne $x) { Set-ById $root "PositionX" $x | Out-Null }
  if ($null -ne $y) { Set-ById $root "PositionY" $y | Out-Null }
  if ($null -ne $z) { Set-ById $root "PositionZ" $z | Out-Null }
  Write-Output "$name at ($x, $y, $z) size ($w, $d, $h)"
}

