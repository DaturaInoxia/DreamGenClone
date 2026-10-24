<#
.SYNOPSIS
  Qwen-Image-2.1 plate-as-base placement proof: the PLATE is the sampler's starting latent and the
  person is painted inside an operator-defined region, with a body reference for identity.

.DESCRIPTION
  The two levers that were measured and ruled out for person-vs-room scale were prompt wording (adding
  the application's reference block or removing it did not fix the ratio) and the reference's own
  framing (scaling the figure inside the reference plate from 92% to 30% did not change the result).

  This is the remaining mechanism CASE-21 measured as CONTAINING a frame: VAEEncodeForInpaint over the
  source plate gives the sampler a latent that genuinely encodes the plate, so everything outside the
  region is the plate's own pixels (0.001% changed vs 52.7% for the encoder-latent route). The region
  then becomes the scale control - the person cannot exceed the hole made for them.

  Graph (mirrors ComfyUIImageEditingClient.BuildQwenImage21EditWorkflow's masked branch):
    UNETLoader / CLIPLoader / VAELoader
    LoadImage(plate)      -> images.image_1 of the encoder, and the inpaint encoder's pixels
    LoadImage(reference)  -> images.image_2 of the encoder
    LoadImageMask(mask, red channel)
    VAEEncodeForInpaint(plate, vae, mask, grow_mask_by=0) -> KSampler.latent_image
    TextEncodeQwenImage21(clip, vae, prompt, resolution, images.image_1, images.image_2)
    KSampler -> VAEDecode -> SaveImage

  No QwenImage21Cache node: the generation path emits none, and the sampler envelope here is the
  generation row's (30 / cfg 3 / er_sde / beta), so the output is comparable with the ten
  run-qwen21-composition-ab renders.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-plate-placement-proof.ps1 `
    -PlateImage 'artifacts/tmp/qwen21-composition-ab/plate.png' `
    -ReferenceImage 'DreamGenClone.Web/data/scene-images/identity/.../bc33196160f34a2a85d3c66c7961a9df.png' `
    -MaskImage 'artifacts/tmp/qwen21-composition-ab/references/placement-left.png'
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'http://192.168.0.10:8188',
    [Parameter(Mandatory = $true)][string]$PlateImage,
    [Parameter(Mandatory = $true)][string]$ReferenceImage,
    [Parameter(Mandatory = $true)][string]$MaskImage,
    [Parameter(Mandatory = $true)][string]$Instruction,
    [long]$Seed = 20261016,
    [int]$Steps = 30,
    [double]$Cfg = 3.0,
    [string]$Sampler = 'er_sde',
    [string]$Scheduler = 'beta',
    [int]$Resolution = 1024,
    [double]$Denoise = 1.0,
    [string]$UnetName = 'qwen_image_2.1_int8_convrot.safetensors',
    [string]$ClipName = 'qwen3vl_8b_int8_convrot.safetensors',
    [string]$VaeName = 'qwen_image_2.1_vae_bf16.safetensors',
    [string]$OutDir = 'artifacts/tmp/qwen21-plate-placement',
    [int]$TimeoutSeconds = 1200
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot
$base = $ComfyUiUrl.TrimEnd('/')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$stats = Invoke-RestMethod -Uri "$base/system_stats" -TimeoutSec 30
Write-Host "Host: ComfyUI $($stats.system.comfyui_version), $($stats.devices[0].name)"

$known = (Invoke-RestMethod -Uri "$base/object_info" -TimeoutSec 180).PSObject.Properties.Name
foreach ($cls in @('UNETLoader', 'CLIPLoader', 'VAELoader', 'LoadImage', 'LoadImageMask', 'VAEEncodeForInpaint',
        'TextEncodeQwenImage21', 'KSampler', 'VAEDecode', 'SaveImage')) {
    if ($known -notcontains $cls) { throw "Host is missing node class '$cls'." }
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

$plateUpload = Upload-Image $PlateImage 'placement-plate.png'
$referenceUpload = Upload-Image $ReferenceImage 'placement-reference.png'
$maskUpload = Upload-Image $MaskImage 'placement-mask.png'
Write-Host "Uploaded: plate=$plateUpload reference=$referenceUpload mask=$maskUpload"

$g = [ordered]@{}
$g['1'] = @{ class_type = 'UNETLoader'; inputs = @{ unet_name = $UnetName; weight_dtype = 'default' } }
$g['2'] = @{ class_type = 'CLIPLoader'; inputs = @{ clip_name = $ClipName; type = 'qwen_image'; device = 'default' } }
$g['3'] = @{ class_type = 'VAELoader'; inputs = @{ vae_name = $VaeName } }
$g['10'] = @{ class_type = 'LoadImage'; inputs = @{ image = $plateUpload } }
$g['11'] = @{ class_type = 'LoadImage'; inputs = @{ image = $referenceUpload } }
$g['12'] = @{ class_type = 'LoadImageMask'; inputs = @{ image = $maskUpload; channel = 'red' } }
$g['13'] = @{ class_type = 'VAEEncodeForInpaint'; inputs = @{
        pixels = @('10', 0); vae = @('3', 0); mask = @('12', 0); grow_mask_by = 0
    } }
$g['4'] = @{ class_type = 'TextEncodeQwenImage21'; inputs = @{
        clip = @('2', 0); vae = @('3', 0); prompt = $Instruction; negative_prompt = ''
        resolution = $Resolution; 'images.image_1' = @('10', 0); 'images.image_2' = @('11', 0)
    } }
$g['6'] = @{ class_type = 'KSampler'; inputs = @{
        seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = $Denoise
        model = @('1', 0); positive = @('4', 0); negative = @('4', 1); latent_image = @('13', 0)
    } }
$g['7'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('6', 0); vae = @('3', 0) } }
$g['8'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('7', 0); filename_prefix = 'qwen21-placement' } }

Write-Host "Submitting: plate as sampler base (VAEEncodeForInpaint) + reference, $Steps steps cfg $Cfg $Sampler/$Scheduler seed=$Seed"

$payload = @{ prompt = $g; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 32
$content = [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, 'application/json')
$resp = $client.PostAsync("$base/prompt", $content).Result
$respBody = $resp.Content.ReadAsStringAsync().Result
if (-not $resp.IsSuccessStatusCode) { throw "Submit failed: $($resp.StatusCode) $respBody" }
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
            throw "Workflow errored: $($entry.status.messages | ConvertTo-Json -Compress)"
        }
    }
}
if (-not $result) { throw "Timed out waiting for prompt $promptId" }

$saved = @()
foreach ($nodeId in $result.outputs.PSObject.Properties.Name) {
    $node = $result.outputs.$nodeId
    foreach ($img in @($node.images)) {
        if (-not $img) { continue }
        $url = "$base/view?filename=$($img.filename)&subfolder=$($img.subfolder)&type=$($img.type)"
        $dest = Join-Path $OutDir "placement-seed$Seed.png"
        Invoke-WebRequest -Uri $url -OutFile $dest -TimeoutSec 300
        $saved += $dest
        Write-Host "saved: $dest"
    }
}
if ($saved.Count -eq 0) { throw "Workflow completed but produced no image." }

$g | ConvertTo-Json -Depth 32 | Set-Content -Path (Join-Path $OutDir "request-seed$Seed.json") -Encoding UTF8
$meta = [ordered]@{
    plate      = $PlateImage; plateSha256 = (Get-FileHash $PlateImage -Algorithm SHA256).Hash
    reference  = $ReferenceImage; referenceSha256 = (Get-FileHash $ReferenceImage -Algorithm SHA256).Hash
    mask       = $MaskImage; maskSha256 = (Get-FileHash $MaskImage -Algorithm SHA256).Hash
    seed = $Seed; steps = $Steps; cfg = $Cfg; sampler = $Sampler; scheduler = $Scheduler; denoise = $Denoise
    resolution = $Resolution; promptId = $promptId; instruction = $Instruction
    output = @($saved | ForEach-Object { (Get-FileHash $_ -Algorithm SHA256).Hash })
    createdUtc = (Get-Date).ToUniversalTime().ToString('o')
}
$meta | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $OutDir "meta-seed$Seed.json") -Encoding UTF8
Write-Host "DONE: $($saved -join ', ')"
