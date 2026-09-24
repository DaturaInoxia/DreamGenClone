<#
.SYNOPSIS
  B-119 route C1 proof: does a layout reference contracted by Depth / Canny ControlNet hold a
  two-body arrangement that OpenPose re-poses? (SDXL-family, local ComfyUI.)

.DESCRIPTION
  One reference image -> four conditioning variants x N checkpoints, all at the SAME seed and
  settings, so the only variable is the structure mechanism:

      text      no ControlNet          (baseline: what the prompt alone produces)
      openpose  DWPreprocessor -> OpenPoseXL2 ControlNet   (the existing app route)
      depth     DepthAnythingV2 -> depth ControlNet        (B-119 C1, reclining/multi-body)
      canny     CannyEdgePreprocessor -> canny ControlNet   (B-119 C1, edge lock)

  Structure maps are extracted first (T02) and saved so the inputs are inspectable, then each
  render is submitted through helpers/local-comfyui-host/run-local-proof.ps1 (the proven
  upload -> /prompt -> /history -> /view runner with its node-class preflight).

  Every variant writes its own workflow.json + images.json + run-manifest.json for provenance.
  No fallbacks: a variant that fails is reported as failed, never retried with different settings.

.PARAMETER Reference
  Layout reference image (the arrangement to inherit).

.PARAMETER OutDir
  Run output root. Defaults to runs/<yyyyMMdd-HHmmss>-<Label>.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File specs/image-generator-tests/layout-structure-proof/run-c1-structure-proof.ps1
#>
[CmdletBinding()]
param(
    [string]$Reference = 'specs/image-generator-tests/sex-slideshow/runs/20260914-02-slideshow-hq/step14-woman-bends-over.png',
    [string]$OutDir,
    [string]$MapsOutDir = 'artifacts/tmp/b119-c1/maps',
    [string]$ComfyUiUrl = 'http://192.168.0.11:8188',
    [string]$Label = 'c1-structure',
    [int]$Width = 1216,
    [int]$Height = 832,
    [int]$Seed = 20260922,
    [int]$Steps = 30,
    [double]$Cfg = 5.0,
    [string]$Sampler = 'dpmpp_2m_sde',
    [string]$Scheduler = 'karras',
    [string[]]$Checkpoints = @('bigLust_v16.safetensors', 'juggernautXL_ragnarok.safetensors'),
    [double]$OpenPoseStrength = 0.8,
    [double]$DepthStrength = 0.75,
    [double]$CannyStrength = 0.6,
    [string]$Prompt,
    [string]$Negative = '',
    [switch]$SkipMaps,
    [switch]$OnlyMaps,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $repoRoot
$runner = Join-Path $repoRoot 'helpers\local-comfyui-host\run-local-proof.ps1'
if (-not (Test-Path $runner)) { throw "Local proof runner not found: $runner" }
if (-not (Test-Path $Reference)) { throw "Reference image not found: $Reference" }

if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot ("runs\" + (Get-Date -Format 'yyyyMMdd-HHmmss') + "-$Label") }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
New-Item -ItemType Directory -Path $MapsOutDir -Force | Out-Null

# The arrangement is described loosely and CLOTHED: the structure carries the geometry, and a
# structural proof must not be confounded by anatomy or explicitness.
if (-not $Prompt) {
    $Prompt = "photorealistic photograph of a man and a woman in a sunlit modern living room, " +
              "the man standing upright wearing a plain dark grey t-shirt and blue jeans, " +
              "the woman bent forward at the waist wearing a light grey t-shirt and blue jeans, " +
              "wooden floor, large window with warm afternoon light, light grey wall, " +
              "full body head to toe, both fully clothed, natural skin texture, 35mm, sharp focus"
}

$REF_NAME = 'c1ref.png'

function New-ImagesManifest([string]$name, [string]$path, [string]$manifestPath) {
    @(@{ name = $name; path = (Resolve-Path $path).Path }) | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8
}

function Save-Workflow($graph, [string]$path) {
    ($graph | ConvertTo-Json -Depth 30) | Set-Content -Path $path -Encoding UTF8
}

function Invoke-LocalProof([string]$workflowPath, [string]$imagesPath, [string]$outDir, [string]$prefix, [int]$timeoutSec) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    & powershell -NoProfile -ExecutionPolicy Bypass -File $runner `
        -WorkflowPath $workflowPath -ImagesJsonPath $imagesPath -ComfyUiUrl $ComfyUiUrl `
        -OutDir $outDir -Prefix $prefix -TimeoutSec $timeoutSec -Seed $Seed
    if ($LASTEXITCODE -ne 0) { throw "Proof runner failed for '$prefix' (exit $LASTEXITCODE)." }
}

# ------------------------------------------------------------------ graph builders

function New-PreprocessMapGraph([string]$name) {
    $pre = switch ($name) {
        'depth' {
            @{ class_type = 'DepthAnythingV2Preprocessor'; inputs = @{ image = @('2', 0); ckpt_name = 'depth_anything_v2_vitl.pth'; resolution = $Width } }
        }
        'canny' {
            @{ class_type = 'CannyEdgePreprocessor'; inputs = @{ image = @('2', 0); low_threshold = 100; high_threshold = 200; resolution = $Width } }
        }
        'openpose' {
            @{ class_type = 'DWPreprocessor'; inputs = @{ image = @('2', 0); detect_hand = 'disable'; detect_body = 'enable'; detect_face = 'enable'; resolution = $Width; bbox_detector = 'yolox_l.torchscript.pt'; pose_estimator = 'dw-ll_ucoco_384_bs5.torchscript.pt' } }
        }
        default { throw "Unknown structure kind '$name'." }
    }
    return [ordered]@{
        '1'  = @{ class_type = 'LoadImage';       inputs = @{ image = $REF_NAME } }
        '2'  = @{ class_type = 'ImageScale';      inputs = @{ image = @('1', 0); upscale_method = 'bilinear'; width = $Width; height = $Height; crop = 'disabled' } }
        '10' = $pre
        '9'  = @{ class_type = 'SaveImage';       inputs = @{ images = @('10', 0); filename_prefix = "c1-map-$name" } }
    }
}

function New-RenderGraph([string]$checkpoint, [string]$control) {
    $g = [ordered]@{
        '4' = @{ class_type = 'CheckpointLoaderSimple'; inputs = @{ ckpt_name = $checkpoint } }
        '6' = @{ class_type = 'CLIPTextEncode';         inputs = @{ text = $Prompt;   clip = @('4', 1) } }
        '7' = @{ class_type = 'CLIPTextEncode';         inputs = @{ text = $Negative; clip = @('4', 1) } }
        '5' = @{ class_type = 'EmptyLatentImage';       inputs = @{ width = $Width; height = $Height; batch_size = 1 } }
        '9' = @{ class_type = 'SaveImage';              inputs = @{ images = @('8', 0); filename_prefix = "c1-$control" } }
    }

    if ($control -eq 'text') {
        $g['3'] = @{ class_type = 'KSampler'; inputs = @{ seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = 1.0; model = @('4', 0); positive = @('6', 0); negative = @('7', 0); latent_image = @('5', 0) } }
    }
    else {
        $pre = switch ($control) {
            'depth'    { @{ class_type = 'DepthAnythingV2Preprocessor'; inputs = @{ image = @('2', 0); ckpt_name = 'depth_anything_v2_vitl.pth'; resolution = $Width } } }
            'canny'    { @{ class_type = 'CannyEdgePreprocessor';       inputs = @{ image = @('2', 0); low_threshold = 100; high_threshold = 200; resolution = $Width } } }
            'openpose' { @{ class_type = 'DWPreprocessor';              inputs = @{ image = @('2', 0); detect_hand = 'disable'; detect_body = 'enable'; detect_face = 'enable'; resolution = $Width; bbox_detector = 'yolox_l.torchscript.pt'; pose_estimator = 'dw-ll_ucoco_384_bs5.torchscript.pt' } } }
            default { throw "Unknown control kind '$control'." }
        }
        $weightFile = switch ($control) {
            'depth'    { 'controlnet-depth-sdxl-1.0.safetensors' }
            'canny'    { 'controlnet-canny-sdxl-1.0.safetensors' }
            'openpose' { 'thibaud-openpose-xl2\OpenPoseXL2.safetensors' }
        }
        $strength = switch ($control) {
            'depth'    { $DepthStrength }
            'canny'    { $CannyStrength }
            'openpose' { $OpenPoseStrength }
        }
        $g['1']  = @{ class_type = 'LoadImage';                inputs = @{ image = $REF_NAME } }
        $g['2']  = @{ class_type = 'ImageScale';               inputs = @{ image = @('1', 0); upscale_method = 'bilinear'; width = $Width; height = $Height; crop = 'disabled' } }
        $g['10'] = $pre
        $g['11'] = @{ class_type = 'ControlNetLoader';         inputs = @{ control_net_name = $weightFile } }
        $g['12'] = @{ class_type = 'ControlNetApplyAdvanced';  inputs = @{ positive = @('6', 0); negative = @('7', 0); control_net = @('11', 0); image = @('10', 0); strength = $strength; start_percent = 0.0; end_percent = 1.0 } }
        $g['3']  = @{ class_type = 'KSampler';                 inputs = @{ seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = 1.0; model = @('4', 0); positive = @('12', 0); negative = @('12', 1); latent_image = @('5', 0) } }
    }

    $g['8'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('3', 0); vae = @('4', 2) } }
    return $g
}

# ------------------------------------------------------------------ execute

$manifest = [ordered]@{
    harness      = 'run-c1-structure-proof.ps1'
    startedAt    = (Get-Date).ToString('o')
    comfyUiUrl   = $ComfyUiUrl
    reference    = (Resolve-Path $Reference).Path
    canvas       = "$Width x $Height"
    seed         = $Seed
    steps        = $Steps
    cfg          = $Cfg
    sampler      = $Sampler
    scheduler    = $Scheduler
    negative     = $Negative
    prompt       = $Prompt
    strengths    = [ordered]@{ openpose = $OpenPoseStrength; depth = $DepthStrength; canny = $CannyStrength }
    checkpoints  = $Checkpoints
    maps         = @()
    renders      = @()
}

Write-Host ""
Write-Host "== B-119 C1 structure proof ==" -ForegroundColor Cyan
Write-Host "reference : $Reference"
Write-Host "canvas    : $Width x $Height   seed=$Seed  steps=$Steps  cfg=$Cfg  $Sampler/$Scheduler"
Write-Host "out       : $OutDir"
Write-Host "maps      : $MapsOutDir"

# --- T02: structure maps (inspectable inputs)
if (-not $SkipMaps) {
    foreach ($kind in @('depth', 'canny', 'openpose')) {
        Write-Host ""
        Write-Host "== map: $kind ==" -ForegroundColor Cyan
        $wfPath = Join-Path $MapsOutDir "map-$kind.workflow.json"
        $imgPath = Join-Path $MapsOutDir "map-$kind.images.json"
        Save-Workflow (New-PreprocessMapGraph $kind) $wfPath
        New-ImagesManifest $REF_NAME $Reference $imgPath
        if ($DryRun) { Write-Host "  DRY RUN - not submitting" ; continue }
        Invoke-LocalProof -workflowPath $wfPath -imagesPath $imgPath -outDir (Join-Path $MapsOutDir 'out') -prefix "map-$kind" -timeoutSec 600
        $manifest.maps += @{ kind = $kind; workflow = $wfPath; outDir = (Join-Path $MapsOutDir 'out') }
    }
}

# --- renders: every checkpoint x every control, same seed/settings
$ckptKeys = @{}
foreach ($c in $Checkpoints) {
    $key = ($c -replace '\.safetensors$', '')
    $key = ($key -replace 'XL_ragnarok$', '')
    $key = ($key -replace '[^A-Za-z0-9\-]', '')
    $ckptKeys[$c] = $key
}

foreach ($c in $Checkpoints) {
    if ($OnlyMaps) { break }
    foreach ($control in @('text', 'openpose', 'depth', 'canny')) {
        $variant = "$($ckptKeys[$c])-$control"
        Write-Host ""
        Write-Host "== render: $variant ==" -ForegroundColor Cyan
        $variantDir = Join-Path $OutDir $variant
        New-Item -ItemType Directory -Path $variantDir -Force | Out-Null
        $wfPath = Join-Path $variantDir 'workflow.json'
        $imgPath = Join-Path $variantDir 'images.json'
        Save-Workflow (New-RenderGraph $c $control) $wfPath
        New-ImagesManifest $REF_NAME $Reference $imgPath
        $entry = [ordered]@{
            variant    = $variant
            checkpoint = $c
            control    = $control
            strength   = $(switch ($control) {
                'text'     { $null }
                'depth'    { $DepthStrength }
                'canny'    { $CannyStrength }
                'openpose' { $OpenPoseStrength }
            })
            workflow   = $wfPath
            images     = $imgPath
            outDir     = $variantDir
            status     = 'pending'
            outputs    = @()
        }
        if ($DryRun) {
            Write-Host "  DRY RUN - workflow written, not submitting"
            $entry.status = 'dry-run'
        }
        else {
            try {
                Invoke-LocalProof -workflowPath $wfPath -imagesPath $imgPath -outDir $variantDir -prefix $variant -timeoutSec 900
                $outs = @(Get-ChildItem -Path $variantDir -File | Where-Object { $_.Extension -in @('.png', '.jpg', '.webp') } | ForEach-Object { $_.FullName })
                $entry.status = 'ok'
                $entry.outputs = $outs
            }
            catch {
                $entry.status = 'failed'
                $entry.error = $_.Exception.Message
                Write-Host "  FAILED: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
        $manifest.renders += $entry
    }
}

$manifest.finishedAt = (Get-Date).ToString('o')
($manifest | ConvertTo-Json -Depth 20) | Set-Content -Path (Join-Path $OutDir 'run-manifest.json') -Encoding UTF8

Write-Host ""
Write-Host "== summary ==" -ForegroundColor Cyan
foreach ($r in $manifest.renders) {
    Write-Host ("  {0,-26} {1}" -f $r.variant, $r.status)
}
Write-Host ""
Write-Host "manifest: $(Join-Path $OutDir 'run-manifest.json')"
