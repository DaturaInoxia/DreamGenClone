<#
.SYNOPSIS
  Qwen-Image-2.1 Edit + a community 360 panorama LoRA: 1-N photos of ONE spot -> a 2:1
  equirectangular panorama, for the location-reference walk-around work (B-139).

.DESCRIPTION
  Drives the RECIPE the LoRA's own model card documents, not the app's edit graph:

    * `TextEncodeQwenImage21` with `images.image_1` .. `images.image_N` (flat DOTTED autogrow ids,
      the only shape ComfyUI accepts) and `resolution` 1088 (a total pixel BUDGET, not a dimension).
    * the sampler starts from an EMPTY 1536x768 latent. This is the whole reason this is a separate
      runner: the app's `BuildQwenImage21EditWorkflow` derives its latent from `image_1`'s own size
      ("any other size shifts the edit"), so it can never emit a 2:1 panorama. Here the canvas is
      chosen by the caller and `image_1` is only an input.
    * 25 steps, CFG 1.0, euler/simple.

  LoRA: `pano360_qwen21_edit_v1.safetensors` ("360 Panorama Maker - Qwen-Image 2.1 Edit",
  Civitai model 2976898 version 3374088, published 2026-10-01, nsfw=FALSE). Staged onto the host by
  `fetch-qwen21-nsfw-lora.ps1 -ModelVersionId 3374088`.

  WHAT THE CARD REQUIRES OF THE INPUT, quoted, because the result is only meaningful inside it:
    "1-3 photos taken from the same spot, turning the camera between shots ... Photos of the same
     place from different positions, or of different places, don't fit the model's assumption of a
     single viewpoint."
    "The first image always lands at the center of the panorama, looking straight ahead."
    "Whatever no view shows is invented."

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-pano360.ps1 `
    -Images @('front.png','back.png') -Scene 'A dim maintenance shed interior ...' `
    -OutDir artifacts/tmp/pano360/twoViews
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'http://192.168.0.11:8188',
    # 1-3 photos of the SAME spot, turning the camera. The first becomes the panorama's centre.
    [Parameter(Mandatory = $true)][string[]]$Images,
    # The caption's `Scene:` part: the whole surrounding space, not just what the views show.
    [Parameter(Mandatory = $true)][string]$Scene,
    [string]$UnetName = 'qwen_image_2.1_int8_convrot.safetensors',
    [string]$ClipName = 'qwen3vl_8b_int8_convrot.safetensors',
    [string]$VaeName = 'qwen_image_2.1_vae_bf16.safetensors',
    [string]$LoraName = 'pano360_qwen21_edit_v1.safetensors',
    [double]$LoraStrength = 1.0,
    [long]$Seed = 7,
    # The card's documented canvas and budget.
    [int]$Width = 1536,
    [int]$Height = 768,
    [int]$Resolution = 1088,
    [int]$Steps = 25,
    [double]$Cfg = 1.0,
    [string]$Sampler = 'euler',
    [string]$Scheduler = 'simple',
    [string]$OutDir = 'artifacts/tmp/pano360',
    [string]$Prefix = 'pano360',
    [int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

if ($Images.Count -gt 0 -and $Images.Count -le 3 -and $Images.Count -eq 1 -and $Images[0] -match ',') {
    # `powershell -File` cannot bind an ARRAY parameter: it stringifies it, so `-Images 'a','b'` arrives as
    # the single value "a,b" and would be looked up as one absurd path. Splitting it here means the runner
    # is usable both ways -- `& runner.ps1 -Images @('a','b')` (in-process, the way the other harnesses
    # call each other) and `powershell -File runner.ps1 -Images 'a,b'`.
    $Images = $Images[0].Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
}

if ($Images.Count -lt 1 -or $Images.Count -gt 3) {
    throw "The LoRA takes 1 to 3 views of one spot; $($Images.Count) were given."
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot
$base = $ComfyUiUrl.TrimEnd('/')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ---- preflight: the host must carry the graph AND the LoRA -----------------------------------
# The host demonstrably drops connections intermittently when it is busy (observed 2026-10-03: the
# same /system_stats call succeeded and then failed seconds apart), so every preflight call is
# retried. A dropped connection is not a missing node, and reporting one as the other wastes a run.
function Invoke-Host {
    param([string]$Uri, [int]$TimeoutSec = 30, [int]$Attempts = 6)
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try { return Invoke-RestMethod -Uri $Uri -TimeoutSec $TimeoutSec }
        catch {
            if ($attempt -eq $Attempts) { throw }
            Write-Host "  host not answering ($($_.Exception.Message.Split([char]10)[0])); retry $attempt/$($Attempts)..."
            Start-Sleep -Seconds (3 * $attempt)
        }
    }
}

$stats = Invoke-Host -Uri "$base/system_stats"
Write-Host "Host: ComfyUI $($stats.system.comfyui_version), $($stats.devices[0].name), $([math]::Round($stats.devices[0].vram_free/1GB,2)) GB VRAM free"

$oi = Invoke-Host -Uri "$base/object_info" -TimeoutSec 180
$known = $oi.PSObject.Properties.Name
foreach ($cls in @('UNETLoader','CLIPLoader','VAELoader','LoraLoader','TextEncodeQwenImage21','EmptyLatentImage','KSampler','VAEDecode','SaveImage','LoadImage')) {
    if ($known -notcontains $cls) { throw "Host is missing node class '$cls'." }
}
$available = $oi.LoraLoader.input.required.lora_name[0]
if ($available -notcontains $LoraName) {
    throw "Host does not list LoRA '$LoraName'. Stage it first: " +
          "helpers/local-comfyui-host/fetch-qwen21-nsfw-lora.ps1 -ModelVersionId 3374088"
}

$instruction = "Transform this set of images into an equirectangular 360 panorama. Scene: $Scene"

# ---- upload the views ------------------------------------------------------------------------
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
    $parsed = $body | ConvertFrom-Json
    if ($parsed.subfolder) { return "$($parsed.subfolder)/$($parsed.name)" }
    return $parsed.name
}

$g = [ordered]@{}
$g['2'] = @{ class_type = 'UNETLoader'; inputs = @{ unet_name = $UnetName; weight_dtype = 'default' } }
$g['3'] = @{ class_type = 'CLIPLoader'; inputs = @{ clip_name = $ClipName; type = 'qwen_image'; device = 'default' } }
$g['9'] = @{ class_type = 'VAELoader'; inputs = @{ vae_name = $VaeName } }

# The LoRA conditions BOTH halves of the model: the sampler's model branch and the text encoder's.
$g['10'] = @{ class_type = 'LoraLoader'; inputs = @{
        lora_name = $LoraName; strength_model = $LoraStrength; strength_clip = $LoraStrength
        model = @('2', 0); clip = @('3', 0)
    } }

$encodeInputs = @{ clip = @('10', 1); prompt = $instruction; negative_prompt = ''; resolution = $Resolution }
for ($i = 0; $i -lt $Images.Count; $i++) {
    $nodeId = (20 + $i).ToString()
    $uploaded = Upload-Image $Images[$i] "pano360-view$($i + 1).png"
    $g[$nodeId] = @{ class_type = 'LoadImage'; inputs = @{ image = $uploaded } }
    $encodeInputs["images.image_$($i + 1)"] = @($nodeId, 0)
    Write-Host "View $($i + 1) uploaded: $uploaded"
}
$g['4'] = @{ class_type = 'TextEncodeQwenImage21'; inputs = $encodeInputs }

# THE documented canvas: an empty 2:1 latent, NOT the encoder's own latent (which follows image_1).
$g['5'] = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $Width; height = $Height; batch_size = 1 } }
$g['6'] = @{ class_type = 'KSampler'; inputs = @{
        seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = 1.0
        model = @('10', 0); positive = @('4', 0); negative = @('4', 1); latent_image = @('5', 0)
    } }
$g['7'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('6', 0); vae = @('9', 0) } }
$g['8'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('7', 0); filename_prefix = $Prefix } }

Write-Host "Submitting: views=$($Images.Count) ${Width}x${Height} res=$Resolution $Steps steps cfg $Cfg $Sampler/$Scheduler lora=$LoraName@$LoraStrength seed=$Seed"

$payload = @{ prompt = $g; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 32
$content = [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, 'application/json')
$resp = $null
for ($attempt = 1; $attempt -le 6; $attempt++) {
    try { $resp = $client.PostAsync("$base/prompt", $content).Result; break }
    catch {
        if ($attempt -eq 6) { throw }
        Write-Host "  submit failed ($($_.Exception.Message.Split([char]10)[0])); retry $attempt/6..."
        Start-Sleep -Seconds (3 * $attempt)
    }
}
$body = $resp.Content.ReadAsStringAsync().Result
if (-not $resp.IsSuccessStatusCode) { throw "Submit failed: $($resp.StatusCode) $body" }
$id = ($body | ConvertFrom-Json).prompt_id
Write-Host "Submitted prompt_id=$id; waiting (timeout ${TimeoutSeconds}s)..."

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$result = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    try { $h = Invoke-RestMethod -Uri "$base/history/$id" -TimeoutSec 60 }
    catch { continue }
    if ($h.PSObject.Properties[$id]) {
        $entry = $h.$id
        if ($entry.status.completed) { $result = $entry; break }
        if ($entry.status.status_str -eq 'error') {
            throw "Workflow errored: $($entry.status.messages | ConvertTo-Json -Compress -Depth 8)"
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
