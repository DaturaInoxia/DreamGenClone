<#
.SYNOPSIS
  Qwen-Image-2.1 text-to-image base render for the image-generator test harnesses.

.DESCRIPTION
  Submits the graph the APP builds for a Qwen-Image-2.1 `SceneImageModelFamily` render
  (`ComfyUIImageClient.BuildQwenImage21Workflow`): UNETLoader -> CLIPLoader +
  TextEncodeQwenImage21 -> EmptyLatentImage -> KSampler -> VAEDecode -> SaveImage. No reference
  slots and no LoRA chain, because the caller passes neither; the reference-bearing and LoRA
  shapes exist in the app and in `run-qwen-2-1-proof.ps1`, and are deliberately not duplicated here.

  Parameter names match the BaseRunner contract of
  `specs/image-generator-tests/sex-slideshow/run-sex-slideshow.ps1` (ComfyUiUrl, Checkpoint,
  Positive, Negative, Seed, Width, Height, OutDir, Prefix), so that harness drives this runner
  unchanged.

  SAMPLING IS NOT A GUESS. Every value below is the app's CONFIGURED value for the local
  Qwen-Image-2.1 model (RegisteredModels 'Qwen-Image-2.1 (Local ComfyUI)', SceneImageModelFamily 5,
  re-qualified 2026-10-02): the unet/clip/vae artifacts, resolution budget 1024, 30 steps, cfg 3,
  er_sde/beta. -Checkpoint is the harness's model label; it is accepted only when it names the same
  artifact as -UnetName, so a merged-checkpoint name can never be rendered by accident.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-native-t2i.ps1 `
    -Positive 'photorealistic studio photograph of a man and a woman standing side by side' `
    -Seed 6601 -OutDir artifacts/tmp/qwen21-t2i
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    # The app's configured Qwen-Image-2.1 diffusion model (a UNETLoader artifact, not a merged checkpoint).
    [string]$UnetName = 'qwen_image_2.1_int8_convrot.safetensors',
    # Harness compatibility label. Must name the same artifact as -UnetName; never a rendered input of its own.
    [string]$Checkpoint = $UnetName,
    [Parameter(Mandatory = $true)][string]$Positive,
    [string]$Negative = '',
    [long]$Seed = 20260922,
    [int]$Width = 1216,
    [int]$Height = 832,
    [string]$OutDir = 'artifacts/tmp/qwen21-native-t2i',
    [string]$Prefix = 'qwen21',
    [string]$ClipName = 'qwen3vl_8b_int8_convrot.safetensors',
    [string]$VaeName = 'qwen_image_2.1_vae_bf16.safetensors',
    [int]$Resolution = 1024,
    [int]$Steps = 30,
    [double]$Cfg = 3.0,
    [string]$Sampler = 'er_sde',
    [string]$Scheduler = 'beta',
    [int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

if ($Checkpoint -ne $UnetName) {
    throw "Checkpoint '$Checkpoint' does not name the diffusion model this runner renders ('$UnetName'). " +
          "Qwen-Image-2.1 loads a UNETLoader artifact, not a merged checkpoint: pass the same value for both, " +
          "or run a merged-checkpoint runner (run-local-aio-edit-proof.ps1) instead."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot
$base = $ComfyUiUrl.TrimEnd('/')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ---- preflight: the host must actually carry the 2.1 graph -----------------------------------
$stats = Invoke-RestMethod -Uri "$base/system_stats" -TimeoutSec 30
Write-Host "Host: ComfyUI $($stats.system.comfyui_version), $($stats.devices[0].name), $([math]::Round($stats.devices[0].vram_free/1GB,2)) GB VRAM free"

$oi = Invoke-RestMethod -Uri "$base/object_info" -TimeoutSec 180
$known = $oi.PSObject.Properties.Name
foreach ($cls in @('UNETLoader','CLIPLoader','VAELoader','TextEncodeQwenImage21','EmptyLatentImage','KSampler','VAEDecode','SaveImage')) {
    if ($known -notcontains $cls) { throw "Host is missing node class '$cls' (Qwen-Image-2.1 native graph)." }
}

# ---- graph: ComfyUIImageClient.BuildQwenImage21Workflow, no references, no LoRA chain ---------
$g = [ordered]@{}
$g['1'] = @{ class_type = 'UNETLoader'; inputs = @{ unet_name = $UnetName; weight_dtype = 'default' } }
$g['2'] = @{ class_type = 'CLIPLoader'; inputs = @{ clip_name = $ClipName; type = 'qwen_image'; device = 'default' } }
$g['3'] = @{ class_type = 'VAELoader'; inputs = @{ vae_name = $VaeName } }
$g['4'] = @{ class_type = 'TextEncodeQwenImage21'; inputs = @{
        clip = @('2', 0); prompt = $Positive; negative_prompt = $Negative; resolution = $Resolution
    } }
$g['5'] = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $Width; height = $Height; batch_size = 1 } }
$g['6'] = @{ class_type = 'KSampler'; inputs = @{
        seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = 1.0
        model = @('1', 0); positive = @('4', 0); negative = @('4', 1); latent_image = @('5', 0)
    } }
$g['7'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('6', 0); vae = @('3', 0) } }
$g['8'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('7', 0); filename_prefix = "qwen21-$Prefix" } }

Write-Host "Submitting: unet=$UnetName clip=$ClipName vae=$VaeName ${Width}x${Height} res=$Resolution $Steps steps cfg $Cfg $Sampler/$Scheduler seed=$Seed"

$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds([Math]::Max(300, $TimeoutSeconds + 120))

$payload = @{ prompt = $g; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 32
$content = [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, 'application/json')
$resp = $client.PostAsync("$base/prompt", $content).Result
$body = $resp.Content.ReadAsStringAsync().Result
if (-not $resp.IsSuccessStatusCode) { throw "Submit failed: $($resp.StatusCode) $body" }
$id = ($body | ConvertFrom-Json).prompt_id
Write-Host "Submitted prompt_id=$id; waiting (timeout ${TimeoutSeconds}s)..."

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$result = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    $h = Invoke-RestMethod -Uri "$base/history/$id" -TimeoutSec 60
    if ($h.PSObject.Properties[$id]) {
        $entry = $h.$id
        if ($entry.status.completed) { $result = $entry; break }
        if ($entry.status.status_str -eq 'error') {
            throw "Workflow errored: $($entry.status.messages | ConvertTo-Json -Compress)"
        }
    }
}
if (-not $result) { throw "Timed out waiting for prompt $id" }

$saved = 0
foreach ($nodeId in $result.outputs.PSObject.Properties.Name) {
    $node = $result.outputs.$nodeId
    foreach ($img in @($node.images)) {
        if (-not $img) { continue }
        $url = "$base/view?filename=$($img.filename)&subfolder=$($img.subfolder)&type=$($img.type)"
        Invoke-WebRequest -Uri $url -OutFile (Join-Path $OutDir $img.filename) -TimeoutSec 300
        Write-Host "Saved: $(Join-Path $OutDir $img.filename)"
        $saved++
    }
}
if ($saved -eq 0) { throw "Workflow completed but produced no image." }
Write-Host "DONE. $saved image(s) in $OutDir"
