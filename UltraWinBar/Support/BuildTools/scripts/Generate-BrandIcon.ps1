param([string]$PreviewPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$resources = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '../../..')) 'Assets\Resources\Brand'
[xml]$svg = Get-Content -LiteralPath (Join-Path $resources 'ultrawinbar.svg') -Raw
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    try {
        $drawing.PushTransform((New-Object System.Windows.Media.ScaleTransform(($size / 256.0), ($size / 256.0))))
        foreach ($rect in $svg.svg.rect) {
            $brush = [System.Windows.Media.BrushConverter]::new().ConvertFromString($rect.fill)
            $bounds = New-Object System.Windows.Rect([double]$rect.x, [double]$rect.y, [double]$rect.width, [double]$rect.height)
            $drawing.DrawRoundedRectangle($brush, $null, $bounds, [double]$rect.rx, [double]$rect.rx)
        }
        $drawing.Pop()
    } finally { $drawing.Close() }
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    try {
        $encoder.Save($stream)
        $frames += ,$stream.ToArray()
        if ($size -eq 256 -and $PreviewPath) { [System.IO.File]::WriteAllBytes($PreviewPath, $stream.ToArray()) }
    } finally { $stream.Dispose() }
}
$output = [System.IO.File]::Create((Join-Path $resources 'ultrawinbar.ico'))
$writer = New-Object System.IO.BinaryWriter($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $output.Dispose() }
