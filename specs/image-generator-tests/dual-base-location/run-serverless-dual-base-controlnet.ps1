[CmdletBinding()]
param(
    [string]$EndpointId = 'l2k5hyrxlhrbi0',
    [string]$LocationRef = 'specs/image-generator-tests/dual-base-location/refs/locref-bedroom-locref-bedroom_00001_.png',
    [string]$DeanRefA = 'specs/image-generator-tests/refs/dean/v8/profr.png',
    [string]$BeckyRefA = 'specs/image-generator-tests/refs/becky/v5/profl.png',
    [string]$DeanRefB = 'specs/image-generator-tests/refs/dean/v8/profr.png',
    [string]$BeckyRefB = 'specs/image-generator-tests/refs/becky/v5/profr.png',
    [string]$DeanMask = 'specs/image-generator-tests/identity-two-character/masks/c6_left.png',
    [string]$BeckyMask = 'specs/image-generator-tests/identity-two-character/masks/c6_right.png',
    [string]$OutDir = 'artifacts/tmp/dual-base-location/serverless-full-proof',
    [long]$Seed = 6601
)

$ErrorActionPreference = 'Stop'
$repo = (Get-Location).Path
. (Join-Path $repo 'helpers/runpod/.runpod-env.ps1')
$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
$base = "https://api.runpod.ai/v2/$EndpointId"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Image-Data([string]$path, [string]$name) {
    $full = (Resolve-Path $path).Path
    $bytes = [IO.File]::ReadAllBytes($full)
    @{ name = $name; image = "data:image/png;base64,$([Convert]::ToBase64String($bytes))" }
}

$images = @(
    (Image-Data $LocationRef 'location.png'),
    (Image-Data $DeanRefA 'dean-a.png'),
    (Image-Data $BeckyRefA 'becky-a.png'),
    (Image-Data $DeanRefB 'dean-b.png'),
    (Image-Data $BeckyRefB 'becky-b.png'),
    (Image-Data $DeanMask 'dean-mask.png'),
    (Image-Data $BeckyMask 'becky-mask.png')
)

function Workflow([string]$variant) {
    $dean = if ($variant -eq 'A') { 'dean-a.png' } else { 'dean-b.png' }
    $becky = if ($variant -eq 'A') { 'becky-a.png' } else { 'becky-b.png' }
    $prompt = if ($variant -eq 'A') {
        'photorealistic wide interior photograph of a bedroom, exactly two people, a man on the left and a woman on the right, standing beside the bed facing one another in side profile, the bed, window, curtains, walls and nightstands remain visible, warm cinematic natural light, 35mm photography'
    } else {
        'photorealistic wide interior photograph of the same bedroom, exactly two people standing side by side near the bed with clear space between them, a man on the left and a woman on the right, both facing the same direction toward the right in side profile, not touching, the bed, window, curtains, walls and nightstands remain visible, warm cinematic natural light, 35mm photography'
    }
    [ordered]@{
        '4'  = @{ class_type = 'CheckpointLoaderSimple'; inputs = @{ ckpt_name = 'juggernautXL_ragnarok.safetensors' } }
        '5'  = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = 1216; height = 832; batch_size = 1 } }
        '6'  = @{ class_type = 'CLIPTextEncode'; inputs = @{ text = $prompt; clip = @('4', 1) } }
        '7'  = @{ class_type = 'CLIPTextEncode'; inputs = @{ text = 'deformed, bad anatomy, extra limbs, text, watermark, low quality'; clip = @('4', 1) } }
        '10' = @{ class_type = 'IPAdapterUnifiedLoader'; inputs = @{ model = @('4', 0); preset = 'PLUS FACE (portraits)' } }
        '11' = @{ class_type = 'LoadImage'; inputs = @{ image = 'location.png' } }
        '12' = @{ class_type = 'CannyEdgePreprocessor'; inputs = @{ image = @('11', 0); low_threshold = 0.4; high_threshold = 0.8; resolution = 832 } }
        '13' = @{ class_type = 'ControlNetLoader'; inputs = @{ control_net_name = 'controlnet-canny-sdxl-1.0.safetensors' } }
        '14' = @{ class_type = 'ControlNetApplyAdvanced'; inputs = @{ positive = @('6', 0); negative = @('7', 0); control_net = @('13', 0); image = @('12', 0); strength = 0.55; start_percent = 0.0; end_percent = 1.0 } }
        '15' = @{ class_type = 'DepthAnythingV2Preprocessor'; inputs = @{ image = @('11', 0); resolution = 832 } }
        '16' = @{ class_type = 'ControlNetLoader'; inputs = @{ control_net_name = 'controlnet-depth-sdxl-1.0.safetensors' } }
        '17' = @{ class_type = 'ControlNetApplyAdvanced'; inputs = @{ positive = @('14', 0); negative = @('14', 1); control_net = @('16', 0); image = @('15', 0); strength = 0.55; start_percent = 0.0; end_percent = 1.0 } }
        '18' = @{ class_type = 'LoadImage'; inputs = @{ image = $dean } }
        '19' = @{ class_type = 'LoadImage'; inputs = @{ image = $becky } }
        '20' = @{ class_type = 'LoadImageMask'; inputs = @{ image = 'dean-mask.png'; channel = 'red' } }
        '21' = @{ class_type = 'LoadImageMask'; inputs = @{ image = 'becky-mask.png'; channel = 'red' } }
        '22' = @{ class_type = 'IPAdapter'; inputs = @{ model = @('10', 0); ipadapter = @('10', 1); image = @('18', 0); weight = 0.8; weight_type = 'standard'; start_at = 0.0; end_at = 1.0; attn_mask = @('20', 0) } }
        '23' = @{ class_type = 'IPAdapter'; inputs = @{ model = @('22', 0); ipadapter = @('10', 1); image = @('19', 0); weight = 0.6; weight_type = 'standard'; start_at = 0.0; end_at = 1.0; attn_mask = @('21', 0) } }
        '3'  = @{ class_type = 'KSampler'; inputs = @{ seed = $Seed; steps = 30; cfg = 5.0; sampler_name = 'dpmpp_2m_sde'; scheduler = 'karras'; denoise = 1.0; model = @('23', 0); positive = @('17', 0); negative = @('17', 1); latent_image = @('5', 0) } }
        '8'  = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('3', 0); vae = @('4', 2) } }
        '9'  = @{ class_type = 'SaveImage'; inputs = @{ images = @('8', 0); filename_prefix = "dual-$variant" } }
    }
}

function Submit([string]$variant) {
    $body = @{ input = @{ workflow = (Workflow $variant); images = $images } } | ConvertTo-Json -Depth 30 -Compress
    Write-Host "Submitting Base $variant ($([Text.Encoding]::UTF8.GetByteCount($body)) bytes)..."
    $response = Invoke-RestMethod -Uri "$base/run" -Method Post -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 600
    if (-not $response.id) { throw "Base $variant returned no job ID: $($response | ConvertTo-Json -Depth 8)" }
    Write-Host "Base $variant job: $($response.id)"
    return $response.id
}

function Wait-Job([string]$variant, [string]$jobId) {
    for ($i = 1; $i -le 60; $i++) {
        Start-Sleep -Seconds 10
        $status = Invoke-RestMethod -Uri "$base/status/$jobId" -Headers $headers -TimeoutSec 60
        Write-Host "Base ${variant} poll ${i}: $($status.status)"
        if ($status.status -in @('COMPLETED','FAILED','CANCELLED','TIMED_OUT')) {
            $status | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $OutDir "$variant-status.json")
            if ($status.status -ne 'COMPLETED') { throw "Base $variant failed: $($status.error)" }
            $raw = $status.output.images[0].data
            if ($raw -match '^data:') { $raw = ($raw -split ',', 2)[1] }
            [IO.File]::WriteAllBytes((Join-Path $OutDir "base-$variant.png"), [Convert]::FromBase64String($raw))
            Write-Host "Saved base-$variant.png"
            return
        }
    }
    throw "Base $variant did not complete within 10 minutes. Job ID: $jobId"
}

$jobA = Submit 'A'
Wait-Job 'A' $jobA
$jobB = Submit 'B'
Wait-Job 'B' $jobB
Write-Host "FULL PROOF COMPLETE: $OutDir"
