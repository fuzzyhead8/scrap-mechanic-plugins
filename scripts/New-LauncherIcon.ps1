[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$PreviewPath
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot "..\src\ScrapMechanicModManager\Assets\ScrapMechanicModManager.ico"
}
if ([string]::IsNullOrWhiteSpace($PreviewPath)) {
    $PreviewPath = Join-Path $PSScriptRoot "..\src\ScrapMechanicModManager\Assets\ScrapMechanicModManager.png"
}
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-LauncherBitmap {
    $bitmap = [System.Drawing.Bitmap]::new(
        512,
        512,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $backgroundPath = New-RoundedRectanglePath 24 24 464 464 92
    $backgroundBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new(24, 24, 464, 464),
        [System.Drawing.ColorTranslator]::FromHtml("#303941"),
        [System.Drawing.ColorTranslator]::FromHtml("#11161B"),
        55)
    $graphics.FillPath($backgroundBrush, $backgroundPath)
    $borderPen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml("#59636C"),
        12)
    $graphics.DrawPath($borderPen, $backgroundPath)

    $shadowBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.Color]::FromArgb(105, 0, 0, 0))
    $graphics.FillEllipse($shadowBrush, 100, 112, 332, 332)

    $orange = [System.Drawing.ColorTranslator]::FromHtml("#F39A21")
    $orangeBrush = [System.Drawing.SolidBrush]::new($orange)
    for ($index = 0; $index -lt 12; $index++) {
        $state = $graphics.Save()
        $graphics.TranslateTransform(256, 250)
        $graphics.RotateTransform($index * 30)
        $toothPath = New-RoundedRectanglePath -24 -205 48 76 10
        $graphics.FillPath($orangeBrush, $toothPath)
        $toothPath.Dispose()
        $graphics.Restore($state)
    }

    $gearBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new(88, 82, 336, 336),
        [System.Drawing.ColorTranslator]::FromHtml("#FFD15A"),
        [System.Drawing.ColorTranslator]::FromHtml("#E27612"),
        70)
    $graphics.FillEllipse($gearBrush, 88, 82, 336, 336)
    $gearOutline = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml("#8F4309"),
        12)
    $graphics.DrawEllipse($gearOutline, 88, 82, 336, 336)

    $innerBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new(135, 129, 242, 242),
        [System.Drawing.ColorTranslator]::FromHtml("#59636B"),
        [System.Drawing.ColorTranslator]::FromHtml("#252C32"),
        55)
    $graphics.FillEllipse($innerBrush, 135, 129, 242, 242)
    $innerPen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml("#E8EEF1"),
        9)
    $graphics.DrawEllipse($innerPen, 135, 129, 242, 242)

    $facePath = New-RoundedRectanglePath 158 169 196 166 42
    $faceBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new(158, 169, 196, 166),
        [System.Drawing.ColorTranslator]::FromHtml("#333C43"),
        [System.Drawing.ColorTranslator]::FromHtml("#171C21"),
        90)
    $graphics.FillPath($faceBrush, $facePath)
    $facePen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml("#AEB9C0"),
        8)
    $graphics.DrawPath($facePen, $facePath)

    $visorGlowPath = New-RoundedRectanglePath 181 207 150 57 20
    $visorGlowBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.Color]::FromArgb(70, 76, 220, 239))
    $graphics.FillPath($visorGlowBrush, $visorGlowPath)
    $visorPath = New-RoundedRectanglePath 188 214 136 43 14
    $visorBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new(188, 214, 136, 43),
        [System.Drawing.ColorTranslator]::FromHtml("#9AF5FF"),
        [System.Drawing.ColorTranslator]::FromHtml("#27BACE"),
        90)
    $graphics.FillPath($visorBrush, $visorPath)

    $eyeBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.ColorTranslator]::FromHtml("#0B3037"))
    $graphics.FillEllipse($eyeBrush, 215, 224, 18, 22)
    $graphics.FillEllipse($eyeBrush, 279, 224, 18, 22)

    $mouthPen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml("#DDE6EA"),
        11)
    $mouthPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $mouthPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($mouthPen, 218, 294, 294, 294)

    $boltBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.ColorTranslator]::FromHtml("#EEF3F5"))
    foreach ($point in @(@(178, 190), @(326, 190), @(178, 309), @(326, 309))) {
        $graphics.FillEllipse($boltBrush, $point[0] - 7, $point[1] - 7, 14, 14)
    }

    foreach ($resource in @(
        $backgroundPath,
        $backgroundBrush,
        $borderPen,
        $shadowBrush,
        $orangeBrush,
        $gearBrush,
        $gearOutline,
        $innerBrush,
        $innerPen,
        $facePath,
        $faceBrush,
        $facePen,
        $visorGlowPath,
        $visorGlowBrush,
        $visorPath,
        $visorBrush,
        $eyeBrush,
        $mouthPen,
        $boltBrush)) {
        $resource.Dispose()
    }
    $graphics.Dispose()
    return $bitmap
}

function Resize-Bitmap {
    param(
        [System.Drawing.Bitmap]$Source,
        [int]$Size
    )

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($Source, 0, 0, $Size, $Size)
    $graphics.Dispose()
    return $bitmap
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$resolvedPreviewPath = [System.IO.Path]::GetFullPath($PreviewPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutputPath)) | Out-Null
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedPreviewPath)) | Out-Null

$master = New-LauncherBitmap
$master.Save($resolvedPreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $bitmap = Resize-Bitmap $master $size
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    $stream.Dispose()
    $bitmap.Dispose()
}
$master.Dispose()

$fileStream = [System.IO.File]::Create($resolvedOutputPath)
$writer = [System.IO.BinaryWriter]::new($fileStream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$images.Count)
$offset = 6 + (16 * $images.Count)
for ($index = 0; $index -lt $images.Count; $index++) {
    $size = $sizes[$index]
    $dimension = if ($size -eq 256) { 0 } else { $size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$index].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$index].Length
}
foreach ($image in $images) {
    $writer.Write($image)
}
$writer.Dispose()
$fileStream.Dispose()

Write-Host "Icon: $resolvedOutputPath"
Write-Host "Preview: $resolvedPreviewPath"
Write-Host "Sizes: $($sizes -join ', ')"
