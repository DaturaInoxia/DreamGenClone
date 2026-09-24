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
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    [string]$SourceImage = 'specs/image-generator-tests/qwen/images/base.png',
    # Raw edit instruction. Required unless -IdentityCharacters builds one (see below).
    [string]$Instruction = '',
    # Optional negative prompt. Empty by default, which reproduces the app graph exactly
    # (ComfyUIImageEditingClient hardcodes an empty negative encode). Only meaningful when CFG > 1.
    [string]$Negative = '',
    # Optional LoRA applied to the merged checkpoint (wired between checkpoint and sampler).
    # Empty by default, which reproduces the app graph exactly (it has no LoRA node).
    [string]$LoraName = '',
    [double]$LoraStrength = 0.9,
    # UI-aligned identity pass. Path to a JSON array of bindings in the SAME shape the app persists
    # on SceneImageRecord.IdentityReferenceBindingsJson and the handler reads back:
    #   [{ "ordinal": 1, "characterName": "Becky", "fileRelativePath": "identity/<profile>/<asset>.png", "sha256": "..." }]
    # Ordinals must be 1..N contiguous (the handler rejects unordered bindings). fileRelativePath is
    # resolved under -IdentityStorageRoot, exactly as the app's identity storage does. When supplied,
    # the app's identity instruction (SceneImageService.BuildFaceOnlyIdentityInstruction) is built and
    # used, so the proof sends what the Studio's "apply identity" action sends.
    [string]$IdentityBindingsPath = '',
    [string]$IdentityStorageRoot = 'DreamGenClone.Web/data/scene-images',
    # Single-reference convenience form, equivalent to one binding. Used by the older chained harnesses.
    [string]$AnchorImage = '',
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

# --- resolve the reference list and the instruction (one source, no ambiguity) ---------
$referenceFiles = @()
$identityCharacters = @()
$identityLocators = @()
$identitySubjects = @()

if ($IdentityBindingsPath -and $AnchorImage) {
    throw "Pass either -IdentityBindingsPath or -AnchorImage, not both."
}

if ($IdentityBindingsPath) {
    if (-not (Test-Path $IdentityBindingsPath)) { throw "Identity bindings file '$IdentityBindingsPath' was not found." }
    # Note: do NOT wrap ConvertFrom-Json in @() on PowerShell 5.1 - it nests the array as a single
    # element (same trap as the image manifests). foreach enumerates the returned array correctly.
    $parsedBindings = Get-Content -Raw $IdentityBindingsPath | ConvertFrom-Json
    $bindings = @()
    foreach ($parsedBinding in $parsedBindings) { $bindings += $parsedBinding }
    if ($bindings.Count -eq 0) { throw "Identity bindings file '$IdentityBindingsPath' contains no bindings." }
    $ordered = @($bindings | Sort-Object { [int]$_.ordinal })
    for ($i = 0; $i -lt $ordered.Count; $i++) {
        if ([int]$ordered[$i].ordinal -ne ($i + 1)) {
            throw "Identity bindings must be ordinally ordered 1..N with no gaps (got $($ordered[$i].ordinal) at position $($i + 1))."
        }
        if ([string]::IsNullOrWhiteSpace($ordered[$i].characterName)) { throw "Binding $($ordered[$i].ordinal) is missing characterName." }
        if ([string]::IsNullOrWhiteSpace($ordered[$i].fileRelativePath)) { throw "Binding $($ordered[$i].ordinal) is missing fileRelativePath." }
        $fullPath = Join-Path $IdentityStorageRoot $ordered[$i].fileRelativePath
        if (-not (Test-Path $fullPath)) { throw "Binding $($ordered[$i].ordinal) asset not found: $fullPath" }
        if ($ordered[$i].sha256) {
            $actual = (Get-FileHash -Algorithm SHA256 -Path $fullPath).Hash
            if ($actual -ne ([string]$ordered[$i].sha256).ToUpperInvariant()) {
                throw "Binding $($ordered[$i].ordinal) sha256 mismatch for $fullPath`n  expected $($ordered[$i].sha256)`n  actual   $actual"
            }
        }
        $referenceFiles += $fullPath
        $identityCharacters += ([string]$ordered[$i].characterName).Trim()
        # visibleLocator is the app's per-face AREA ("man on the left facing right"). Its presence
        # selects the editor identity instruction below, exactly as EnqueueEditorIdentityAsync does.
        if ([string]::IsNullOrWhiteSpace($ordered[$i].visibleLocator)) { $identityLocators += '' }
        else { $identityLocators += ([string]$ordered[$i].visibleLocator).Trim() }
        # SubjectLabel is the target key when the compiler supplied one, else the character name.
        if ([string]::IsNullOrWhiteSpace($ordered[$i].targetKey)) { $identitySubjects += ([string]$ordered[$i].characterName).Trim() }
        else { $identitySubjects += ([string]$ordered[$i].targetKey).Trim() }
        $referenceView = if ($ordered[$i].faceView) { " ($($ordered[$i].faceView))" } else { '' }
        Write-Host ("  binding $($i + 1): $($identityCharacters[$i])$referenceView -> $($ordered[$i].fileRelativePath)")
    }

    if ($Instruction) { throw "Pass either -Instruction or -IdentityBindingsPath, not both." }

    $withLocator = @($identityLocators | Where-Object { $_ }).Count
    if ($withLocator -ne 0 -and $withLocator -ne $identityLocators.Count) {
        throw "Bindings must either ALL carry visibleLocator (editor identity path) or NONE (face-only path); got $withLocator of $($identityLocators.Count)."
    }

    if ($withLocator -gt 0) {
        # Mirrors SceneImageService.BuildEditorIdentityInstruction - the app's EDITOR identity path
        # (EnqueueEditorIdentityAsync), which is the one that carries a per-face area. It is the
        # proven short, face-primary "Picture N" recipe; the reference image is the caller's chosen
        # approved asset (so an angle-matched view can be supplied) rather than the pack canonical.
        $mappingParts = @()
        for ($i = 0; $i -lt $identitySubjects.Count; $i++) {
            $mappingParts += "Apply the face of the person shown in Picture $($i + 2) to the $($identitySubjects[$i]) at $($identityLocators[$i]). Keep that person's facial identity consistent with Picture $($i + 2) for the entire image; do not change anyone else."
        }
        $facesWord = if ($identitySubjects.Count -gt 1) { 'faces' } else { 'face' }
        $Instruction = ($mappingParts -join ' ') + " Keep the pose, bodies, position, clothing, lighting, and everything else in the image exactly unchanged except the selected $facesWord."
    }
    else {
        # Mirrors SceneImageService.BuildFaceOnlyIdentityInstruction - the long, feature-explicit
        # form persisted by EnqueueIdentityAsync (no per-face area; canonical pack asset).
        # Ordinal is 1-based and the template prints ordinal+1 because image 1 is the scene source.
        $identityNames = ((@($identityCharacters) | Where-Object { $_ } | Select-Object -Unique) -join ', ')
        $mappingParts = @()
        for ($i = 0; $i -lt $identityCharacters.Count; $i++) {
            $mappingParts += "Reference image $($i + 2) is the approved face identity reference for $($identityCharacters[$i]) and applies only to that character's face in image 1."
        }
        $Instruction = "Identity correction only for the selected character faces: $identityNames. " +
            "Image 1 is the existing scene and must remain the base image. " +
            (($mappingParts -join ' ') + ' ') +
            "The additional approved face images are identity references only, not replacement images or composition sources. " +
            "Transfer the approved reference identity into the matching face region: preserve and reproduce the reference's distinguishing facial geometry, eye color and shape, eyebrows, nose, lips, freckles, complexion markers, and hairline-adjacent facial details, adapted to the existing face's scale, angle, expression, and lighting. " +
            "Use the reference only to correct face-local identity details for those selected characters. " +
            "Treat the existing scene's visible neck and body skin tone as authoritative: harmonize the corrected face skin tone, undertone, exposure, and shading with that body under the existing scene lighting, without importing a mismatched complexion from the reference. " +
            "Preserve everything outside those selected face regions exactly: every person and unselected face, bodies, poses, hands, clothing, accessories, expression, action, scene geometry, framing, camera, crop, background, objects, lighting, color, and composition. " +
            "Do not copy the reference image framing, background, body, pose, clothing, or lighting. " +
            "Do not add, remove, move, restyle, or otherwise alter anything outside the selected character face regions."
    }
}
elseif ($AnchorImage) {
    if (-not (Test-Path $AnchorImage)) { throw "Anchor image '$AnchorImage' was not found." }
    $referenceFiles = @($AnchorImage)
}

if ([string]::IsNullOrWhiteSpace($Instruction)) {
    throw "An instruction is required: pass -Instruction, or -IdentityBindingsPath."
}

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

    # --- ordered reference images (image2, image3, ... exactly as the app wires them) ---
    $referenceUploaded = @()
    for ($index = 0; $index -lt $referenceFiles.Count; $index++) {
        $referencePath = $referenceFiles[$index]
        if (-not (Test-Path $referencePath)) { throw "Reference image '$referencePath' was not found." }
        $referenceName = [System.IO.Path]::GetFileName($referencePath)
        $referenceUploadName = "proof-ref$($index + 1)-" + $referenceName
        $referenceBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $referencePath))
        $referenceMultipart = New-Object System.Net.Http.MultipartFormDataContent
        $referenceContent = New-Object System.Net.Http.ByteArrayContent (,$referenceBytes)
        $referenceContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('image/png')
        $referenceMultipart.Add($referenceContent, 'image', $referenceUploadName)
        $referenceMultipart.Add((New-Object System.Net.Http.StringContent 'true'), 'overwrite')
        $referenceResponse = $client.PostAsync("$base/upload/image", $referenceMultipart).Result
        if (-not $referenceResponse.IsSuccessStatusCode) {
            throw "Reference $($index + 1) upload failed: $($referenceResponse.StatusCode) $($referenceResponse.Content.ReadAsStringAsync().Result)"
        }
        $referenceUploaded += ($referenceResponse.Content.ReadAsStringAsync().Result | ConvertFrom-Json).name
        "reference $($index + 1) uploaded: $($referenceUploaded[$index])"
    }

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
    '7'  = @{ class_type = 'TextEncodeQwenImageEditPlus';             inputs = @{ clip = @('16', 1); vae = @('16', 2); image1 = @('2', 0); prompt = $Negative } }
    '8'  = @{ class_type = 'VAEEncode';                               inputs = @{ pixels = @('2', 0); vae = @('16', 2) } }
    '9'  = @{ class_type = 'SaveImage';                               inputs = @{ images = @('15', 0); filename_prefix = 'dreamgen_app/qwen-edit-local' } }
    '12' = @{ class_type = 'FluxKontextMultiReferenceLatentMethod';   inputs = @{ conditioning = @('6', 0); reference_latents_method = 'index_timestep_zero' } }
    '13' = @{ class_type = 'FluxKontextMultiReferenceLatentMethod';   inputs = @{ conditioning = @('7', 0); reference_latents_method = 'index_timestep_zero' } }
    '14' = @{ class_type = 'CFGNorm';                                 inputs = @{ model = @('5', 0); strength = $CfgNormStrength } }
    '15' = @{ class_type = 'VAEDecode';                               inputs = @{ samples = @('3', 0); vae = @('16', 2) } }
    '16' = @{ class_type = 'CheckpointLoaderSimple';                  inputs = @{ ckpt_name = $Checkpoint } }
}

# Ordered references: reference i becomes image(i+2) on both text encodes via LoadImage node
# (20+i), matching ComfyUIImageEditingClient.AddReferenceInputs/AddReferenceLoaders.
for ($index = 0; $index -lt $referenceUploaded.Count; $index++) {
    $referenceNodeId = (20 + $index).ToString()
    $referenceSlot = "image$($index + 2)"
    $workflow[$referenceNodeId] = @{ class_type = 'LoadImage'; inputs = @{ image = $referenceUploaded[$index] } }
    $workflow['6'].inputs[$referenceSlot] = @($referenceNodeId, 0)
    $workflow['7'].inputs[$referenceSlot] = @($referenceNodeId, 0)
}

# Optional LoRA: insert LoraLoader between the checkpoint and everything downstream
# (the sampler model branch AND the text-encode clip branch, which is why both are rewired).
if ($LoraName) {
    $workflow['17'] = @{ class_type = 'LoraLoader'; inputs = @{
        model = @('16', 0); clip = @('16', 1); lora_name = $LoraName; strength_model = $LoraStrength; strength_clip = $LoraStrength } }
    $workflow['5'].inputs.model = @('17', 0)
    $workflow['6'].inputs.clip  = @('17', 1)
    $workflow['7'].inputs.clip  = @('17', 1)
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
# $OutDir may already be ABSOLUTE (the chained harnesses pass an absolute staging directory). Join-Path
# concatenates rather than resolving, so joining a rooted child produces an unopenable "D:\a\D:\b" path
# and the .NET call below dies with "The given path's format is not supported." That is what silently
# killed steps in earlier chained runs (empty _staging-stepN folders, no manifest). Resolve only when the
# path is still relative.
if (-not [System.IO.Path]::IsPathRooted($outPath)) { $outPath = Join-Path (Get-Location) $outPath }
[System.IO.File]::WriteAllBytes($outPath, $viewResponse.Content.ReadAsByteArrayAsync().Result)

# The full prompt, as required whenever a generation runs.
'--- PROMPT SENT TO THE MODEL -------------------------------------------------'
"instruction: $Instruction"
"negative   : $(if ([string]::IsNullOrWhiteSpace($Negative)) { '(empty)' } else { $Negative })"
"checkpoint : $Checkpoint$(if ($LoraName) { "  + LoRA $LoraName @ $LoraStrength" } else { '' })"
"sampler    : $Sampler / $Scheduler, $Steps steps, CFG $Cfg, denoise $Denoise, AuraFlow shift $AuraFlowShift, CFGNorm $CfgNormStrength, seed $Seed"
"graph      : CheckpointLoaderSimple merged checkpoint (ImageEditorGraphKind.MergedCheckpoint)"
'-----------------------------------------------------------------------------'
"OUTPUT: $outPath"
