<#
.SYNOPSIS
  Proof test: Juggernaut + ControlNet (Canny + Depth) + Regional IP-Adapter faces.

.DESCRIPTION
  Generates two base images for a location reference using:
  - JuggernautXL as the T2I checkpoint
  - ControlNet Canny + Depth to lock the location's spatial structure
  - Regional IP-Adapter (masked) for Dean and Becky faces only

  This is the industry-standard way to preserve arbitrary location references
  while maintaining strong character identity.

  Output: Two base images (Base A = facing each other, Base B = facing away).
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
    [string]$Checkpoint = 'juggernautXL_ragnarok.safetensors',
    [int]$Steps = 30,
    [double]$Cfg = 5.0,
    [string]$Sampler = 'dpmpp_2m_sde',
    [string]$Scheduler = 'karras',
    [double]$DeanWeight = 0.8,
    [double]$BeckyWeight = 0.6,
    [double]$ControlNetStrength = 0.9,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Prefix = 'controlnet'
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

# Build workflow with ControlNet (Canny + Depth) + regional IP-Adapter faces
function Build-DualWorkflow($locImg, $deanImg, $beckyImg, $prompt, $deanW, $beckyW) {
    return @{
        '4'  = @{ class_type = 'CheckpointLoaderSimple'; inputs = @{ ckpt_name = $Checkpoint } }
        '5'  = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $Width; height = $Height; batch_size = 1 } }
        '6'  = @{ class_type = 'CLIPTextEncode'; inputs = @{ text = $prompt; clip = @('4', 1) } }
        '7'  = @{ class_type = 'CLIPTextEncode'; inputs = @{ text = $Negative; clip = @('4', 1) } }
        '10' = @{ class_type = 'IPAdapterUnifiedLoader'; inputs = @{ model = @('4', 0); preset = 'PLUS FACE (portraits)' } }
        # Location reference for ControlNet
        '11' = @{ class_type = 'LoadImage'; inputs = @{ image = $locImg } }
        # Canny ControlNet
        '12' = @{ class_type = 'CannyEdgePreprocessor'; inputs = @{ image = @('11', 0); low_threshold = 100; high_threshold = 200 } }
        '13' = @{ class_type = 'ControlNetLoader'; inputs = @{ control_net_name = 'controlnet-canny-sdxl-1.0.safetensors' } }
        '14' = @{ class_type = 'ControlNetApplyAdvanced'; inputs = @{
            positive = @('6', 0); negative = @('7', 0); control_net = @('13', 0); image = @('12', 0)
            strength = $ControlNetStrength; start_percent = 0.0; end_percent = 1.0
        }}
        # Depth ControlNet (optional second lock)
        '15' = @{ class_type = 'DepthAnythingV2Preprocessor'; inputs = @{ image = @('11', 0) } }
        '16' = @{ class_type = 'ControlNetLoader'; inputs = @{ control_net_name = 'controlnet-depth-sdxl-1.0.safetensors' } }
        '17' = @{ class_type = 'ControlNetApplyAdvanced'; inputs = @{
            positive = @('14', 0); negative = @('14', 1); control_net = @('16', 0); image = @('15', 0)
            strength = $ControlNetStrength; start_percent = 0.0; end_percent = 1.0
        }}
        # Face IP-Adapters (regional, masked)
        '18' = @{ class_type = 'LoadImage'; inputs = @{ image = $deanImg } }
        '19' = @{ class_type = 'LoadImage'; inputs = @{ image = $beckyImg } }
        '20' = @{ class_type = 'LoadImageMask'; inputs = @{ image = $deanMaskName; channel = 'red' } }
        '21' = @{ class_type = 'LoadImageMask'; inputs = @{ image = $beckyMaskName; channel = 'red' } }
        '22' = @{ class_type = 'IPAdapter'; inputs = @{
            model = @('10', 0); ipadapter = @('10', 1); image = @('18', 0)
            weight = $deanW; weight_type = 'standard'; start_at = 0.0; end_at = 1.0; attn_mask = @('20', 0)
        }}
        '23' = @{ class_type = 'IPAdapter'; inputs = @{
            model = @('22', 0); ipadapter = @('10', 1); image = @('19', 0)
            weight = $beckyW; weight_type = 'standard'; start_at = 0.0; end_at = 1.0; attn_mask = @('21', 0)
        }}
        '3'  = @{ class_type = 'KSampler'; inputs = @{
            seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler
            denoise = 1.0; model = @('23', 0); positive = @('17', 0); negative = @('17', 1); latent_image = @('5', 0)
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
"queued base A (ControlNet): $promptIdA"

# Generate Base B
$wfB = Build-DualWorkflow $locName $deanBName $beckyBName $PromptB $DeanWeight $BeckyWeight
$payloadB = @{ prompt = $wfB; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 12
$contentB = New-Object System.Net.Http.StringContent $payloadB
$contentB.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/json')
$queuedB = ($client.PostAsync("$base/prompt", $contentB).Result.Content.ReadAsStringAsync().Result | ConvertFrom-Json)
$promptIdB = $queuedB.prompt_id
"queued base B (ControlNet): $promptIdB"

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

"BASE A (ControlNet): $outA"
"BASE B (ControlNet): $outB"
