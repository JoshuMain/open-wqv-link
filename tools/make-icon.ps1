# Draws the Open WQV Link app icon (a stylised wrist-camera watch) and packs it into an .ico.
#
# Usage (from the repository root):
#   powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1 -OutDir src/WqvLink.App/Assets
#
# Writes app.ico (256, 128, 64, 48, 32 and 16 px), app.png (256 px) and preview.png (all sizes, with
# 32 and 16 enlarged). The 48 px and larger sizes use the detailed drawing in DrawDetailed; 32 and 16 px
# are hand-placed pixels in DrawSmall, because shrinking the big one makes them blurry.
# Windows only (uses System.Drawing). Delete preview.png afterwards if you don't want it committed.
param([string]$OutDir)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null

function C([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = [Math]::Min($r, [Math]::Min($w, $h) / 2)
    if ($r -le 0.5) { $p.AddRectangle((New-Object System.Drawing.RectangleF $x, $y, $w, $h)); return $p }
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Fill($g, $path, $brush) { $g.FillPath($brush, $path) }

function Grad([float]$x, [float]$y, [float]$w, [float]$h, [string]$a, [string]$b, [float]$angle = 90) {
    New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF $x, $y, $w, $h), (C $a), (C $b), $angle
}

# Seven-segment digit. Segments: a top, b top-right, c bottom-right, d bottom, e bottom-left, f top-left, g middle.
$SEG = @{ '0'='abcdef'; '1'='bc'; '2'='abged'; '3'='abgcd'; '4'='fgbc'; '5'='afgcd'; '6'='afgedc'; '7'='abc'; '8'='abcdefg'; '9'='abcdfg' }
function Digit($g, $brush, [string]$ch, [float]$x, [float]$y, [float]$w, [float]$h, [float]$t) {
    $m = $h / 2
    $rects = @{
        a = @(($x + $t * 0.6), $y, ($w - 1.2 * $t), $t)
        b = @(($x + $w - $t), ($y + $t * 0.6), $t, ($m - $t * 0.9))
        c = @(($x + $w - $t), ($y + $m + $t * 0.3), $t, ($m - $t * 0.9))
        d = @(($x + $t * 0.6), ($y + $h - $t), ($w - 1.2 * $t), $t)
        e = @($x, ($y + $m + $t * 0.3), $t, ($m - $t * 0.9))
        f = @($x, ($y + $t * 0.6), $t, ($m - $t * 0.9))
        g = @(($x + $t * 0.6), ($y + $m - $t / 2), ($w - 1.2 * $t), $t)
    }
    foreach ($s in $SEG[$ch].ToCharArray()) {
        $r = $rects["$s"]
        $g.FillRectangle($brush, [float]$r[0], [float]$r[1], [float]$r[2], [float]$r[3])
    }
}

# Detailed design, drawn on a 256-unit canvas and scaled to $size.
function DrawDetailed([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    $outline = New-Object System.Drawing.Pen (C '#3a3f45'), 4

    # Bracelet stubs (behind the case), with link lines.
    foreach ($y in 0, 206) {
        $p = RoundRect 70 $y 116 50 6
        Fill $g $p (Grad 70 $y 116 50 '#c9ced3' '#8d949b' 0)
        $g.DrawPath($outline, $p)
        $linkPen = New-Object System.Drawing.Pen (C '#7b828a'), 3
        foreach ($ly in 12, 25, 38) { $g.DrawLine($linkPen, 74, $y + $ly, 182, $y + $ly) }
    }

    # Side buttons.
    foreach ($by in 88, 150) {
        foreach ($bx in 14, 226) {
            $p = RoundRect $bx $by 16 30 4
            Fill $g $p (Grad $bx $by 16 30 '#b7bdc3' '#7d848b')
            $g.DrawPath((New-Object System.Drawing.Pen (C '#3a3f45'), 3), $p)
        }
    }

    # Case: brushed silver.
    $case = RoundRect 24 22 208 212 38
    Fill $g $case (Grad 24 22 208 212 '#f1f3f5' '#9aa1a8' 60)
    $g.DrawPath($outline, $case)
    # Soft highlight along the top.
    $hl = RoundRect 36 30 184 60 30
    Fill $g $hl (Grad 36 30 184 60 '#ffffff' '#e9ecef' 90)

    # Camera plate on top, with the up-pointing triangle.
    $plate = RoundRect 70 34 116 38 12
    Fill $g $plate (Grad 70 34 116 38 '#e3e6e9' '#b3b9bf')
    $g.DrawPath((New-Object System.Drawing.Pen (C '#6c737a'), 2.5), $plate)
    $tri = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tri.AddPolygon([System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 128, 42), (New-Object System.Drawing.PointF 138, 58), (New-Object System.Drawing.PointF 118, 58)))
    $g.FillPath((New-Object System.Drawing.SolidBrush (C '#2f343a')), $tri)

    # Screen bezel and LCD.
    $bezel = RoundRect 46 80 164 118 16
    Fill $g $bezel (Grad 46 80 164 118 '#3b4046' '#1f2327')
    $lcd = RoundRect 58 90 140 98 8
    Fill $g $lcd (Grad 58 90 140 98 '#d3dccb' '#a9b5a1')
    $ink = New-Object System.Drawing.SolidBrush (C '#252a26')

    # Small top row (the day/date line) as short bars.
    foreach ($bx in 70, 84, 98) { $g.FillRectangle($ink, [float]$bx, 100, 10, 5) }
    foreach ($bx in 164, 176) { $g.FillRectangle($ink, [float]$bx, 100, 9, 5) }

    # "10:58" in seven-segment digits.
    $dw = 24; $dh = 48; $t = 6; $y = 124
    Digit $g $ink '1' 66 $y $dw $dh $t
    Digit $g $ink '0' 94 $y $dw $dh $t
    $g.FillRectangle($ink, 124, $y + 12, 6, 6); $g.FillRectangle($ink, 124, $y + 30, 6, 6)
    Digit $g $ink '5' 136 $y $dw $dh $t
    Digit $g $ink '8' 164 $y $dw $dh $t

    # Bottom button.
    $btn = RoundRect 100 204 56 16 7
    Fill $g $btn (Grad 100 204 56 16 '#f4f6f8' '#a4abb2')
    $g.DrawPath((New-Object System.Drawing.Pen (C '#5a6168'), 2.5), $btn)

    $g.Dispose()
    return $bmp
}

# Pixel-snapped simplified design for 16 and 32 px (no anti-aliasing, so edges stay crisp).
function DrawSmall([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'None'; $g.PixelOffsetMode = 'Half'
    function Px($c) { New-Object System.Drawing.SolidBrush (C $c) }
    if ($size -eq 32) {
        $g.FillRectangle((Px '#9aa1a8'), 10, 0, 12, 3)     # bracelet top
        $g.FillRectangle((Px '#9aa1a8'), 10, 29, 12, 3)    # bracelet bottom
        $g.FillRectangle((Px '#3a3f45'), 3, 2, 26, 28)     # case outline
        $g.FillRectangle((Px '#d7dbdf'), 4, 3, 24, 26)     # case
        $g.FillRectangle((Px '#f3f5f7'), 4, 3, 24, 4)      # top highlight
        $g.FillRectangle((Px '#3a3f45'), 3, 2, 1, 1); $g.FillRectangle((Px '#3a3f45'), 28, 2, 1, 1) # corner trims
        $g.FillRectangle((Px '#2f343a'), 15, 4, 2, 1); $g.FillRectangle((Px '#2f343a'), 14, 5, 4, 1) # triangle
        $g.FillRectangle((Px '#262a2e'), 6, 9, 20, 15)     # bezel
        $g.FillRectangle((Px '#bfc9b7'), 7, 10, 18, 13)    # LCD
        $ink = Px '#252a26'
        $g.FillRectangle($ink, 9, 11, 3, 1); $g.FillRectangle($ink, 19, 11, 3, 1)   # date row
        # "10:58" as tiny block digits
        $g.FillRectangle($ink, 9, 14, 1, 7)                                          # 1
        $g.FillRectangle($ink, 11, 14, 3, 1); $g.FillRectangle($ink, 11, 20, 3, 1); $g.FillRectangle($ink, 11, 14, 1, 7); $g.FillRectangle($ink, 13, 14, 1, 7)  # 0
        $g.FillRectangle($ink, 15, 16, 1, 1); $g.FillRectangle($ink, 15, 18, 1, 1)   # :
        $g.FillRectangle($ink, 17, 14, 3, 1); $g.FillRectangle($ink, 17, 14, 1, 4); $g.FillRectangle($ink, 17, 17, 3, 1); $g.FillRectangle($ink, 19, 17, 1, 4); $g.FillRectangle($ink, 17, 20, 3, 1) # 5
        $g.FillRectangle($ink, 21, 14, 3, 7); $g.FillRectangle((Px '#bfc9b7'), 22, 15, 1, 2); $g.FillRectangle((Px '#bfc9b7'), 22, 18, 1, 2) # 8
        $g.FillRectangle((Px '#8d949b'), 13, 26, 6, 2)     # bottom button
    } else {
        $g.FillRectangle((Px '#9aa1a8'), 5, 0, 6, 1)
        $g.FillRectangle((Px '#9aa1a8'), 5, 15, 6, 1)
        $g.FillRectangle((Px '#3a3f45'), 1, 1, 14, 14)
        $g.FillRectangle((Px '#d7dbdf'), 2, 2, 12, 12)
        $g.FillRectangle((Px '#2f343a'), 7, 2, 2, 1)       # triangle hint
        $g.FillRectangle((Px '#262a2e'), 3, 4, 10, 8)      # bezel
        $g.FillRectangle((Px '#bfc9b7'), 4, 5, 8, 6)       # LCD
        $ink = Px '#252a26'
        $g.FillRectangle($ink, 5, 7, 2, 3); $g.FillRectangle($ink, 9, 7, 2, 3)    # time blocks
        $g.FillRectangle((Px '#8d949b'), 6, 13, 4, 1)      # bottom button
    }
    $g.Dispose()
    return $bmp
}

$sizes = 256, 128, 64, 48, 32, 16
$pngs = @{}
foreach ($s in $sizes) {
    $bmp = if ($s -ge 48) { DrawDetailed $s } else { DrawSmall $s }
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$s] = $ms.ToArray()
    if ($s -eq 256) { $bmp.Save((Join-Path $OutDir 'app.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# Pack into .ico: header, one 16-byte directory entry per size, then the PNG data for each.
$ico = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$s].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$s].Length
}
foreach ($s in $sizes) { $w.Write($pngs[$s]) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $OutDir 'app.ico'), $ico.ToArray())
"wrote app.ico ({0:N0} bytes, sizes {1}) and app.png" -f $ico.Length, ($sizes -join ', ')

# Preview sheet: every size at 1:1, then 32 and 16 enlarged x8 to check the pixels.
$sheet = New-Object System.Drawing.Bitmap 1080, 280
$g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear((C '#e8e8e8')); $g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'
$x = 10
foreach ($s in $sizes) {
    $img = [System.Drawing.Image]::FromStream((New-Object IO.MemoryStream (,$pngs[$s])))
    $g.DrawImage($img, $x, 10, $s, $s); $x += $s + 10
}
$img = [System.Drawing.Image]::FromStream((New-Object IO.MemoryStream (,$pngs[32]))); $g.DrawImage($img, 650, 10, 256, 256)
$img = [System.Drawing.Image]::FromStream((New-Object IO.MemoryStream (,$pngs[16]))); $g.DrawImage($img, 930, 10, 128, 128)
$g.Dispose(); $sheet.Save((Join-Path $OutDir 'preview.png'))

