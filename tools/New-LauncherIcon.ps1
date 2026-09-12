<#
.SYNOPSIS
    生成启动器图标 assets\dsh-launcher.ico。

.DESCRIPTION
    使用 System.Drawing 绘制多尺寸图标，并以 32bpp BMP 条目写入 ICO 容器
    （不使用 PNG 条目，保证 csc /win32icon 与 shell 都能读取）。
    产物是构建输入，已随仓库提交；仅在需要修改图标外观时重新运行。

.EXAMPLE
    pwsh -File tools\New-LauncherIcon.ps1
#>
[CmdletBinding()]
param(
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($OutFile)) {
    # $PSScriptRoot 在部分宿主里于 param 默认值求值阶段尚未赋值，改在函数体内解析
    $root = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($root)) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $OutFile = Join-Path (Split-Path -Parent $root) 'assets\dsh-launcher.ico'
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-RoundedPath {
    param([int]$Size, [double]$Inset)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = [Math]::Max(1.0, ($Size * 0.22))
    $x = $Inset
    $y = $Inset
    $w = $Size - (2 * $Inset)
    $h = $Size - (2 * $Inset)
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $g.Clear([System.Drawing.Color]::Transparent)

        $inset = [Math]::Max(0.5, $Size * 0.03)
        $path = New-RoundedPath -Size $Size -Inset $inset
        $rect = New-Object System.Drawing.RectangleF(0, 0, $Size, $Size)
        $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect,
            [System.Drawing.Color]::FromArgb(255, 60, 130, 246),
            [System.Drawing.Color]::FromArgb(255, 23, 55, 145),
            45.0)
        $g.FillPath($grad, $path)
        $grad.Dispose()

        if ($Size -ge 32) {
            # 高光描边，让深色背景下也有轮廓
            $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 255, 255, 255), [Math]::Max(1.0, $Size / 48.0))
            $g.DrawPath($edge, $path)
            $edge.Dispose()
        }

        if ($Size -le 24) {
            # 小尺寸下文字不可读，改用播放三角
            $pts = @(
                (New-Object System.Drawing.PointF(($Size * 0.36), ($Size * 0.26))),
                (New-Object System.Drawing.PointF(($Size * 0.36), ($Size * 0.74))),
                (New-Object System.Drawing.PointF(($Size * 0.76), ($Size * 0.50)))
            )
            $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
            $g.FillPolygon($brush, $pts)
            $brush.Dispose()
        }
        else {
            $text = 'DSH'
            $family = $null
            foreach ($name in @('Segoe UI', 'Arial')) {
                try {
                    $candidate = New-Object System.Drawing.FontFamily($name)
                    $family = $candidate
                    break
                }
                catch { $family = $null }
            }
            if ($null -eq $family) { $family = [System.Drawing.FontFamily]::GenericSansSerif }
            $font = New-Object System.Drawing.Font($family, ($Size * 0.36), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
            $fmt = New-Object System.Drawing.StringFormat
            $fmt.Alignment = [System.Drawing.StringAlignment]::Center
            $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
            $box = New-Object System.Drawing.RectangleF(0, ($Size * 0.02), $Size, $Size)
            $shadow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, 8, 20, 60))
            $g.DrawString($text, $font, $shadow, (New-Object System.Drawing.RectangleF(0, ($Size * 0.06), $Size, $Size)), $fmt)
            $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
            $g.DrawString($text, $font, $white, $box, $fmt)
            $white.Dispose()
            $shadow.Dispose()
            $fmt.Dispose()
            $font.Dispose()
            $family.Dispose()
        }
        $path.Dispose()
    }
    finally {
        $g.Dispose()
    }
    return $bmp
}

function Get-IconEntryBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $size = $Bitmap.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $raw = New-Object byte[] ($stride * $size)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
    }
    finally {
        $Bitmap.UnlockBits($data)
    }

    $maskStride = [int]([Math]::Floor((($size + 31) / 32)) * 4)
    $xorSize = $size * $size * 4
    $andSize = $maskStride * $size

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER：高度写两倍，随后是 XOR 位图与 AND 掩码
    $bw.Write([int]40)
    $bw.Write([int]$size)
    $bw.Write([int]($size * 2))
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int]0)
    $bw.Write([int]($xorSize + $andSize))
    $bw.Write([int]0); $bw.Write([int]0)
    $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        $bw.Write($raw, ($y * $stride), ($size * 4))
    }
    $zeros = New-Object byte[] $andSize
    $bw.Write($zeros, 0, $andSize)
    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose()
    $ms.Dispose()
    return $bytes
}

$entries = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    try {
        $entries += [pscustomobject]@{ Size = $size; Bytes = (Get-IconEntryBytes -Bitmap $bmp) }
    }
    finally {
        $bmp.Dispose()
    }
}

$dir = Split-Path -Parent $OutFile
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)
$writer.Write([int16]0)
$writer.Write([int16]1)
$writer.Write([int16]$entries.Count)
$offset = 6 + (16 * $entries.Count)
foreach ($entry in $entries) {
    $dim = if ($entry.Size -ge 256) { 0 } else { $entry.Size }
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([int16]1)
    $writer.Write([int16]32)
    $writer.Write([int]$entry.Bytes.Length)
    $writer.Write([int]$offset)
    $offset += $entry.Bytes.Length
}
foreach ($entry in $entries) { $writer.Write($entry.Bytes, 0, $entry.Bytes.Length) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
$writer.Dispose()
$out.Dispose()

Write-Host ("生成图标: {0} ({1} 字节, 尺寸: {2})" -f $OutFile, (Get-Item $OutFile).Length, ($sizes -join ','))
