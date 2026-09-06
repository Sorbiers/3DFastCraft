param([string]$Out = "shot.png", [string]$Dir = ".")

Add-Type -AssemblyName System.Drawing
if (-not ("W" -as [type])) {
  Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  public struct R { public int L, T, Rt, B; }
}
"@
}

$p = Get-Process -Name "3DFastCraft" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "NOT RUNNING"; exit 1 }

[void][W]::ShowWindow($p.MainWindowHandle, 9)
[void][W]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Milliseconds 900

$r = New-Object W+R
[void][W]::GetWindowRect($p.MainWindowHandle, [ref]$r)
$w = $r.Rt - $r.L; $h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)

# The window rect takes in a sliver of whatever is behind it, so the frame is trimmed off.
$crop = New-Object System.Drawing.Rectangle 8, 6, ($w - 16), ($h - 15)
$trimmed = $bmp.Clone($crop, $bmp.PixelFormat)

if (-not (Test-Path $Dir)) { New-Item -ItemType Directory -Force $Dir | Out-Null }
$path = Join-Path (Resolve-Path $Dir) $Out
$trimmed.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)

$g.Dispose(); $bmp.Dispose(); $trimmed.Dispose()
Write-Output "saved $path ($($trimmed.Width)x$($trimmed.Height))"
