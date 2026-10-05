<#
.SYNOPSIS
  SDXL + EXACT depth control on the synthetic room: does supplying geometry fix the layout?

.DESCRIPTION
  Companion to run-room-pov-proof.ps1. The 2.1-native depth ControlNet
  (QwenImageDiffsynthControlnet + qwen_image_depth_diffsynth_controlnet.safetensors) errors on this
  host inside ComfyUI's own `process_input_latent_image`, which applies `latent_formats.Wan21()` to a
  Qwen latent, so the 2.1 geometry route is blocked at the host. This runner instead uses the SDXL
  depth ControlNet that the layout-structure proof already proved works on this machine
  (controlnet-depth-sdxl-1.0.safetensors), so the GEOMETRY question can still be answered.

  Unlike the earlier proof, the depth map is NOT estimated from a photo: room.py renders it from the
  same analytic geometry as the ground truth, so it is an exact control input.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File .../run-room-depth-sdxl.ps1 `
    -DepthImage artifacts/tmp/room-proof/gt/N_to_S-depth.png -OutDir artifacts/tmp/room-proof/runs/F
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'http://192.168.0.11:8188',
    [Parameter(Mandatory = $true)][string]$DepthImage,
    [Parameter(Mandatory = $true)][string]$Prompt,
    [string]$Negative = 'blurry, watermark, text, people, person',
    [double]$Strength = 0.75,
    [string]$Checkpoint = 'juggernautXL_ragnarok.safetensors',
    [string]$ControlNet = 'controlnet-depth-sdxl-1.0.safetensors',
    [int]$Width = 1024, [int]$Height = 1024,
    [int]$Steps = 30, [double]$Cfg = 5.0,
    [string]$Sampler = 'dpmpp_2m_sde', [string]$Scheduler = 'karras',
    [long]$Seed = 20261004,
    [string]$OutDir = 'artifacts/tmp/room-proof/runs/sdxl-depth',
    [int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
Add-Type -AssemblyName System.Net.Http
$base = $ComfyUiUrl.TrimEnd('/')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$oi = Invoke-RestMethod -Uri "$base/object_info" -TimeoutSec 180
$known = $oi.PSObject.Properties.Name
foreach ($cls in @('CheckpointLoaderSimple','CLIPTextEncode','EmptyLatentImage','LoadImage','ImageScale',
                   'ControlNetLoader','ControlNetApplyAdvanced','KSampler','VAEDecode','SaveImage')) {
    if ($known -notcontains $cls) { throw "Host is missing node class '$cls'." }
}
if ($oi.ControlNetLoader.input.required.control_net_name[0] -notcontains $ControlNet) {
    throw "Host does not list ControlNet '$ControlNet'."
}

$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds([Math]::Max(300, $TimeoutSeconds + 120))

function Upload-Image([string]$filePath, [string]$uploadName) {
    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $filePath))
    $multipart = New-Object System.Net.Http.MultipartFormDataContent
    $content = New-Object System.Net.Http.ByteArrayContent (, $bytes)
    $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('image/png')
    $multipart.Add($content, 'image', $uploadName)
    $multipart.Add((New-Object System.Net.Http.StringContent 'true'), 'overwrite')
    $resp = $client.PostAsync("$base/upload/image", $multipart).Result
    $body = $resp.Content.ReadAsStringAsync().Result
    if (-not $resp.IsSuccessStatusCode) { throw "Upload of '$uploadName' failed: $($resp.StatusCode) $body" }
    return ($body | ConvertFrom-Json).name
}

$depthUpload = Upload-Image $DepthImage 'room-depth-control.png'
Write-Host "Depth control uploaded: $depthUpload"

$g = [ordered]@{}
$g['1'] = @{ class_type = 'CheckpointLoaderSimple'; inputs = @{ ckpt_name = $Checkpoint } }
$g['2'] = @{ class_type = 'CLIPTextEncode'; inputs = @{ text = $Prompt; clip = @('1', 1) } }
$g['3'] = @{ class_type = 'CLIPTextEncode'; inputs = @{ text = $Negative; clip = @('1', 1) } }
$g['4'] = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $Width; height = $Height; batch_size = 1 } }
$g['5'] = @{ class_type = 'LoadImage'; inputs = @{ image = $depthUpload } }
$g['6'] = @{ class_type = 'ImageScale'; inputs = @{
        image = @('5', 0); width = $Width; height = $Height; upscale_method = 'lanczos'; crop = 'disabled' } }
$g['7'] = @{ class_type = 'ControlNetLoader'; inputs = @{ control_net_name = $ControlNet } }
$g['8'] = @{ class_type = 'ControlNetApplyAdvanced'; inputs = @{
        positive = @('2', 0); negative = @('3', 0); control_net = @('7', 0); image = @('6', 0)
        strength = $Strength; start_percent = 0.0; end_percent = 1.0 } }
$g['9'] = @{ class_type = 'KSampler'; inputs = @{
        seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = 1.0
        model = @('1', 0); positive = @('8', 0); negative = @('8', 1); latent_image = @('4', 0) } }
$g['10'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('9', 0); vae = @('1', 2) } }
$g['11'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('10', 0); filename_prefix = 'room-depth' } }

Write-Host "Submitting SDXL+exact-depth: $Checkpoint $Checkpoint/$ControlNet@$Strength ${Width}x$Height $Steps steps cfg $Cfg seed=$Seed"
$payload = @{ prompt = $g; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 32
$content = [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, 'application/json')
$resp = $client.PostAsync("$base/prompt", $content).Result
$body = $resp.Content.ReadAsStringAsync().Result
if (-not $resp.IsSuccessStatusCode) { throw "Submit failed: $($resp.StatusCode) $body" }
$id = ($body | ConvertFrom-Json).prompt_id
Write-Host "Submitted prompt_id=$id; waiting..."

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$result = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    $h = Invoke-RestMethod -Uri "$base/history/$id" -TimeoutSec 60
    if ($h.PSObject.Properties[$id]) {
        $entry = $h.$id
        if ($entry.status.messages | Where-Object { $_[0] -eq 'execution_error' }) {
            throw "Workflow errored: $($entry.status.messages | ConvertTo-Json -Depth 8 -Compress)"
        }
        if ($entry.status.completed -eq $true -or $entry.outputs) { $result = $entry; break }
    }
}
if (-not $result) { throw "Timed out waiting for prompt $id" }

$saved = @()
foreach ($nodeId in $result.outputs.PSObject.Properties.Name) {
    foreach ($img in @($result.outputs.$nodeId.images)) {
        if ($img) {
            $src = [System.IO.Path]::Combine($OutDir, $img.filename)
            $url = "$base/view?filename=$([uri]::EscapeDataString($img.filename))&subfolder=$([uri]::EscapeDataString($img.subfolder))&type=$($img.type)"
            Invoke-WebRequest -Uri $url -OutFile $src -TimeoutSec 300
            $saved += $src
        }
    }
}
Write-Host "DONE. $($saved.Count) image(s):"
$saved | ForEach-Object { Write-Host "  $_" }
