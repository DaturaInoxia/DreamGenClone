<#
.SYNOPSIS
  Generates a clean, character-free location reference image for IP-Adapter conditioning.

.DESCRIPTION
  Creates a T2I render of a location (bedroom, outdoors, etc.) with no people.
  The output is used as an IP-Adapter reference for dual-base generation so that
  multiple character pose variations share the exact same location conditioning.

  Run from the repo root. PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    [Parameter(Mandatory = $true)][string]$LocationName,
    [Parameter(Mandatory = $true)][string]$Prompt,
    [string]$Negative = 'people, person, man, woman, character, face, body, crowd, text, watermark, logo',
    [long]$Seed = 1001,
    [int]$Width = 1216,
    [int]$Height = 832,
    [string]$Checkpoint = 'bigLust_v16.safetensors',
    [int]$Steps = 30,
    [double]$Cfg = 5.0,
    [string]$Sampler = 'dpmpp_2m_sde',
    [string]$Scheduler = 'karras',
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Stop'
$base = $ComfyUiUrl.TrimEnd('/')

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(600)

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$workflow = [ordered]@{
    '4' = @{ class_type = 'CheckpointLoaderSimple'; inputs = @{ ckpt_name = $Checkpoint } }
    '5' = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $Width; height = $Height; batch_size = 1 } }
    '6' = @{ class_type = 'CLIPTextEncode'; inputs = @{ clip = @('4', 1); text = $Prompt } }
    '7' = @{ class_type = 'CLIPTextEncode'; inputs = @{ clip = @('4', 1); text = $Negative } }
    '3' = @{ class_type = 'KSampler'; inputs = @{
        seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler
        denoise = 1.0; model = @('4', 0); positive = @('6', 0); negative = @('7', 0); latent_image = @('5', 0)
    }}
    '8' = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('3', 0); vae = @('4', 2) } }
    '9' = @{ class_type = 'SaveImage'; inputs = @{ images = @('8', 0); filename_prefix = "locref-$LocationName" } }
}

$payload = @{ prompt = $workflow; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 12
$content = New-Object System.Net.Http.StringContent $payload
$content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/json')
$queued = ($client.PostAsync("$base/prompt", $content).Result.Content.ReadAsStringAsync().Result | ConvertFrom-Json)

if ($queued.error) { throw "ComfyUI rejected the prompt: $($queued.error | ConvertTo-Json -Depth 6)" }
$promptId = $queued.prompt_id
"queued location ref ($LocationName): $promptId"

$deadline = (Get-Date).AddSeconds(900)
$entry = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $history = ($client.GetStringAsync("$base/history/$promptId").Result | ConvertFrom-Json)
    if ($history.PSObject.Properties.Name -contains $promptId) {
        $entry = $history.$promptId
        if ($entry.outputs) { break }
    }
}

if (-not $entry -or -not $entry.outputs) { throw "Location render did not complete." }
$image = $entry.outputs.'9'.images[0]

$target = Join-Path $OutDir ("locref-$LocationName-" + $image.filename)
$bytes = $client.GetByteArrayAsync("$base/view?filename=$($image.filename)&subfolder=$($image.subfolder)&type=$($image.type)").Result
[System.IO.File]::WriteAllBytes($target, $bytes)

"OUTPUT: $target"
