param([int]$X, [int]$Y, [switch]$Drag, [int]$ToX, [int]$ToY)

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class M {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  public struct R { public int L, T, Rt, B; }
  public const uint DOWN = 0x0002, UP = 0x0004;
}
"@ -ErrorAction SilentlyContinue

$p = Get-Process -Name "3DFastCraft" | Select-Object -First 1
[void][M]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Milliseconds 400

$r = New-Object M+R
[void][M]::GetWindowRect($p.MainWindowHandle, [ref]$r)

$sx = $r.L + $X; $sy = $r.T + $Y
[void][M]::SetCursorPos($sx, $sy)
Start-Sleep -Milliseconds 150
[M]::mouse_event([M]::DOWN, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 120

if ($Drag) {
  $ex = $r.L + $ToX; $ey = $r.T + $ToY
  for ($i = 1; $i -le 10; $i++) {
    $ix = [int]($sx + ($ex - $sx) * $i / 10.0)
    $iy = [int]($sy + ($ey - $sy) * $i / 10.0)
    [void][M]::SetCursorPos($ix, $iy)
    Start-Sleep -Milliseconds 45
  }
}

[M]::mouse_event([M]::UP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 350
Write-Output "clicked window($X,$Y) -> screen($sx,$sy)"
