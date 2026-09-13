<#
.SYNOPSIS
  Builds every base image the anatomy edit matrix needs (gender x posture x framing).

.DESCRIPTION
  Three renders, each cropped into a "close" variant:
    01 male standing   (clothed)  -> 02 crop
    03 female standing (clothed)  -> 04 crop
    05 female lying, legs spread (clothed) -> 06 crop
  The "close" variants are crops of the SAME render, so framing is the only difference — not the subject.

  Existing bases are SKIPPED unless -Force is passed, so re-running this does not invalidate a matrix
  whose results were already produced from the current bases.

  Run from the repo root. PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'http://192.168.0.16:8188',
    [string]$OutDir = 'specs/image-generator-tests/anatomy-edit-matrix/images',
    [long]$Seed = 5501,
    [double]$CropTop = 0.42,
    [double]$CropBottom = 0.88,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$render = Join-Path $here 'make-base-image.ps1'
$crop = Join-Path $here 'crop-band.py'
$python = '.venv/Scripts/python.exe'

if (-not (Test-Path $render)) { throw "Missing $render" }
if (-not (Test-Path $crop)) { throw "Missing $crop" }
if (-not (Test-Path $python)) { throw "Python venv not found at $python (run from the repo root)" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$maleStanding = 'photorealistic full body studio photograph of a 30 year old man standing facing the camera, plain medium gray seamless backdrop, light gray floor, wearing a fitted dark gray short-sleeved t-shirt and medium wash blue jeans, barefoot, arms relaxed at his sides, neutral expression, short hair, soft studio lighting, sharp focus, high detail'
$femaleStanding = 'photorealistic full body studio photograph of a 28 year old woman standing facing the camera, plain medium gray seamless backdrop, light gray floor, wearing a fitted dark gray short-sleeved t-shirt and medium wash blue jeans, barefoot, arms relaxed at her sides, neutral expression, short dark hair, soft studio lighting, sharp focus, high detail'

# Masturbation-scenario base: lying on her back, legs already spread, still fully clothed.
# NOTE: this MUST be rendered landscape (1216x832). A portrait canvas biases the model straight back to a
# standing full-body pose — the first attempt at 832x1216 ignored "lying on her back" entirely and produced
# a woman standing against a wall. "Top down"/"directly above" plus the standing-fighting negative pin it.
# Seed 6601 is the VERIFIED pose (supine, legs spread apart, bare feet toward the bottom of the frame).
# Seed 7701 was rejected: legs not spread and it carried a stock-photo watermark.
$femaleLying = 'photorealistic photograph taken from directly above, looking straight down, of a 28 year old woman lying flat on her back on a white bed, knees bent and lifted, thighs apart, legs spread wide open, wearing a fitted dark gray short-sleeved t-shirt and medium wash blue jeans, barefoot, arms relaxed at her sides, neutral expression, short dark hair, soft studio lighting, sharp focus, high detail'
$negativeFightingStanding = 'standing, standing pose, upright, vertical, full body standing, legs together, knees together, cropped top'

$bases = @(
    @{ Far = 'base-male-far.png'; Close = 'base-male-close.png'; Prompt = $maleStanding; Seed = $Seed; Width = 832; Height = 1216; Negative = ''; CropTop = $CropTop; CropBottom = $CropBottom },
    @{ Far = 'base-female-far.png'; Close = 'base-female-close.png'; Prompt = $femaleStanding; Seed = $Seed; Width = 832; Height = 1216; Negative = ''; CropTop = $CropTop; CropBottom = $CropBottom },
    # Lying: landscape canvas, own seed, pelvis-centred crop (columns narrowed, not just a row band).
    @{ Far = 'base-female-lying-far.png'; Close = 'base-female-lying-close.png'; Prompt = $femaleLying; Seed = 6601; Width = 1216; Height = 832; Negative = $negativeFightingStanding; CropTop = 0.45; CropBottom = 0.80; Left = 0.30; Right = 0.70 }
)

foreach ($base in $bases) {
    $farPath = Join-Path $OutDir $base.Far
    $closePath = Join-Path $OutDir $base.Close

    if ((Test-Path $farPath) -and -not $Force) {
        "SKIP render (exists): $($base.Far)"
    } else {
        $staging = Join-Path $OutDir ('_staging-' + $base.Far)
        if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
        New-Item -ItemType Directory -Path $staging -Force | Out-Null

        '--- rendering ---------------------------------------------------------'
        "target : $($base.Far)"
        "size   : $($base.Width)x$($base.Height)  seed $($base.Seed)"
        "prompt : $($base.Prompt)"
        & $render -ComfyUiUrl $ComfyUiUrl -Positive $base.Prompt -Negative $base.Negative -Seed $base.Seed `
            -Width $base.Width -Height $base.Height -OutDir $staging -Prefix 'anatomy-base'

        $produced = Get-ChildItem $staging -Filter *.png | Select-Object -First 1
        if (-not $produced) { throw "No image produced for $($base.Far)" }
        Copy-Item $produced.FullName $farPath -Force
        Remove-Item $staging -Recurse -Force
    }

    # Re-crop ALWAYS: a crop-bounds change must take effect without forcing a re-render.
    if ($base.ContainsKey('Left')) {
        & $python $crop $farPath $closePath $base.CropTop $base.CropBottom $base.Left $base.Right
    } else {
        & $python $crop $farPath $closePath $base.CropTop $base.CropBottom
    }
}

''
'=== BASE IMAGES ==='
Get-ChildItem $OutDir -Filter 'base-*.png' | Sort-Object Name |
    Select-Object @{n = 'File'; e = { $_.Name }}, @{n = 'KB'; e = { [int]($_.Length / 1KB) }} |
    Format-Table -AutoSize | Out-String -Width 160
