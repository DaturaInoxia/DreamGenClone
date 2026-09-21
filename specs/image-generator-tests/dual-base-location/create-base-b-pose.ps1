param(
    [string]$Source = 'specs/image-generator-tests/dual-base-location/proofs/dwpose-step02-facing-each-other/pose-dwpose-serverless-comfy_0.png',
    [string]$Output = 'specs/image-generator-tests/dual-base-location/refs/pose-base-b-both-facing-right.png'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path $Source).Path
$outputPath = Join-Path (Get-Location) $Output
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null

$sourceImage = [Drawing.Bitmap]::new($sourcePath)
$outputImage = [Drawing.Bitmap]::new($sourceImage)
$graphics = [Drawing.Graphics]::FromImage($outputImage)

try {
    # The current successful Base A pose is a 768x512 DWPose canvas. The right
    # figure occupies this region; mirror only that figure around its center so
    # the left figure remains unchanged and both figures face right.
    $rightFigure = [Drawing.Rectangle]::new(392, 48, 92, 432)
    $scratch = [Drawing.Bitmap]::new($rightFigure.Width, $rightFigure.Height)
    $scratchGraphics = [Drawing.Graphics]::FromImage($scratch)
    try {
        $scratchGraphics.DrawImage($sourceImage,
            [Drawing.Rectangle]::new(0, 0, $scratch.Width, $scratch.Height),
            $rightFigure,
            [Drawing.GraphicsUnit]::Pixel)
        $scratch.RotateFlip([Drawing.RotateFlipType]::RotateNoneFlipX)
        $graphics.DrawImage($scratch, $rightFigure.Location)
    }
    finally {
        $scratchGraphics.Dispose()
        $scratch.Dispose()
    }

    $outputImage.Save($outputPath, [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $outputImage.Dispose()
    $sourceImage.Dispose()
}

Write-Output "Created: $outputPath"
Write-Output "Size: 768x512"
Write-Output "Pose: left figure unchanged; right figure mirrored to face right"
