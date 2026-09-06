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
