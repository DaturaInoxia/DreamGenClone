<#
.SYNOPSIS
  Renders ONE base image locally through ComfyUI (BigLust v1.6 defaults) for use as an edit-matrix input.

.DESCRIPTION
  Local T2I over the plain ComfyUI HTTP API (/prompt -> /history -> /view). Defaults are the verified
  BigLust settings (dpmpp_2m_sde / sgm_uniform / 50 steps / CFG 7.0 / 832x1216 / empty negative).

  Output is written to the caller's OutDir. For the anatomy matrix that is the tracked `images/` folder
  inside the harness, so a base render is reviewable and reproducible alongside the results it feeds.

  Run from the repo root. PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'http://192.168.0.16:8188',
    [string]$Checkpoint = 'bigLust_v16.safetensors',
    [Parameter(Mandatory = $true)][string]$Positive,
    [string]$Negative = '',
    [string]$LoraName = '',
    [double]$LoraStrength = 1.0,
    [long]$Seed = 5501,
    [int]$Steps = 50,
    [double]$Cfg = 7.0,
    [string]$Sampler = 'dpmpp_2m_sde',
    [string]$Scheduler = 'sgm_uniform',
    [int]$Width = 832,
    [int]$Height = 1216,
    [string]$Prefix = 'anatomy-base',
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Stop'
$base = $ComfyUiUrl.TrimEnd('/')
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(600)

$workflow = [ordered]@{
    '4' = @{ class_type = 'CheckpointLoaderSimple'; inputs = @{ ckpt_name = $Checkpoint } }
    '5' = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $Width; height = $Height; batch_size = 1 } }
    '8' = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('3', 0); vae = @('4', 2) } }
    '9' = @{ class_type = 'SaveImage'; inputs = @{ images = @('8', 0); filename_prefix = $Prefix } }
}

# Model/clip source: raw checkpoint, or checkpoint -> LoraLoader when a LoRA is requested.
if ($LoraName) {
    $workflow['11'] = @{ class_type = 'LoraLoader'; inputs = @{
        model = @('4', 0); clip = @('4', 1); lora_name = $LoraName; strength_model = $LoraStrength; strength_clip = $LoraStrength } }
    $modelSrc = @('11', 0); $clipSrc = @('11', 1)
} else {
    $modelSrc = @('4', 0); $clipSrc = @('4', 1)
}

$workflow['6'] = @{ class_type = 'CLIPTextEncode'; inputs = @{ clip = $clipSrc; text = $Positive } }
$workflow['7'] = @{ class_type = 'CLIPTextEncode'; inputs = @{ clip = $clipSrc; text = $Negative } }
$workflow['3'] = @{ class_type = 'KSampler'; inputs = @{
    seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler
    denoise = 1.0; model = $modelSrc; positive = @('6', 0); negative = @('7', 0); latent_image = @('5', 0) } }

$payload = @{ prompt = $workflow; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 12
$content = New-Object System.Net.Http.StringContent $payload
$content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/json')
$queued = ($client.PostAsync("$base/prompt", $content).Result.Content.ReadAsStringAsync().Result | ConvertFrom-Json)

if ($queued.error) { throw "ComfyUI rejected the prompt: $($queued.error | ConvertTo-Json -Depth 6)" }
$promptId = $queued.prompt_id
"queued: $promptId"

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

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$target = Join-Path $OutDir ($Prefix + '_' + $image.filename)
$bytes = $client.GetByteArrayAsync("$base/view?filename=$($image.filename)&subfolder=$($image.subfolder)&type=$($image.type)").Result
[System.IO.File]::WriteAllBytes($target, $bytes)

'--- PROMPT SENT TO THE MODEL -------------------------------------------------'
"positive   : $Positive"
"negative   : $(if ([string]::IsNullOrWhiteSpace($Negative)) { '(empty)' } else { $Negative })"
"checkpoint : $Checkpoint$(if ($LoraName) { "  + LoRA $LoraName @ $LoraStrength" } else { '' })"
"sampler    : $Sampler / $Scheduler, $Steps steps, CFG $Cfg, ${Width}x${Height}, seed $Seed"
'-----------------------------------------------------------------------------'
"OUTPUT: $target"
