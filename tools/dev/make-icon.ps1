# Draws the logo - Assets/logo.svg - at every icon size and packs them into app.ico.
#
#   tools\dev\make-icon.ps1                       writes src\FastCraft3D\Assets\app.ico
#   tools\dev\make-icon.ps1 -Preview <folder>     and a PNG of each size there, to look at
#
# Drawn here rather than scaled from one picture: below 48 px the five layer lines of the full
# logo run together into a grey smudge, so each size gets as many as it can show - two at 16 and
# 24, three at 32, four at 48, five from 64 up. The small sizes are stored as plain bitmaps, which
# everything that reads icons takes, and 256 as PNG. Needs an STA PowerShell, which Windows
# PowerShell is by default.
param([string]$Ico = "", [string]$Preview = "")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Ico) { $Ico = Join-Path $root 'src\FastCraft3D\Assets\app.ico' }
Add-Type -AssemblyName PresentationCore, WindowsBase

function Brush($hex) { New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($hex)) }

function Face($dc, $brush, $pts) {
    $g = New-Object System.Windows.Media.StreamGeometry
    $ctx = $g.Open()
    $ctx.BeginFigure($pts[0], $true, $true)
    for ($i = 1; $i -lt $pts.Count; $i++) { $ctx.LineTo($pts[$i], $true, $false) }
    $ctx.Close()
    $g.Freeze()
    $dc.DrawGeometry($brush, $null, $g)
}

# Typed, or a negative coordinate arrives as text and multiplying repeats it.
function P([double]$x, [double]$y) { New-Object System.Windows.Point ($script:cx + $x * $script:s), ($script:cy + $y * $script:s) }

function Render([int]$size) {
    # The logo's own units, as in logo.svg: the cube 80 wide and 92 tall about the middle.
    $script:s = $size * 0.94 / 92.0
    $script:cx = $size / 2.0
    $script:cy = $size / 2.0

    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    Face $dc (Brush '#FF6FA8F5') @((P 0 -46), (P 40 -23), (P 0 0), (P -40 -23))
    Face $dc (Brush '#FF2F6FD0') @((P 0 0), (P 40 -23), (P 40 23), (P 0 46))
    Face $dc (Brush '#FFE3EEFC') @((P -40 -23), (P 0 0), (P 0 46), (P -40 23))

    $lines = if ($size -le 24) { 2 } elseif ($size -le 32) { 3 } elseif ($size -le 48) { 4 } else { 5 }
    $thick = [Math]::Max(2.5 * $script:s, 1.0)

    # Stopped short of the face's edges, or the round ends stand out past them.
    $inset = [Math]::Max(4.0, 0.6 * $thick / $script:s + 2.0)
    $pen = New-Object System.Windows.Media.Pen ((Brush '#FF1D4E9E'), $thick)
    $pen.StartLineCap = 'Round'
    $pen.EndLineCap = 'Round'
    for ($k = 1; $k -le $lines; $k++) {
        $d = 46.0 * $k / ($lines + 1)
        $dc.DrawLine($pen, (P (-40 + $inset) (-23 + $d + $inset * 0.575)), (P (-$inset) ($d - $inset * 0.575)))
    }
    $dc.Close()

    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    return New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $bmp, ([System.Windows.Media.PixelFormats]::Bgra32), $null, 0
}

function Png($bitmap) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return ,$ms.ToArray()
}

function Dib($bitmap, [int]$size) {
    $stride = $size * 4
    $pixels = New-Object byte[] ($stride * $size)
    $bitmap.CopyPixels($pixels, $stride, 0)
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($row = $size - 1; $row -ge 0; $row--) { $w.Write($pixels, $row * $stride, $stride) }
    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskStride * $size)))
    $w.Flush()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @()
foreach ($size in $sizes) {
    $bitmap = Render $size
    $data = if ($size -ge 256) { Png $bitmap } else { Dib $bitmap $size }
    $images += ,@($size, $data)
    if ($Preview) { [System.IO.File]::WriteAllBytes((Join-Path $Preview "logo-$size.png"), (Png $bitmap)) }
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $size = $img[0]; $data = $img[1]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$data.Length); $w.Write([int]$offset)
    $offset += $data.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes($Ico, $out.ToArray())
Write-Output "wrote $Ico ($($out.Length) bytes)"
