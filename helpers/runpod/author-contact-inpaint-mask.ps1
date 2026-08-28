param(
    [Parameter(Mandatory=$true)][string]$SourcePath,
    [Parameter(Mandatory=$true)][string]$OutputSourcePath,
    [Parameter(Mandatory=$true)][string]$OutputMaskPath,
    [Parameter(Mandatory=$true)][string]$OutputPreviewPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$width = 1024
$height = 1024
$bounds = [System.Drawing.Rectangle]::new(240, 420, 350, 300)
$featherRadius = 12

$source = [System.Drawing.Bitmap]::new((Resolve-Path $SourcePath).Path)
try {
    if ($source.Width -ne $width -or $source.Height -ne $height) {
        throw "Expected 1024x1024 source image, found $($source.Width)x$($source.Height)."
    }

    $outputDirectory = Split-Path -Parent $OutputSourcePath
    if ($outputDirectory -and -not (Test-Path $outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }

    Copy-Item $SourcePath $OutputSourcePath -Force

    $mask = [System.Drawing.Bitmap]::new($width, $height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($mask)
        try {
            $graphics.Clear([System.Drawing.Color]::Black)
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            for ($inset = 0; $inset -le $featherRadius; $inset++) {
                $ratio = $inset / [double]$featherRadius
                $value = [int](255 * $ratio * $ratio * (3 - 2 * $ratio))
                $brush = [System.Drawing.SolidBrush]::new(
                    [System.Drawing.Color]::FromArgb($value, $value, $value)
                )
                try {
                    $ellipse = [System.Drawing.Rectangle]::new(
                        $bounds.X + $inset,
                        $bounds.Y + $inset,
                        $bounds.Width - (2 * $inset),
                        $bounds.Height - (2 * $inset)
                    )
                    $graphics.FillEllipse($brush, $ellipse)
                }
                finally {
                    $brush.Dispose()
                }
            }
        }
        finally {
            $graphics.Dispose()
        }
        $mask.Save($OutputMaskPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $mask.Dispose()
    }

    $preview = [System.Drawing.Bitmap]::new($source)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($preview)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $brush = [System.Drawing.SolidBrush]::new(
                [System.Drawing.Color]::FromArgb(110, 220, 35, 35)
            )
            try {
                $graphics.FillEllipse($brush, $bounds)
            }
            finally {
                $brush.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }
        $preview.Save($OutputPreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $preview.Dispose()
    }
}
finally {
    $source.Dispose()
}