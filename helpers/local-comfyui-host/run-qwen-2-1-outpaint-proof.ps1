<#
.SYNOPSIS
  Outpaint proof for Qwen-Image-2.1: extend a room shot sideways and measure that the
  original area is preserved while the newly exposed strip is generated (N6, EVP-2).

.DESCRIPTION
  Builds the documented 2.1 edit graph with the sampler's latent swapped for
  VAEEncodeForInpaint over a PADDED canvas (CASE-21's containment recipe, applied to a
  canvas larger than the source). The encoder conditions on the ORIGINAL source
  (images.image_1), while the sampler starts from VAEEncodeForInpaint(padded, new-strip mask).

  Direction + amount, not target ratio: `-Direction` names the edge to extend, `-Percent`
  is how much of the source's own width/height to add.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen-2-1-outpaint-proof.ps1 -Direction right -Percent 50
#>
[CmdletBinding()]
param(
    [ValidateSet('left', 'right', 'top', 'bottom')]
    [string]$Direction = 'right',
    [int]$Percent = 50,
    [string]$SourceImage = 'specs/image-generator-tests/dual-base-location/runs/dual-location-local-fast/refs/locref-bedroom.png',
    [string]$ComfyUiUrl = 'http://192.168.0.11:8188',
    [string]$OutRoot = 'artifacts/tmp/qwen-2-1-outpaint',
    [int]$Seed = 20261002,
    [int]$Steps = 25,
    [int]$Resolution = 1024,
    [int]$TimeoutSec = 2400,
    [string]$UnetName = 'qwen_image_2.1_int8_convrot.safetensors',
    [string]$ClipName = 'qwen3vl_8b_int8_convrot.safetensors',
    [string]$VaeName = 'qwen_image_2.1_vae_bf16.safetensors'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot
$base = $ComfyUiUrl.TrimEnd('/')
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null

function Fail($msg) { throw $msg }

# ---- preflight: host reachable, queue idle-ish, nodes present --------------------------------
$stats = Invoke-RestMethod -Uri "$base/system_stats" -TimeoutSec 30
$queue = Invoke-RestMethod -Uri "$base/queue" -TimeoutSec 30
Write-Host "Host: ComfyUI $($stats.system.comfyui_version), $($stats.devices[0].name), $([math]::Round($stats.devices[0].vram_free/1GB,2)) GB VRAM free; queue running=$($queue.queue_running.Count) pending=$($queue.queue_pending.Count)"

$oi = Invoke-RestMethod -Uri "$base/object_info" -TimeoutSec 180
$known = $oi.PSObject.Properties.Name
foreach ($cls in @('UNETLoader','CLIPLoader','VAELoader','KSampler','VAEDecode','SaveImage','TextEncodeQwenImage21','QwenImage21Cache','LoadImage','ImagePadForOutpaint','MaskRectArea','VAEEncodeForInpaint')) {
    if ($known -notcontains $cls) { Fail "Host is missing node class '$cls'." }
}

# ---- measure the source so pads and masks are in the padded canvas's percent ----------------
$src = (Resolve-Path $SourceImage).Path
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Image]::FromFile($src)
$srcW = $bmp.Width; $srcH = $bmp.Height; $bmp.Dispose()
Write-Host "Source: $($srcW)x$($srcH)  ->  Direction=$Direction Percent=$Percent"

$padL = 0; $padT = 0; $padR = 0; $padB = 0
switch ($Direction) {
    'left'   { $padL = [int]($srcW * $Percent / 100.0) }
    'right'  { $padR = [int]($srcW * $Percent / 100.0) }
    'top'    { $padT = [int]($srcH * $Percent / 100.0) }
    'bottom' { $padB = [int]($srcH * $Percent / 100.0) }
}
$outW = $srcW + $padL + $padR
$outH = $srcH + $padT + $padB
Write-Host "Padded canvas: $($outW)x$($outH) (pad L=$padL T=$padT R=$padR B=$padB)"

# New-strip mask, in percent of the PADDED canvas. ImagePadForOutpaint places the source at
# (left, top), so the exposed strip is the complement of the source's rect.
$mx = [math]::Round($padL / $outW * 100, 2)
$my = [math]::Round($padT / $outH * 100, 2)
$mw = [math]::Round($srcW / $outW * 100, 2)
$mh = [math]::Round($srcH / $outH * 100, 2)
Write-Host "Source rect in padded canvas: x=$mx y=$my w=$mw h=$mh (percent)"

# ---- upload ---------------------------------------------------------------
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds([Math]::Max(300, $TimeoutSec + 120))

function Upload-Image([string]$filePath, [string]$uploadName) {
    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $filePath))
    $multipart = New-Object System.Net.Http.MultipartFormDataContent
    $content = New-Object System.Net.Http.ByteArrayContent (, $bytes)
    $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('image/png')
    $multipart.Add($content, 'image', $uploadName)
    $multipart.Add((New-Object System.Net.Http.StringContent 'true'), 'overwrite')
    $resp = $client.PostAsync("$base/upload/image", $multipart).Result
    $body = $resp.Content.ReadAsStringAsync().Result
    if (-not $resp.IsSuccessStatusCode) { Fail "Upload failed: $($resp.StatusCode) $body" }
    $parsed = $body | ConvertFrom-Json
    if ($parsed.subfolder) { return "$($parsed.subfolder)/$($parsed.name)" }
    return $parsed.name
}

$uploaded = Upload-Image $src "outpaint-source.png"
Write-Host "Uploaded: $uploaded"

$prompt = switch ($Direction) {
    'left'   { "Extend this photograph to the LEFT, revealing more of the room beyond the left edge. Keep the existing area exactly as it is; generate only the newly revealed strip on the left, continuing the wall, floor, furniture and lighting consistently." }
    'right'  { "Extend this photograph to the RIGHT, revealing more of the room beyond the right edge. Keep the existing area exactly as it is; generate only the newly revealed strip on the right, continuing the wall, floor, furniture and lighting consistently." }
    'top'    { "Extend this photograph UPWARD, revealing more of the room above the top edge. Keep the existing area exactly as it is; generate only the newly revealed strip at the top, continuing the wall, ceiling and lighting consistently." }
    'bottom' { "Extend this photograph DOWNWARD, revealing more of the room below the bottom edge. Keep the existing area exactly as it is; generate only the newly revealed strip at the bottom, continuing the floor and lighting consistently." }
}

# ---- workflow ---------------------------------------------------------------
#  1 LoadImage(source)                -> encoder images.image_1 (the scene conditioning)
#  10 ImagePadForOutpaint(source)     -> the padded canvas
#  11 MaskRectArea(new-strip rect)    -> white over the exposed strip, black over the original
#  12 VAEEncodeForInpaint(padded, mask) -> the sampler's starting latent (CASE-21 recipe)
#  2 CLIPLoader, 3 VAELoader, 4 TextEncodeQwenImage21, 5 QwenImage21Cache, 6 KSampler, 7 VAEDecode, 8 SaveImage
$g = [ordered]@{}
$g['1'] = @{ class_type = 'LoadImage'; inputs = @{ image = $uploaded } }
$g['2'] = @{ class_type = 'CLIPLoader'; inputs = @{ clip_name = $ClipName; type = 'qwen_image'; device = 'default' } }
$g['3'] = @{ class_type = 'VAELoader'; inputs = @{ vae_name = $VaeName } }
$g['4'] = @{ class_type = 'UNETLoader'; inputs = @{ unet_name = $UnetName; weight_dtype = 'default' } }
$g['5'] = @{ class_type = 'QwenImage21Cache'; inputs = @{ model = @('4', 0); device = 'auto'; dtype = 'default' } }
$g['6'] = @{ class_type = 'TextEncodeQwenImage21'; inputs = @{
        clip = @('2', 0); vae = @('3', 0); prompt = $prompt; negative_prompt = ''; resolution = $Resolution
        'images.image_1' = @('1', 0)
    } }
$g['7'] = @{ class_type = 'KSampler'; inputs = @{
        model = @('5', 0); positive = @('6', 0); negative = @('6', 1); latent_image = @('12', 0)
        seed = $Seed; steps = $Steps; cfg = 1.0; sampler_name = 'euler'; scheduler = 'simple'; denoise = 1.0
    } }
$g['8'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('7', 0); vae = @('3', 0) } }
$g['9'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('8', 0); filename_prefix = 'qwen21-outpaint' } }

# ---- canvas + mask (CASE-21's containment recipe, on a canvas larger than the source) --------
$g['10'] = @{ class_type = 'ImagePadForOutpaint'; inputs = @{
        image = @('1', 0); left = $padL; top = $padT; right = $padR; bottom = $padB; feathering = 0
    } }
$g['11'] = @{ class_type = 'MaskRectArea'; inputs = @{
        x = $mx; y = $my; width = $mw; height = $mh; blur_radius = 0
    } }
# The strip mask must be the INVERSE of the source rect: white over the exposed strip, black over the
# original (MaskRectArea is white-on-black, so the source rect is white and must be flipped).
$g['11b'] = @{ class_type = 'InvertMask'; inputs = @{ mask = @('11', 0) } }
$g['12'] = @{ class_type = 'VAEEncodeForInpaint'; inputs = @{
        pixels = @('10', 0); vae = @('3', 0); mask = @('11b', 0); grow_mask_by = 6
    } }

$payload = @{ prompt = $g; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 32
$content = [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, 'application/json')
$resp = $client.PostAsync("$base/prompt", $content).Result
$body = $resp.Content.ReadAsStringAsync().Result
if (-not $resp.IsSuccessStatusCode) { Fail "Submit failed: $($resp.StatusCode) $body" }
$id = ($body | ConvertFrom-Json).prompt_id
Write-Host "Submitted prompt_id=$id; waiting for completion (timeout ${TimeoutSec}s)..."

$deadline = (Get-Date).AddSeconds($TimeoutSec)
$result = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    $h = Invoke-RestMethod -Uri "$base/history/$id" -TimeoutSec 60
    if ($h.PSObject.Properties[$id]) {
        $entry = $h.$id
        if ($entry.status.completed) { $result = $entry; break }
        if ($entry.status.status_str -eq 'error') { Fail "Workflow errored: $($entry.status.messages | ConvertTo-Json -Compress)" }
    }
}
if (-not $result) { Fail "Timed out waiting for prompt $id" }

$outDir = Join-Path $OutRoot "$Direction-$Percent"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
foreach ($nodeId in $result.outputs.PSObject.Properties.Name) {
    $node = $result.outputs.$nodeId
    if ($node.images) {
        foreach ($img in $node.images) {
            $url = "$base/view?filename=$($img.filename)&subfolder=$($img.subfolder)&type=$($img.type)"
            Invoke-WebRequest -Uri $url -OutFile (Join-Path $outDir $img.filename) -TimeoutSec 120
            Write-Host "Saved: $(Join-Path $outDir $img.filename) ($($img.filename))"
        }
    }
}
Write-Host "DONE. Padded canvas $($outW)x$($outH); source rect x=$mx% y=$my% w=$mw% h=$mh%."
