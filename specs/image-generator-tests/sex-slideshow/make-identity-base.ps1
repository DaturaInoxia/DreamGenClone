<#
.SYNOPSIS
  Renders an identity-conditioned base image for the sex-slideshow harness using regional IP-Adapter.

.DESCRIPTION
  Step 1 base render with Dean (left) + Becky (right) multiangle refs via regional IP-Adapter PLUS FACE.
  Uses the same System.Net.Http pattern as run-local-aio-edit-proof.ps1 so auth/headers work correctly.

  Auto-resolves refs from specs/image-generator-tests/refs/versions.json when no explicit refs given.
  Defaults to profile views matching the "facing each other" base pose:
    - Dean (left) → profr (his right profile)
    - Becky (right) → profl (her left profile)

Usage (called from run-sex-slideshow.ps1):
  & specs/image-generator-tests/sex-slideshow/make-identity-base.ps1 `
      -ComfyUiUrl 'https://comfy.kenacwood.net' `
      -Prompt "..." -Seed 6601 -Width 1216 -Height 832 `
      -Checkpoint bigLust_v16.safetensors -OutDir <staging> -Prefix slideXXX
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'http://192.168.0.16:8188',
    # Explicit ref paths (optional). When omitted, auto-resolve from versions.json.
    [string]$DeanRef,
    [string]$BeckyRef,
    [string]$DeanMask = 'specs/image-generator-tests/identity-two-character/masks/c6_left.png',
    [string]$BeckyMask = 'specs/image-generator-tests/identity-two-character/masks/c6_right.png',
    [Parameter(Mandatory = $true)][string]$Prompt,
    [long]$Seed = 6601,
    [int]$Width = 1216,
    [int]$Height = 832,
    [string]$Checkpoint = 'bigLust_v16.safetensors',
    [int]$Steps = 30,
    [double]$Cfg = 5.0,
    [string]$Sampler = 'dpmpp_2m_sde',
    [string]$Scheduler = 'karras',
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Prefix = 'slide'
)

$ErrorActionPreference = 'Stop'
$base = $ComfyUiUrl.TrimEnd('/')

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(600)

# --- resolve refs -----------------------------------------------------------------------
if (-not $DeanRef) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    # sex-slideshow → image-generator-tests → refs
    $harnessDir = Split-Path -Parent $scriptDir
    $versionsFile = Join-Path (Join-Path $harnessDir "refs") "versions.json"
    $raw = Get-Content $versionsFile -Raw
    $jsonObj = $raw | ConvertFrom-Json
    $deanVer = $jsonObj.'dean'
    $candidate = Join-Path (Join-Path (Join-Path $harnessDir "refs") "dean") "$deanVer\profr.png"
    if (-not (Test-Path $candidate)) { $candidate = Join-Path (Join-Path (Join-Path $harnessDir "refs") "dean") "$deanVer\front.png" }
    if (-not (Test-Path $candidate)) { throw "Could not resolve Dean ref from $versionsFile" }
    $DeanRef = $candidate
}

if (-not $BeckyRef) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $harnessDir = Split-Path -Parent $scriptDir
    $versionsFile = Join-Path (Join-Path $harnessDir "refs") "versions.json"
    $raw = Get-Content $versionsFile -Raw
    $jsonObj = $raw | ConvertFrom-Json
    $beckyVer = $jsonObj.'becky'
    $candidate = Join-Path (Join-Path (Join-Path $harnessDir "refs") "becky") "$beckyVer\profl.png"
    if (-not (Test-Path $candidate)) { $candidate = Join-Path (Join-Path (Join-Path $harnessDir "refs") "becky") "$beckyVer\front.png" }
    if (-not (Test-Path $candidate)) { throw "Could not resolve Becky ref from $versionsFile" }
    $BeckyRef = $candidate
}

# Verify mask files exist
if (-not (Test-Path $DeanMask)) { throw "Dean mask not found: $DeanMask" }
if (-not (Test-Path $BeckyMask)) { throw "Becky mask not found: $BeckyMask" }

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# --- helper: upload image to ComfyUI ----------------------------------------------------
function Upload-Image($url, $filePath, $uploadName) {
    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $filePath))
    $multipart = New-Object System.Net.Http.MultipartFormDataContent
    $imageContent = New-Object System.Net.Http.ByteArrayContent (,$bytes)
    $imageContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('image/png')
    $multipart.Add($imageContent, 'image', $uploadName)
    $multipart.Add((New-Object System.Net.Http.StringContent 'true'), 'overwrite')
    $resp = $client.PostAsync("$url/upload/image", $multipart).Result
    if (-not $resp.IsSuccessStatusCode) {
        throw "Upload failed: $($resp.StatusCode) $($resp.Content.ReadAsStringAsync().Result)"
    }
    return ($resp.Content.ReadAsStringAsync().Result | ConvertFrom-Json).name
}

# --- upload all assets ------------------------------------------------------------------
$deanName = Upload-Image $base $DeanRef "dean_ref.png"
$beckyName = Upload-Image $base $BeckyRef "becky_ref.png"
$deanMaskName = Upload-Image $base $DeanMask "dean_mask.png"
$beckyMaskName = Upload-Image $base $BeckyMask "becky_mask.png"

"uploaded dean ref : $deanName"
"uploaded becky ref: $beckyName"
"uploaded dean mask: $deanMaskName"
"uploaded becky mask: $beckyMaskName"

# --- build workflow (regional IP-Adapter, pattern from identity-two-character c6) -------
$workflow = @{
    '4'  = @{ class_type = 'CheckpointLoaderSimple'; inputs = @{ ckpt_name = $Checkpoint } }
    '5'  = @{ class_type = 'EmptyLatentImage';       inputs = @{ width = $Width; height = $Height; batch_size = 1 } }
    '6'  = @{ class_type = 'CLIPTextEncode';         inputs = @{ text = $Prompt; clip = @('4', 1) } }
    '7'  = @{ class_type = 'CLIPTextEncode';         inputs = @{ text = ''; clip = @('4', 1) } }
    '8'  = @{ class_type = 'VAEDecode';              inputs = @{ samples = @('3', 0); vae = @('4', 2) } }
    '9'  = @{ class_type = 'SaveImage';              inputs = @{ images = @('8', 0); filename_prefix = $Prefix } }
    '10' = @{ class_type = 'IPAdapterUnifiedLoader'; inputs = @{ model = @('4', 0); preset = 'PLUS FACE (portraits)' } }
    '11' = @{ class_type = 'LoadImage';              inputs = @{ image = $deanName } }
    '12' = @{ class_type = 'LoadImage';              inputs = @{ image = $beckyName } }
    '13' = @{ class_type = 'LoadImageMask';          inputs = @{ image = $deanMaskName; channel = 'red' } }
    '14' = @{ class_type = 'LoadImageMask';          inputs = @{ image = $beckyMaskName; channel = 'red' } }
    '20' = @{ class_type = 'IPAdapter'; inputs = @{
        model = @('10', 0); ipadapter = @('10', 1); image = @('11', 0)
        weight = 0.8; weight_type = 'standard'; start_at = 0.0; end_at = 1.0; attn_mask = @('13', 0)
    }}
    '21' = @{ class_type = 'IPAdapter'; inputs = @{
        model = @('20', 0); ipadapter = @('10', 1); image = @('12', 0)
        weight = 0.6; weight_type = 'standard'; start_at = 0.0; end_at = 1.0; attn_mask = @('14', 0)
    }}
    '3'  = @{ class_type = 'KSampler'; inputs = @{
        seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler
        denoise = 1.0; model = @('21', 0); positive = @('6', 0); negative = @('7', 0); latent_image = @('5', 0)
    }}
}

$payload = @{ prompt = $workflow; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 12
$content = New-Object System.Net.Http.StringContent $payload
$content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/json')
$queued = ($client.PostAsync("$base/prompt", $content).Result.Content.ReadAsStringAsync().Result | ConvertFrom-Json)

if ($queued.error) { throw "ComfyUI rejected the prompt: $($queued.error | ConvertTo-Json -Depth 6)" }
$promptId = $queued.prompt_id
"queued: $promptId"

# --- poll for completion ----------------------------------------------------------------
$deadline = (Get-Date).AddSeconds(900)
$entry = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $history = ($client.GetStringAsync("$base/history/$promptId").Result | ConvertFrom-Json)
    if ($history.PSObject.Properties.Name -contains $promptId) {
        $entry = $history.$promptId
        if ($entry.outputs) { break }
    }
}

if (-not $entry -or -not $entry.outputs) { throw "Render did not complete. Inspect the ComfyUI queue." }
$image = $entry.outputs.'9'.images[0]

$target = Join-Path $OutDir "$Prefix`_$($image.filename)"
$bytes = $client.GetByteArrayAsync("$base/view?filename=$($image.filename)&subfolder=$($image.subfolder)&type=$($image.type)").Result
[System.IO.File]::WriteAllBytes($target, $bytes)

'--- PROMPT SENT TO THE MODEL -------------------------------------------------'
"positive   : $Prompt"
"checkpoint : $Checkpoint"
"sampler    : $Sampler / $Scheduler, $Steps steps, CFG $Cfg, ${Width}x${Height}, seed $Seed"
'dean ref   : ' + $DeanRef
'becky ref  : ' + $BeckyRef
'--- END PROMPT -----------------------------------------------------------------'
"OUTPUT: $target"
