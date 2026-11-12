Add-Type -AssemblyName System.Drawing

function New-RoundRect([single]$x,[single]$y,[single]$w,[single]$h,[single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    if ($d -gt $w) { $d = $w }
    if ($d -gt $h) { $d = $h }
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
    $p.AddArc(($x + $w - $d), ($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc($x, ($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::Transparent)

# 深蓝圆角底板
$outer = New-RoundRect 10 10 236 236 42
$lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Rectangle(0,0,256,256)), [System.Drawing.Color]::FromArgb(255,58,84,120), [System.Drawing.Color]::FromArgb(255,20,30,44), 120.0)
$g.FillPath($lg, $outer)

# 纸卡片（略微旋转）
$g.TranslateTransform(128, 128)
$g.RotateTransform(-14)
$card = New-RoundRect -78 -78 156 156 14
$lg2 = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Rectangle(-78,-78,156,156)), [System.Drawing.Color]::FromArgb(255,255,255,252), [System.Drawing.Color]::FromArgb(255,220,222,218), 90.0)
$g.FillPath($lg2, $card)
$pen1 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255,168,170,166), 3)
$g.DrawPath($pen1, $card)

# 卡片上的迷你图标（方块 + 斜切）
$inner = New-RoundRect -40 -40 80 80 12
$br = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,70,110,168))
$g.FillPath($br, $inner)

# 刀光
$g.ResetTransform()
$g.TranslateTransform(128, 128)
$g.RotateTransform(28)
$glow = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120,150,210,255), 26)
$g.DrawLine($glow, -140, 0, 140, 0)
$core = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255,255,255,255), 9)
$g.DrawLine($core, -140, 0, 140, 0)
$g.ResetTransform()

$bmp.Save('D:\DesktopIconNinja\app.png', [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()

# 打包成 ICO（Vista+ 支持 PNG 负载）
$png = [System.IO.File]::ReadAllBytes('D:\DesktopIconNinja\app.png')
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0)      # reserved
$bw.Write([uint16]1)      # type = icon
$bw.Write([uint16]1)      # count
$bw.Write([byte]0)        # width 256
$bw.Write([byte]0)        # height 256
$bw.Write([byte]0)        # palette
$bw.Write([byte]0)        # reserved
$bw.Write([uint16]1)      # planes
$bw.Write([uint16]32)     # bpp
$bw.Write([uint32]$png.Length)
$bw.Write([uint32]22)     # offset
$bw.Write($png)
$bw.Flush()
[System.IO.File]::WriteAllBytes('D:\DesktopIconNinja\app.ico', $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
"icon written"
