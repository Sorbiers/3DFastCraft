param([int]$X = 600, [int]$Y = 500, [int]$Notches = 3, [switch]$In)

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class Wh {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  public struct R { public int L, T, Rt, B; }
}
"@ -ErrorAction SilentlyContinue

$p = Get-Process -Name "3DFastCraft" | Select-Object -First 1
[void][Wh]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Milliseconds 400

$r = New-Object Wh+R
[void][Wh]::GetWindowRect($p.MainWindowHandle, [ref]$r)
[void][Wh]::SetCursorPos(($r.L + $X), ($r.T + $Y))
Start-Sleep -Milliseconds 200

if ($In) { $delta = [uint32]120 } else { $delta = [uint32]4294967176 }

for ($i = 0; $i -lt $Notches; $i++) {
  [Wh]::mouse_event(0x0800, 0, 0, $delta, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 130
}
Write-Output "scrolled $Notches"
