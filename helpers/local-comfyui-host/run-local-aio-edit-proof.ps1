<#
.SYNOPSIS
  Runs the app's merged-checkpoint Qwen edit graph against the local ComfyUI host and saves the result.

.DESCRIPTION
  Reproduces EXACTLY the graph `ComfyUIImageEditingClient.BuildAioMergedCheckpointWorkflow` submits for an
  editor model configured with ImageEditorGraphKind = MergedCheckpoint (CheckpointLoaderSimple + AuraFlow
  shift + CFGNorm + TextEncodeQwenImageEditPlus x2 + FluxKontextMultiReferenceLatentMethod x2 + KSampler),
  over the plain ComfyUI HTTP API (/upload/image -> /prompt -> /history -> /view).

  Purpose: prove the local Rapid-AIO merged checkpoint actually renders through the app's own graph
  (non-explicit test edit) without needing the web app to be rebuilt/restarted.

  Run this from the dev box or the ComfyUI host. PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'http://192.168.0.16:8188',
    [string]$SourceImage = 'specs/image-generator-tests/qwen/images/base.png',
    [string]$Instruction = "Change only the man's shirt from blue to solid red.",
    [string]$Checkpoint = 'Qwen-Rapid-AIO-NSFW-v23.safetensors',
    [int]$Steps = 8,
    [double]$Cfg = 1.0,
    [string]$Sampler = 'euler_ancestral',
    [string]$Scheduler = 'beta',
    [double]$Denoise = 1.0,
    [double]$AuraFlowShift = 3.1,
    [double]$CfgNormStrength = 1.0,
    [long]$Seed = 73191,
    [string]$OutDir = 'artifacts/tmp/proofs/qwen-edit-local',
    [int]$TimeoutSeconds = 1500,
    # Reattach to a render that is already queued/running instead of submitting a new one.
    [string]$ExistingPromptId
)

$ErrorActionPreference = 'Stop'
$base = $ComfyUiUrl.TrimEnd('/')

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(300)

$promptId = $ExistingPromptId
if (-not $promptId) {
    # --- upload the source image -------------------------------------------------
    if (-not (Test-Path $SourceImage)) { throw "Source image '$SourceImage' was not found." }
    $sourceName = [System.IO.Path]::GetFileName($SourceImage)
    $uploadName = "proof-" + $sourceName
    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $SourceImage))
    $multipart = New-Object System.Net.Http.MultipartFormDataContent
    $imageContent = New-Object System.Net.Http.ByteArrayContent (,$bytes)
    $imageContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('image/png')
    $multipart.Add($imageContent, 'image', $uploadName)
    $multipart.Add((New-Object System.Net.Http.StringContent 'true'), 'overwrite')
    $uploadResponse = $client.PostAsync("$base/upload/image", $multipart).Result
    if (-not $uploadResponse.IsSuccessStatusCode) {
        throw "Upload failed: $($uploadResponse.StatusCode) $($uploadResponse.Content.ReadAsStringAsync().Result)"
    }
    $uploaded = ($uploadResponse.Content.ReadAsStringAsync().Result | ConvertFrom-Json).name
    "uploaded: $uploaded"

# --- the app's merged-checkpoint graph ---------------------------------------
$workflow = [ordered]@{
    '1'  = @{ class_type = 'LoadImage';                              inputs = @{ image = $uploaded } }
    '2'  = @{ class_type = 'FluxKontextImageScale';                  inputs = @{ image = @('1', 0) } }
    '3'  = @{ class_type = 'KSampler';                               inputs = @{
                model = @('14', 0); positive = @('12', 0); negative = @('13', 0); latent_image = @('8', 0)
                seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = $Denoise
            } }
    '5'  = @{ class_type = 'ModelSamplingAuraFlow';                   inputs = @{ model = @('16', 0); shift = $AuraFlowShift } }
    '6'  = @{ class_type = 'TextEncodeQwenImageEditPlus';             inputs = @{ clip = @('16', 1); vae = @('16', 2); image1 = @('2', 0); prompt = $Instruction } }
    '7'  = @{ class_type = 'TextEncodeQwenImageEditPlus';             inputs = @{ clip = @('16', 1); vae = @('16', 2); image1 = @('2', 0); prompt = '' } }
    '8'  = @{ class_type = 'VAEEncode';                               inputs = @{ pixels = @('2', 0); vae = @('16', 2) } }
    '9'  = @{ class_type = 'SaveImage';                               inputs = @{ images = @('15', 0); filename_prefix = 'dreamgen_app/qwen-edit-local' } }
    '12' = @{ class_type = 'FluxKontextMultiReferenceLatentMethod';   inputs = @{ conditioning = @('6', 0); reference_latents_method = 'index_timestep_zero' } }
    '13' = @{ class_type = 'FluxKontextMultiReferenceLatentMethod';   inputs = @{ conditioning = @('7', 0); reference_latents_method = 'index_timestep_zero' } }
    '14' = @{ class_type = 'CFGNorm';                                 inputs = @{ model = @('5', 0); strength = $CfgNormStrength } }
    '15' = @{ class_type = 'VAEDecode';                               inputs = @{ samples = @('3', 0); vae = @('16', 2) } }
    '16' = @{ class_type = 'CheckpointLoaderSimple';                  inputs = @{ ckpt_name = $Checkpoint } }
}

$payload = @{ prompt = $workflow; client_id = 'dreamgen-proof' } | ConvertTo-Json -Depth 20 -Compress
$submitContent = New-Object System.Net.Http.StringContent($payload, [System.Text.Encoding]::UTF8, 'application/json')
$submitResponse = $client.PostAsync("$base/prompt", $submitContent).Result
if (-not $submitResponse.IsSuccessStatusCode) {
    throw "Prompt submit failed: $($submitResponse.StatusCode) $($submitResponse.Content.ReadAsStringAsync().Result)"
}
$promptId = ($submitResponse.Content.ReadAsStringAsync().Result | ConvertFrom-Json).prompt_id
"prompt queued: $promptId"
}

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# --- poll history -------------------------------------------------------------
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$image = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    $history = Invoke-RestMethod -Uri "$base/history/$promptId" -TimeoutSec 30
    $entry = $null
    foreach ($property in $history.PSObject.Properties) {
        if ($property.Name -eq $promptId) { $entry = $property.Value }
    }
    if (-not $entry) { continue }
    $statusText = $entry.status.status_str
    if ($statusText -eq 'error') { throw "ComfyUI reported an error: $($entry.status | ConvertTo-Json -Depth 8 -Compress)" }
    if ($entry.outputs) {
        foreach ($nodeId in $entry.outputs.PSObject.Properties.Name) {
            $images = $entry.outputs.$nodeId.images
            if ($images) { $image = $images[0]; break }
        }
    }
    if ($image) { break }
}
if (-not $image) { throw "Timed out after $TimeoutSeconds seconds waiting for the render." }

# --- download the result ------------------------------------------------------
$query = "filename=$([uri]::EscapeDataString($image.filename))&subfolder=$([uri]::EscapeDataString($image.subfolder))&type=$($image.type)"
$viewResponse = $client.GetAsync("$base/view?$query").Result
$viewResponse.EnsureSuccessStatusCode() | Out-Null
$outPath = Join-Path $OutDir ("local-aio-" + $image.filename)
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $outPath), $viewResponse.Content.ReadAsByteArrayAsync().Result)

# The full prompt, as required whenever a generation runs.
'--- PROMPT SENT TO THE MODEL -------------------------------------------------'
"instruction: $Instruction"
"negative   : (empty)"
"checkpoint : $Checkpoint"
"sampler    : $Sampler / $Scheduler, $Steps steps, CFG $Cfg, denoise $Denoise, AuraFlow shift $AuraFlowShift, CFGNorm $CfgNormStrength, seed $Seed"
"graph      : CheckpointLoaderSimple merged checkpoint (ImageEditorGraphKind.MergedCheckpoint)"
'-----------------------------------------------------------------------------'
"OUTPUT: $outPath"
