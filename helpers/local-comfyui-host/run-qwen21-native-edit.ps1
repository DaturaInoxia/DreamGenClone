<#
.SYNOPSIS
  Qwen-Image-2.1 native source-image edit: one instruction applied to one source image, exactly the
  graph the APP submits for the local 'Qwen-Image-2.1 Editor' model.

.DESCRIPTION
  Mirrors `ComfyUIImageEditingClient.BuildQwenImage21EditWorkflow` (ImageEditorGraphKind
  `QwenImage21Native`): the source travels through ONE `TextEncodeQwenImage21` that also returns the
  sampler's starting latent — an EMPTY latent sized to image_1 ("any other size shifts the edit"), so at
  denoise 1 the source enters the render through the conditioning only, and the sampler starts from a
  noise draw determined by the seed. `QwenImage21Cache` sits between the UNETLoader and the KSampler, and
  every reference occupies `images.image_{i+2}` after the source at `images.image_1`.

  BECAUSE the start is a seed-derived noise draw, a CHAIN that reuses one seed replays the same noise at
  every link and collapses into noise by the third edit (measured; ComfyUI issue 16607, Diffusers issue
  14824 call it "noise replay"). Give each link its own seed — `run-sex-slideshow.ps1 -VarySeedPerStep`.

  Parameter names match the EditRunner contract of
  `specs/image-generator-tests/sex-slideshow/run-sex-slideshow.ps1` (ComfyUiUrl, SourceImage,
  Instruction, Seed, Checkpoint, OutDir, LoraName, LoraStrength, Steps, Cfg, Sampler, Scheduler,
  Denoise, AnchorImage), so that harness — and its chained / -FromBase modes — drive this runner
  unchanged.

  SAMPLING IS NOT A GUESS. The defaults are the app's CONFIGURED values for the local
  'Qwen-Image-2.1 Editor' row (ImageEditorGraphKind QwenImage21Native): diffusion model
  `qwen_image_2.1_int8_convrot.safetensors`, text encoder `qwen3vl_8b_int8_convrot.safetensors`, VAE
  `qwen_image_2.1_vae_bf16.safetensors`, 25 steps, CFG 1, euler/simple, denoise 1, reference
  resolution budget 1024, and NO editor LoRA (ImageEditorLoraName is blank, which is a configured
  state, not a fallback — so the graph emits no loader node unless -LoraName is given).

  -Checkpoint is the harness's model label. It is a UNETLoader artifact for this family, not a merged
  checkpoint, so it is accepted only when it names the same file as -UnetName.

  -AnchorImage, when given, is wired the way the app wires a reference (LoadImage -> images.image_2).
  The 2.1 editor has no separate "anchor" concept; a second image is simply the next reference slot.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-native-edit.ps1 `
    -SourceImage artifacts/tmp/step01.png `
    -Instruction 'the woman kneels in front of the man' `
    -Seed 6601 -OutDir artifacts/tmp/qwen21-edit
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    [Parameter(Mandatory = $true)][string]$SourceImage,
    [Parameter(Mandatory = $true)][string]$Instruction,
    [string]$Negative = '',
    [long]$Seed = 20260922,
    # The app's configured Qwen-Image-2.1 editor diffusion model (a UNETLoader artifact).
    [string]$UnetName = 'qwen_image_2.1_int8_convrot.safetensors',
    # Harness compatibility label. Must name the same artifact as -UnetName; never a rendered input of its own.
    [string]$Checkpoint = $UnetName,
    [string]$ClipName = 'qwen3vl_8b_int8_convrot.safetensors',
    [string]$VaeName = 'qwen_image_2.1_vae_bf16.safetensors',
    [int]$Resolution = 1024,
    [int]$Steps = 25,
    [double]$Cfg = 1.0,
    [string]$Sampler = 'euler',
    [string]$Scheduler = 'simple',
    [double]$Denoise = 1.0,
    # Blank is the app's configured state for this model: no LoRA node is emitted at all.
    [string]$LoraName = '',
    [double]$LoraStrength = 0,
    [string]$AnchorImage = '',
    # Additional reference images beyond the source (and beyond -AnchorImage when both are given).
    # They occupy images.image_2, image_3, ... in the order supplied, exactly as the app numbers a
    # reference set. The room-POV proof needs four wall views at once; before this, only one extra
    # reference could be expressed, so a multi-reference question could not be asked at all.
    [string[]]$References = @(),
    # Optional depth-ControlNet geometry control for the view being asked for. The image is a depth
    # map (near = bright) loaded through ModelPatchLoader + QwenImageDiffsynthControlnet, which is the
    # only control route this host exposes for the Qwen family. Without it the model has appearance
    # references but no geometry, which is the gap the room-POV proof measured.
    [string]$DepthControlImage = '',
    [double]$DepthStrength = 1.0,
    [string]$DepthPatchName = 'qwen_image_depth_diffsynth_controlnet.safetensors',
    [string]$OutDir = 'artifacts/tmp/qwen21-native-edit',
    [int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

if ($Checkpoint -ne $UnetName) {
    throw "Checkpoint '$Checkpoint' does not name the diffusion model this runner renders ('$UnetName'). " +
          "Qwen-Image-2.1 loads a UNETLoader artifact, not a merged checkpoint: pass the same value for both, " +
          "or run a merged-checkpoint runner (run-local-aio-edit-proof.ps1) instead."
}
if (-not [string]::IsNullOrWhiteSpace($LoraName) -and $LoraStrength -le 0) {
    throw "LoRA '$LoraName' was requested without a positive strength; no database or script default is substituted. " +
          "Pass -LoraStrength explicitly."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot
$base = $ComfyUiUrl.TrimEnd('/')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$stats = Invoke-RestMethod -Uri "$base/system_stats" -TimeoutSec 30
Write-Host "Host: ComfyUI $($stats.system.comfyui_version), $($stats.devices[0].name), $([math]::Round($stats.devices[0].vram_free/1GB,2)) GB VRAM free"

$oi = Invoke-RestMethod -Uri "$base/object_info" -TimeoutSec 180
$known = $oi.PSObject.Properties.Name
foreach ($cls in @('UNETLoader','CLIPLoader','VAELoader','TextEncodeQwenImage21','QwenImage21Cache','KSampler','VAEDecode','SaveImage','LoadImage')) {
    if ($known -notcontains $cls) { throw "Host is missing node class '$cls' (Qwen-Image-2.1 native edit graph)." }
}
if (-not [string]::IsNullOrWhiteSpace($DepthControlImage)) {
    foreach ($cls in @('ModelPatchLoader','QwenImageDiffsynthControlnet')) {
        if ($known -notcontains $cls) { throw "Host is missing node class '$cls' (depth control graph)." }
    }
    $patches = $oi.ModelPatchLoader.input.required.name[0]
    if ($patches -notcontains $DepthPatchName) {
        throw "Host does not list model patch '$DepthPatchName'. Stage it in D:/ComfyUI/models/model_patches first."
    }
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
    $parsed = $body | ConvertFrom-Json
    if ($parsed.subfolder) { return "$($parsed.subfolder)/$($parsed.name)" }
    return $parsed.name
}

$sourceUpload = Upload-Image $SourceImage 'qwen21-edit-source.png'
Write-Host "Source uploaded: $sourceUpload"

# ---- graph: ComfyUIImageEditingClient.BuildQwenImage21EditWorkflow ---------------------------
# Reference sub-inputs are declared as flat DOTTED autogrow ids (`images.image_N`) - the only shape
# ComfyUI accepts. Flat `image_N` kwargs raise a TypeError inside execute() and a hand-built `images`
# dict is silently ignored (both ruled out on this host 2026-09-22), so a sloppy wiring would DROP
# every reference without failing.
$g = [ordered]@{}
$g['1'] = @{ class_type = 'LoadImage'; inputs = @{ image = $sourceUpload } }
$g['2'] = @{ class_type = 'UNETLoader'; inputs = @{ unet_name = $UnetName; weight_dtype = 'default' } }
$g['3'] = @{ class_type = 'CLIPLoader'; inputs = @{ clip_name = $ClipName; type = 'qwen_image'; device = 'default' } }
$g['4'] = @{ class_type = 'VAELoader'; inputs = @{ vae_name = $VaeName } }
$g['5'] = @{ class_type = 'QwenImage21Cache'; inputs = @{ model = @('2', 0); device = 'auto'; dtype = 'default' } }

$encodeInputs = @{
    clip = @('3', 0); vae = @('4', 0); prompt = $Instruction; negative_prompt = $Negative
    resolution = $Resolution; 'images.image_1' = @('1', 0)
}

$referenceCount = 0
$referencePaths = @()
if (-not [string]::IsNullOrWhiteSpace($AnchorImage)) { $referencePaths += $AnchorImage }
if ($References.Count -gt 0) { $referencePaths += $References }
foreach ($referencePath in $referencePaths) {
    if ([string]::IsNullOrWhiteSpace($referencePath)) { continue }
    $slot = $referenceCount + 2                       # image_1 is the source
    $nodeId = (20 + $referenceCount).ToString()
    $upload = Upload-Image $referencePath "qwen21-edit-reference-$($referenceCount + 1).png"
    $g[$nodeId] = @{ class_type = 'LoadImage'; inputs = @{ image = $upload } }
    $encodeInputs["images.image_$slot"] = @($nodeId, 0)
    $referenceCount++
    Write-Host "Reference $referenceCount uploaded (image_$slot): $upload"
}

$g['6'] = @{ class_type = 'TextEncodeQwenImage21'; inputs = $encodeInputs }
$g['7'] = @{ class_type = 'KSampler'; inputs = @{
        seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = $Denoise
        model = @('5', 0); positive = @('6', 0); negative = @('6', 1); latent_image = @('6', 2)
    } }
$g['8'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('7', 0); vae = @('4', 0) } }
$g['9'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('8', 0); filename_prefix = 'qwen21-edit' } }

# Optional editor LoRA, wired exactly as the app's 2.1 editor graph wires it: LoraLoader takes the
# model from QwenImage21Cache and the clip from CLIPLoader, and both the sampler branch and the
# encoder's clip input are re-pointed at it. (The app's GENERATION path uses LoraLoaderModelOnly
# because every 2.1 scene LoRA is a model-only file; the editor graph is model+clip. This runner
# mirrors the editor graph and does not reconcile the two.)
if (-not [string]::IsNullOrWhiteSpace($LoraName)) {
    $g['17'] = @{ class_type = 'LoraLoader'; inputs = @{
            lora_name = $LoraName; strength_model = $LoraStrength; strength_clip = $LoraStrength
            model = @('5', 0); clip = @('3', 0)
        } }
    $g['7'].inputs.model = @('17', 0)
    $encodeInputs.clip = @('17', 0)
    Write-Host "Editor LoRA: $LoraName @ $LoraStrength"
}

# Optional geometry control. Wired AFTER the LoRA so the patch wraps whatever the sampler will use,
# and it re-points only the sampler's model input: the text encoder's branch is untouched, because
# the control is geometry, not a second prompt channel.
if (-not [string]::IsNullOrWhiteSpace($DepthControlImage)) {
    $depthUpload = Upload-Image $DepthControlImage 'qwen21-depth-control.png'
    $g['30'] = @{ class_type = 'ModelPatchLoader'; inputs = @{ name = $DepthPatchName } }
    $g['31'] = @{ class_type = 'LoadImage'; inputs = @{ image = $depthUpload } }
    $modelIntoControl = $g['7'].inputs.model
    $g['32'] = @{ class_type = 'QwenImageDiffsynthControlnet'; inputs = @{
            model = $modelIntoControl; model_patch = @('30', 0); vae = @('4', 0)
            image = @('31', 0); strength = $DepthStrength
        } }
    $g['7'].inputs.model = @('32', 0)
    Write-Host "Depth control: $DepthPatchName @ strength $DepthStrength ($depthUpload)"
}

Write-Host ("Submitting edit: unet=$UnetName clip=$ClipName vae=$VaeName res=$Resolution " +
    "$Steps steps cfg $Cfg $Sampler/$Scheduler denoise $Denoise seed=$Seed references=$referenceCount")

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
