<#
.SYNOPSIS
  Qwen-Image-2.1 composition A/B: identical graph, seed and references. Only the PROMPT differs.

.DESCRIPTION
  Isolates whether the application-authored reference block changes placement and scale, or whether
  the character reference itself is what breaks them.

  Reproduces ComfyUIImageClient.BuildQwenImage21Workflow node for node: UNETLoader -> CLIPLoader ->
  VAELoader -> TextEncodeQwenImage21 (references as flat dotted autogrow ids `images.image_N`, VAE
  wired ONLY when references exist) -> EmptyLatentImage -> KSampler -> VAEDecode -> SaveImage. No
  LoRA node and no QwenImage21Cache: the generation path emits neither.

  Cells (prompt files live in <OutRoot>/prompts):
    A1  LOCATION reference only; the character exists only as text. The render the operator reports
        works - the control.
    B2  BODY reference (ordinal 1) + LOCATION reference (ordinal 2), carrying the application's full
        authoring block VERBATIM as recorded for SceneImages 6e7ffb21 (NativeReferenceRenderSubmitted).
    C2  The same two references in the same order, with only the vendor-shaped naming lines (each
        image named and given its role) and none of the application's extra sentences.

  Seed, size, steps, cfg, sampler, scheduler and the reference resolution budget are identical across
  cells, so a difference in the output is attributable to the prompt text alone.

  Both reference files are SHA-256 verified against the two approved assets the application sent, so a
  substitute image cannot silently invalidate the result.

.NOTES
  Measurement harness only. It changes no product behaviour and writes nothing outside <OutRoot>
  (git-ignored).

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-composition-ab.ps1
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'http://192.168.0.10:8188',
    [long]$Seed = 20261016,
    [int]$Width = 1024,
    [int]$Height = 1024,
    [int]$Steps = 30,
    [double]$Cfg = 3.0,
    [string]$Sampler = 'er_sde',
    [string]$Scheduler = 'beta',
    [int]$Resolution = 1024,
    [string]$UnetName = 'qwen_image_2.1_int8_convrot.safetensors',
    [string]$ClipName = 'qwen3vl_8b_int8_convrot.safetensors',
    [string]$VaeName = 'qwen_image_2.1_vae_bf16.safetensors',
    [string]$OutRoot = 'artifacts/tmp/qwen21-composition-ab',
    [string]$BodyImage = 'DreamGenClone.Web/data/scene-images/identity/de351eb3-69d3-421a-a762-79ae8ee183ed/bc33196160f34a2a85d3c66c7961a9df.png',
    [string]$BodySha256 = '4E3F9B29EF06A00A1735D573DA8F50D802D9BF3966B1C9CDF5A2A07C328D0913',
    [string]$LocationImage = 'DreamGenClone.Web/data/scene-images/assets/f733b1e7e19a43d191c56f5761edccbd.png',
    [string]$LocationSha256 = 'A3AE79BD050B7720BCEDF32E3025B0F3148C232866D25B727CC325FC28633128',
    # A closer-framed PERSON reference (the pack's frontal face crop, 780x1024) - used to separate
    # "a person reference is present" from "the body plate's full-figure framing is present".
    [string]$FaceImage = 'DreamGenClone.Web/data/scene-images/identity/de351eb3-69d3-421a-a762-79ae8ee183ed/282f5b91ba7d4dba85140ae006aa06ee.png',
    [string]$FaceSha256 = '',
    # The approved wardrobe asset a wardrobe binding actually sends (SceneAssetImages fd8413fe...): a flat-lay of a
    # white tank top and frayed jean shorts.
    [string]$WardrobeImage = 'DreamGenClone.Web/data/scene-images/assets/fd8413feb4a24057b32bf3dcd583c11b.png',
    [string]$WardrobeSha256 = '',
    [string[]]$Cells = @('A1', 'B2', 'C2'),
    [int]$TimeoutSeconds = 1200
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot
$base = $ComfyUiUrl.TrimEnd('/')
$promptRoot = Join-Path $OutRoot 'prompts'

# ---- the two approved assets the application actually sent --------------------------------
foreach ($pair in @(@($BodyImage, $BodySha256), @($LocationImage, $LocationSha256))) {
    if (-not (Test-Path $pair[0])) { throw "Reference image not found: $($pair[0])" }
    $actual = (Get-FileHash $pair[0] -Algorithm SHA256).Hash
    if ($actual -ne $pair[1]) {
        throw "Reference '$($pair[0])' is sha256 $actual, expected $($pair[1]). The A/B would not be the render under test."
    }
}
if (-not [string]::IsNullOrWhiteSpace($FaceSha256)) {
    if (-not (Test-Path $FaceImage)) { throw "Face reference not found: $FaceImage" }
    $faceActual = (Get-FileHash $FaceImage -Algorithm SHA256).Hash
    if ($faceActual -ne $FaceSha256) { throw "Face reference '$FaceImage' is sha256 $faceActual, expected $FaceSha256." }
}

# ---- preflight ----------------------------------------------------------------------------
$stats = Invoke-RestMethod -Uri "$base/system_stats" -TimeoutSec 30
Write-Host "Host: ComfyUI $($stats.system.comfyui_version), $($stats.devices[0].name), $([math]::Round($stats.devices[0].vram_free/1GB,2)) GB VRAM free"

$known = (Invoke-RestMethod -Uri "$base/object_info" -TimeoutSec 180).PSObject.Properties.Name
foreach ($cls in @('UNETLoader', 'CLIPLoader', 'VAELoader', 'TextEncodeQwenImage21', 'EmptyLatentImage', 'KSampler', 'VAEDecode', 'SaveImage')) {
    if ($known -notcontains $cls) { throw "Host is missing node class '$cls' (Qwen-Image-2.1 native graph)." }
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

$bodyUpload = Upload-Image $BodyImage 'ab-body.png'
$locationUpload = Upload-Image $LocationImage 'ab-location.png'
$faceUpload = $null
if (-not [string]::IsNullOrWhiteSpace($FaceSha256)) { $faceUpload = Upload-Image $FaceImage 'ab-face.png' }
$wardrobeUpload = $null
if (-not [string]::IsNullOrWhiteSpace($WardrobeSha256)) { $wardrobeUpload = Upload-Image $WardrobeImage 'ab-wardrobe.png' }
Write-Host "Uploaded: body=$bodyUpload  location=$locationUpload face=$faceUpload wardrobe=$wardrobeUpload"

# ---- cells: reference order matches the application's ordinals (1 = body, 2 = location) ----
$definitions = @{
    'A1' = @{ Label = 'location reference only (character is text)'; Refs = @($locationUpload); Shas = @($LocationSha256) }
    'B2' = @{ Label = 'body + location, application block verbatim'; Refs = @($bodyUpload, $locationUpload); Shas = @($BodySha256, $LocationSha256) }
    'C2' = @{ Label = 'body + location, minimal naming only'; Refs = @($bodyUpload, $locationUpload); Shas = @($BodySha256, $LocationSha256) }
    'A3' = @{ Label = 'location + FACE reference (closer-framed person)'; Refs = @($faceUpload, $locationUpload); Shas = @($FaceSha256, $LocationSha256) }
    'W1' = @{ Label = 'face + wardrobe + location, NO full-body plate'; Refs = @($faceUpload, $wardrobeUpload, $locationUpload); Shas = @($FaceSha256, $WardrobeSha256, $LocationSha256) }
    'W2' = @{ Label = 'WARDROBE FIRST, then body, then location'; Refs = @($wardrobeUpload, $bodyUpload, $locationUpload); Shas = @($WardrobeSha256, $BodySha256, $LocationSha256) }
    'W3' = @{ Label = 'body + wardrobe + location (body asset supplied by the caller)'; Refs = @($bodyUpload, $wardrobeUpload, $locationUpload); Shas = @($BodySha256, $WardrobeSha256, $LocationSha256) }
}

$summary = New-Object System.Collections.Generic.List[object]

foreach ($cell in $Cells) {
    if (-not $definitions.ContainsKey($cell)) { throw "Unknown cell '$cell'." }
    $def = $definitions[$cell]
    $promptFile = Get-ChildItem -Path $promptRoot -Filter "$cell-*.txt" | Select-Object -First 1
    if (-not $promptFile) { throw "No prompt file '$cell-*.txt' in $promptRoot" }
    $prompt = [System.IO.File]::ReadAllText($promptFile.FullName, [System.Text.Encoding]::UTF8).TrimEnd()
    $outDir = Join-Path $OutRoot $cell
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    # ---- graph: ComfyUIImageClient.BuildQwenImage21Workflow --------------------------------
    $enc = @{
        clip            = @('2', 0)
        prompt          = $prompt
        negative_prompt = ''
        resolution      = $Resolution
    }
    $g = [ordered]@{}
    $g['1'] = @{ class_type = 'UNETLoader'; inputs = @{ unet_name = $UnetName; weight_dtype = 'default' } }
    $g['2'] = @{ class_type = 'CLIPLoader'; inputs = @{ clip_name = $ClipName; type = 'qwen_image'; device = 'default' } }
    $g['3'] = @{ class_type = 'VAELoader'; inputs = @{ vae_name = $VaeName } }
    $g['5'] = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $Width; height = $Height; batch_size = 1 } }
    $g['6'] = @{ class_type = 'KSampler'; inputs = @{
            seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = 1.0
            model = @('1', 0); positive = @('4', 0); negative = @('4', 1); latent_image = @('5', 0)
        } }
    $g['7'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('6', 0); vae = @('3', 0) } }
    $g['8'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('7', 0); filename_prefix = "ab-$cell" } }

    if ($def.Refs.Count -gt 0) {
        # The VAE turns each reference into a latent that rides the conditioning, so it is wired ONLY
        # when references exist - the published text-to-image template leaves it off.
        $enc['vae'] = @('3', 0)
        for ($i = 0; $i -lt $def.Refs.Count; $i++) {
            $nodeId = (20 + $i).ToString()
            $g[$nodeId] = @{ class_type = 'LoadImage'; inputs = @{ image = $def.Refs[$i] } }
            $enc["images.image_$($i + 1)"] = @($nodeId, 0)
        }
    }
    $g['4'] = @{ class_type = 'TextEncodeQwenImage21'; inputs = $enc }

    Write-Host ""
    Write-Host "=== $cell  $($def.Label) ==="
    Write-Host "prompt: $($promptFile.Name)  $($prompt.Length) chars  references=$($def.Refs.Count)  seed=$Seed"

    $g | ConvertTo-Json -Depth 32 | Set-Content -Path (Join-Path $outDir 'request.json') -Encoding UTF8
    $prompt | Set-Content -Path (Join-Path $outDir 'prompt.txt') -Encoding UTF8

    $payload = @{ prompt = $g; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 32
    $content = [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, 'application/json')
    $resp = $client.PostAsync("$base/prompt", $content).Result
    $respBody = $resp.Content.ReadAsStringAsync().Result
    if (-not $resp.IsSuccessStatusCode) { throw "Submit of '$cell' failed: $($resp.StatusCode) $respBody" }
    $promptId = ($respBody | ConvertFrom-Json).prompt_id
    Write-Host "submitted prompt_id=$promptId; waiting (timeout ${TimeoutSeconds}s)..."

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $result = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        $h = Invoke-RestMethod -Uri "$base/history/$promptId" -TimeoutSec 60
        if ($h.PSObject.Properties[$promptId]) {
            $entry = $h.$promptId
            if ($entry.status.completed) { $result = $entry; break }
            if ($entry.status.status_str -eq 'error') {
                throw "Cell '$cell' errored: $($entry.status.messages | ConvertTo-Json -Compress)"
            }
        }
    }
    if (-not $result) { throw "Timed out waiting for cell '$cell' (prompt $promptId)" }

    $saved = @()
    foreach ($nodeId in $result.outputs.PSObject.Properties.Name) {
        $node = $result.outputs.$nodeId
        foreach ($img in @($node.images)) {
            if (-not $img) { continue }
            $url = "$base/view?filename=$($img.filename)&subfolder=$($img.subfolder)&type=$($img.type)"
            $dest = Join-Path $outDir "$cell.png"
            Invoke-WebRequest -Uri $url -OutFile $dest -TimeoutSec 300
            $saved += $dest
            Write-Host "saved: $dest"
        }
    }
    if ($saved.Count -eq 0) { throw "Cell '$cell' completed but produced no image." }

    $meta = [ordered]@{
        cell             = $cell
        label            = $def.Label
        promptFile       = $promptFile.Name
        promptChars      = $prompt.Length
        promptSha256     = (Get-FileHash (Join-Path $outDir 'prompt.txt') -Algorithm SHA256).Hash
        references       = @($def.Refs)
        referenceShas    = @($def.Shas)
        seed             = $Seed
        width            = $Width
        height           = $Height
        steps            = $Steps
        cfg              = $Cfg
        sampler          = $Sampler
        scheduler        = $Scheduler
        resolutionBudget = $Resolution
        unet             = $UnetName
        clip             = $ClipName
        vae              = $VaeName
        promptId         = $promptId
        output           = @($saved | ForEach-Object { (Get-FileHash $_ -Algorithm SHA256).Hash })
        createdUtc       = (Get-Date).ToUniversalTime().ToString('o')
    }
    $meta | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $outDir 'meta.json') -Encoding UTF8
    $summary.Add([pscustomobject]@{ Cell = $cell; Label = $def.Label; Refs = $def.Refs.Count; Chars = $prompt.Length; Output = (Split-Path $saved[0] -Leaf) })
}

Write-Host ""
Write-Host "==== SUMMARY ===="
$summary | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Outputs: $OutRoot"
