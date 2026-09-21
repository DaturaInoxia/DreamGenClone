<#
.SYNOPSIS
  Proof test: Qwen Edit with native multi-reference face conditioning.

.DESCRIPTION
  Generates two base images for a location reference using Qwen's native
  multi-reference mechanism:

  - Location reference as the source image
  - Dean's approved profile ref as image2
  - Becky's approved profile ref as image3
  - Native FluxKontextMultiReferenceLatentMethod for face conditioning

  This is the correct way to use Qwen Edit for both location composition
  and strong character identity (Dean + Becky) without SDXL IP-Adapter.

  Output: Two base images (Base A = facing each other, Base B = facing away / same direction).
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    [Parameter(Mandatory = $true)][string]$LocationRef,
    [Parameter(Mandatory = $true)][string]$DeanRefA,
    [Parameter(Mandatory = $true)][string]$BeckyRefA,
    [Parameter(Mandatory = $true)][string]$DeanRefB,
    [Parameter(Mandatory = $true)][string]$BeckyRefB,
    [Parameter(Mandatory = $true)][string]$PromptA,
    [Parameter(Mandatory = $true)][string]$PromptB,
    [string]$Negative = '',
    [long]$Seed = 6601,
    [int]$Width = 1216,
    [int]$Height = 832,
    [string]$Checkpoint = 'Qwen-Rapid-AIO-NSFW-v23.safetensors',
    [string]$LoraName = 'QwenEdit2511_AllIncludedGay_v2.safetensors',
    [double]$LoraStrength = 0.8,
    [int]$Steps = 8,
    [double]$Cfg = 1.0,
    [string]$Sampler = 'euler_ancestral',
    [string]$Scheduler = 'beta',
    [double]$Denoise = 1.0,
    [double]$AuraFlowShift = 3.1,
    [double]$CfgNormStrength = 1.0,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Prefix = 'qwen-ref'
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
$locName   = Upload-Image $base $LocationRef "locref.png"
$deanAName = Upload-Image $base $DeanRefA "dean_a.png"
$beckyAName = Upload-Image $base $BeckyRefA "becky_a.png"
$deanBName = Upload-Image $base $DeanRefB "dean_b.png"
$beckyBName = Upload-Image $base $BeckyRefB "becky_b.png"

"uploaded location ref : $locName"
"uploaded dean A ref   : $deanAName"
"uploaded becky A ref  : $beckyAName"
"uploaded dean B ref   : $deanBName"
"uploaded becky B ref  : $beckyBName"

# Build Qwen workflow with native multi-reference (image2 = Dean, image3 = Becky)
function Build-QwenRefWorkflow($locImg, $deanImg, $beckyImg, $prompt) {
    return @{
        '1'  = @{ class_type = 'LoadImage'; inputs = @{ image = $locImg } }
        '2'  = @{ class_type = 'FluxKontextImageScale'; inputs = @{ image = @('1', 0) } }
        '14' = @{ class_type = 'LoadImage'; inputs = @{ image = $deanImg } }
        '15' = @{ class_type = 'LoadImage'; inputs = @{ image = $beckyImg } }
        '4'  = @{ class_type = 'CheckpointLoaderSimple'; inputs = @{ ckpt_name = $Checkpoint } }
        '5'  = @{ class_type = 'ModelSamplingAuraFlow'; inputs = @{ model = @('4', 0); shift = $AuraFlowShift } }
        '6'  = @{ class_type = 'TextEncodeQwenImageEditPlus'; inputs = @{
            clip = @('4', 1); vae = @('4', 2); image1 = @('2', 0); prompt = $prompt; image2 = @('14', 0); image3 = @('15', 0)
        }}
        '7'  = @{ class_type = 'TextEncodeQwenImageEditPlus'; inputs = @{
            clip = @('4', 1); vae = @('4', 2); image1 = @('2', 0); prompt = $Negative
        }}
        '8'  = @{ class_type = 'VAEEncode'; inputs = @{ pixels = @('2', 0); vae = @('4', 2) } }
        '9'  = @{ class_type = 'CFGNorm'; inputs = @{ model = @('5', 0); strength = $CfgNormStrength } }
        '10' = @{ class_type = 'FluxKontextMultiReferenceLatentMethod'; inputs = @{
            conditioning = @('6', 0); reference_latents_method = 'index_timestep_zero'
        }}
        '11' = @{ class_type = 'FluxKontextMultiReferenceLatentMethod'; inputs = @{
            conditioning = @('7', 0); reference_latents_method = 'index_timestep_zero'
        }}
        '3'  = @{ class_type = 'KSampler'; inputs = @{
            model = @('9', 0); positive = @('10', 0); negative = @('11', 0); latent_image = @('8', 0)
            seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = $Denoise
        }}
        '12' = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('3', 0); vae = @('4', 2) } }
        '13' = @{ class_type = 'SaveImage'; inputs = @{ images = @('12', 0); filename_prefix = $Prefix } }
    }
}

# Generate Base A
$wfA = Build-QwenRefWorkflow $locName $deanAName $beckyAName $PromptA
$payloadA = @{ prompt = $wfA; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 12
$contentA = New-Object System.Net.Http.StringContent $payloadA
$contentA.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/json')
$queuedA = ($client.PostAsync("$base/prompt", $contentA).Result.Content.ReadAsStringAsync().Result | ConvertFrom-Json)
if ($queuedA.error) { throw "ComfyUI rejected base A: $($queuedA.error | ConvertTo-Json -Depth 6)" }
$promptIdA = $queuedA.prompt_id
"queued base A (Qwen native refs): $promptIdA"

# Generate Base B
$wfB = Build-QwenRefWorkflow $locName $deanBName $beckyBName $PromptB
$payloadB = @{ prompt = $wfB; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 12
$contentB = New-Object System.Net.Http.StringContent $payloadB
$contentB.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/json')
$queuedB = ($client.PostAsync("$base/prompt", $contentB).Result.Content.ReadAsStringAsync().Result | ConvertFrom-Json)
if ($queuedB.error) { throw "ComfyUI rejected base B: $($queuedB.error | ConvertTo-Json -Depth 6)" }
$promptIdB = $queuedB.prompt_id
"queued base B (Qwen native refs): $promptIdB"

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

"BASE A (Qwen native refs): $outA"
"BASE B (Qwen native refs): $outB"
