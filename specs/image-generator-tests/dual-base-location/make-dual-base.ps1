<#
.SYNOPSIS
  Generates two T2I base images with identical location conditioning but different character orientations.

.DESCRIPTION
  Takes a location reference image + two character pose configurations and produces two renders:
  - Base A: Character A (Dean) + Character B (Becky) in one orientation (e.g., facing each other)
  - Base B: Same characters + location, but Becky in the opposite profile (facing away)

  Both use img2img from the location reference (high denoise) + regional IP-Adapter for faces only.
  The location reference is VAE-encoded and used as the starting latent with high denoise,
  allowing the model to compose characters into the scene while preserving the location's
  visual character (lighting, composition, textures).

  This is the core primitive for the app's scene image pipeline when locations are arbitrary assets.

  Run from the repo root. PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    [Parameter(Mandatory = $true)][string]$LocationRef,
    [Parameter(Mandatory = $true)][string]$DeanRefA,
    [Parameter(Mandatory = $true)][string]$BeckyRefA,
    [Parameter(Mandatory = $true)][string]$DeanRefB,
    [Parameter(Mandatory = $true)][string]$BeckyRefB,
    [string]$DeanMask = 'specs/image-generator-tests/identity-two-character/masks/c6_left.png',
    [string]$BeckyMask = 'specs/image-generator-tests/identity-two-character/masks/c6_right.png',
    [Parameter(Mandatory = $true)][string]$PromptA,
    [Parameter(Mandatory = $true)][string]$PromptB,
    [string]$Negative = 'deformed, bad anatomy, extra limbs, text, watermark, low quality',
    [long]$Seed = 6601,
    [int]$Width = 1216,
    [int]$Height = 832,
    [string]$Checkpoint = 'bigLust_v16.safetensors',
    [int]$Steps = 30,
    [double]$Cfg = 5.0,
    [string]$Sampler = 'dpmpp_2m_sde',
    [string]$Scheduler = 'karras',
    [double]$Denoise = 0.75,
    [double]$DeanWeight = 0.8,
    [double]$BeckyWeight = 0.6,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Prefix = 'dualbase'
)

$ErrorActionPreference = 'Stop'
$base = $ComfyUiUrl.TrimEnd('/')

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(600)

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

function Upload-Image($url, $filePath, $uploadName) {
    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $filePath))
    $multipart = New-Object System.Net.Http.MultipartFormDataContent
    $imageContent = New-Object System.Net.Http.ByteArrayContent (,$bytes)
    $imageContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('image/png')
    $multipart.Add($imageContent, 'image', $uploadName)
    $multipart.Add((New-Object System.Net.Http.StringContent 'true'), 'overwrite')
    $resp = $client.PostAsync("$url/upload/image", $multipart).Result
    if (-not $resp.IsSuccessStatusCode) {
        throw "Upload failed: $($resp.StatusCode) $($resp.Content.ReadAsStringAsync().Result)"
    }
    return ($resp.Content.ReadAsStringAsync().Result | ConvertFrom-Json).name
}

# Upload all assets
$locName = Upload-Image $base $LocationRef "locref.png"
$deanAName = Upload-Image $base $DeanRefA "dean_a.png"
$beckyAName = Upload-Image $base $BeckyRefA "becky_a.png"
$deanBName = Upload-Image $base $DeanRefB "dean_b.png"
$beckyBName = Upload-Image $base $BeckyRefB "becky_b.png"
$deanMaskName = Upload-Image $base $DeanMask "dean_mask.png"
$beckyMaskName = Upload-Image $base $BeckyMask "becky_mask.png"

"uploaded location ref: $locName"
"uploaded dean A: $deanAName, becky A: $beckyAName"
"uploaded dean B: $deanBName, becky B: $beckyBName"

# Build img2img + regional IP-Adapter workflow
# Pattern: VAEEncode location ref → KSampler (high denoise) + regional face IP-Adapters
function Build-DualWorkflow($locImg, $deanImg, $beckyImg, $prompt, $deanW, $beckyW) {
    return @{
        '4'  = @{ class_type = 'CheckpointLoaderSimple'; inputs = @{ ckpt_name = $Checkpoint } }
        '5'  = @{ class_type = 'VAEEncode'; inputs = @{ pixels = @('11', 0); vae = @('4', 2) } }
        '6'  = @{ class_type = 'CLIPTextEncode'; inputs = @{ text = $prompt; clip = @('4', 1) } }
        '7'  = @{ class_type = 'CLIPTextEncode'; inputs = @{ text = $Negative; clip = @('4', 1) } }
        '10' = @{ class_type = 'IPAdapterUnifiedLoader'; inputs = @{ model = @('4', 0); preset = 'PLUS FACE (portraits)' } }
        # Location reference (VAE-encoded, used as starting latent)
        '11' = @{ class_type = 'LoadImage'; inputs = @{ image = $locImg } }
        # Face IP-Adapters (regional, masked)
        '13' = @{ class_type = 'LoadImage'; inputs = @{ image = $deanImg } }
        '14' = @{ class_type = 'LoadImage'; inputs = @{ image = $beckyImg } }
        '15' = @{ class_type = 'LoadImageMask'; inputs = @{ image = $deanMaskName; channel = 'red' } }
        '16' = @{ class_type = 'LoadImageMask'; inputs = @{ image = $beckyMaskName; channel = 'red' } }
        '20' = @{ class_type = 'IPAdapter'; inputs = @{
            model = @('10', 0); ipadapter = @('10', 1); image = @('13', 0)
            weight = $deanW; weight_type = 'standard'; start_at = 0.0; end_at = 1.0; attn_mask = @('15', 0)
        }}
        '21' = @{ class_type = 'IPAdapter'; inputs = @{
            model = @('20', 0); ipadapter = @('10', 1); image = @('14', 0)
            weight = $beckyW; weight_type = 'standard'; start_at = 0.0; end_at = 1.0; attn_mask = @('16', 0)
        }}
        '3'  = @{ class_type = 'KSampler'; inputs = @{
            seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler
            denoise = $Denoise; model = @('21', 0); positive = @('6', 0); negative = @('7', 0); latent_image = @('5', 0)
        }}
        '8'  = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('3', 0); vae = @('4', 2) } }
        '9'  = @{ class_type = 'SaveImage'; inputs = @{ images = @('8', 0); filename_prefix = $Prefix } }
    }
}

# Generate Base A
$wfA = Build-DualWorkflow $locName $deanAName $beckyAName $PromptA $DeanWeight $BeckyWeight
$payloadA = @{ prompt = $wfA; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 12
$contentA = New-Object System.Net.Http.StringContent $payloadA
$contentA.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/json')
$queuedA = ($client.PostAsync("$base/prompt", $contentA).Result.Content.ReadAsStringAsync().Result | ConvertFrom-Json)
$promptIdA = $queuedA.prompt_id
"queued base A: $promptIdA"

# Generate Base B
$wfB = Build-DualWorkflow $locName $deanBName $beckyBName $PromptB $DeanWeight $BeckyWeight
$payloadB = @{ prompt = $wfB; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 12
$contentB = New-Object System.Net.Http.StringContent $payloadB
$contentB.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/json')
$queuedB = ($client.PostAsync("$base/prompt", $contentB).Result.Content.ReadAsStringAsync().Result | ConvertFrom-Json)
$promptIdB = $queuedB.prompt_id
"queued base B: $promptIdB"

# Poll both
function Wait-ForRender($promptId) {
    $deadline = (Get-Date).AddSeconds(900)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $history = ($client.GetStringAsync("$base/history/$promptId").Result | ConvertFrom-Json)
        if ($history.PSObject.Properties.Name -contains $promptId -and $history.$promptId.outputs) {
            return $history.$promptId
        }
    }
    throw "Render $promptId did not complete."
}

$entryA = Wait-ForRender $promptIdA
$entryB = Wait-ForRender $promptIdB

$imgA = $entryA.outputs.'9'.images[0]
$imgB = $entryB.outputs.'9'.images[0]

$outA = Join-Path $OutDir ("$Prefix-baseA-" + $imgA.filename)
$outB = Join-Path $OutDir ("$Prefix-baseB-" + $imgB.filename)

[System.IO.File]::WriteAllBytes($outA, $client.GetByteArrayAsync("$base/view?filename=$($imgA.filename)&subfolder=$($imgA.subfolder)&type=$($imgA.type)").Result)
[System.IO.File]::WriteAllBytes($outB, $client.GetByteArrayAsync("$base/view?filename=$($imgB.filename)&subfolder=$($imgB.subfolder)&type=$($imgB.type)").Result)

"BASE A: $outA"
"BASE B: $outB"
