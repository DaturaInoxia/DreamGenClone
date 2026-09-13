<#
.SYNOPSIS
  Replays the six controlled Qwen proof edits against the LOCAL Rapid-AIO checkpoint.

.DESCRIPTION
  "These tests" = the six-of-six controlled, non-explicit edits frozen in
  `specs/image-generator-tests/qwen/manifest.json`. The RunPod harness replays them through the stock
  2511 split graph (`prompts/qwen-simple-people-edit.json`, 40 steps / CFG 4). This runner replays the
  SAME prompts and seeds through the merged-checkpoint graph the app now emits for the local editor
  (`ComfyUIImageEditingClient.BuildAioMergedCheckpointWorkflow`): CheckpointLoaderSimple +
  ModelSamplingAuraFlow + CFGNorm + TextEncodeQwenImageEditPlus x2 + KSampler, 8 steps / CFG 1 /
  euler_ancestral / beta.

  Source image defaults to the frozen canonical `base.png` so the six cells stay comparable with the
  committed accepted outputs. Pass -RegenerateBase to render a fresh base from the tracked Juggernaut
  workflow first (needs juggernautXL_ragnarok.safetensors on the same host).

  Outputs go to a git-ignored folder plus a run manifest. Review the images visually; this runner does
  not score them.

.NOTES
  PowerShell 5.1 compatible. Does not modify the frozen proof package or the app database.
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'http://192.168.0.16:8188',
    [string]$Checkpoint = 'Qwen-Rapid-AIO-NSFW-v23.safetensors',
    [string]$ManifestPath = 'specs/image-generator-tests/qwen/manifest.json',
    [string]$SourceImage = 'specs/image-generator-tests/qwen/images/base.png',
    [int]$Steps = 8,
    [double]$Cfg = 1.0,
    [string]$Sampler = 'euler_ancestral',
    [string]$Scheduler = 'beta',
    [double]$Denoise = 1.0,
    [double]$AuraFlowShift = 3.1,
    [double]$CfgNormStrength = 1.0,
    [string]$OutDir = 'artifacts/tmp/images/qwen-six-edit-local-aio',
    [int]$TimeoutSeconds = 900,
    [string[]]$Only = @()
)

$ErrorActionPreference = 'Stop'
$base = $ComfyUiUrl.TrimEnd('/')
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repoRoot

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
if (-not (Test-Path $SourceImage)) { throw "Source image '$SourceImage' was not found." }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(300)

function Send-MergedCheckpointEdit {
    param([string]$UploadedImage, [string]$Instruction, [long]$Seed)

    $workflow = [ordered]@{
        '1'  = @{ class_type = 'LoadImage';                            inputs = @{ image = $UploadedImage } }
        '2'  = @{ class_type = 'FluxKontextImageScale';                inputs = @{ image = @('1', 0) } }
        '3'  = @{ class_type = 'KSampler';                             inputs = @{
                    model = @('14', 0); positive = @('12', 0); negative = @('13', 0); latent_image = @('8', 0)
                    seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = $Denoise
                } }
        '5'  = @{ class_type = 'ModelSamplingAuraFlow';                 inputs = @{ model = @('16', 0); shift = $AuraFlowShift } }
        '6'  = @{ class_type = 'TextEncodeQwenImageEditPlus';           inputs = @{ clip = @('16', 1); vae = @('16', 2); image1 = @('2', 0); prompt = $Instruction } }
        '7'  = @{ class_type = 'TextEncodeQwenImageEditPlus';           inputs = @{ clip = @('16', 1); vae = @('16', 2); image1 = @('2', 0); prompt = '' } }
        '8'  = @{ class_type = 'VAEEncode';                             inputs = @{ pixels = @('2', 0); vae = @('16', 2) } }
        '9'  = @{ class_type = 'SaveImage';                             inputs = @{ images = @('15', 0); filename_prefix = 'dreamgen_proof/six-edit-local-aio' } }
        '12' = @{ class_type = 'FluxKontextMultiReferenceLatentMethod'; inputs = @{ conditioning = @('6', 0); reference_latents_method = 'index_timestep_zero' } }
        '13' = @{ class_type = 'FluxKontextMultiReferenceLatentMethod'; inputs = @{ conditioning = @('7', 0); reference_latents_method = 'index_timestep_zero' } }
        '14' = @{ class_type = 'CFGNorm';                               inputs = @{ model = @('5', 0); strength = $CfgNormStrength } }
        '15' = @{ class_type = 'VAEDecode';                             inputs = @{ samples = @('3', 0); vae = @('16', 2) } }
        '16' = @{ class_type = 'CheckpointLoaderSimple';                inputs = @{ ckpt_name = $Checkpoint } }
    }

    $payload = @{ prompt = $workflow; client_id = 'dreamgen-proof' } | ConvertTo-Json -Depth 20 -Compress
    $content = New-Object System.Net.Http.StringContent($payload, [System.Text.Encoding]::UTF8, 'application/json')
    $response = $client.PostAsync("$base/prompt", $content).Result
    if (-not $response.IsSuccessStatusCode) {
        throw "Prompt submit failed: $($response.StatusCode) $($response.Content.ReadAsStringAsync().Result)"
    }
    $promptId = ($response.Content.ReadAsStringAsync().Result | ConvertFrom-Json).prompt_id

    $started = Get-Date
    $deadline = $started.AddSeconds($TimeoutSeconds)
    $image = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        $history = Invoke-RestMethod -Uri "$base/history/$promptId" -TimeoutSec 30
        $entry = $null
        foreach ($property in $history.PSObject.Properties) {
            if ($property.Name -eq $promptId) { $entry = $property.Value }
        }
        if (-not $entry) { continue }
        if ($entry.status.status_str -eq 'error') { throw "ComfyUI error: $($entry.status | ConvertTo-Json -Depth 8 -Compress)" }
        if ($entry.outputs) {
            foreach ($nodeId in $entry.outputs.PSObject.Properties.Name) {
                $images = $entry.outputs.$nodeId.images
                if ($images) { $image = $images[0]; break }
            }
        }
        if ($image) { break }
    }
    if (-not $image) { throw "Timed out after $TimeoutSeconds seconds (prompt $promptId)." }

    $query = "filename=$([uri]::EscapeDataString($image.filename))&subfolder=$([uri]::EscapeDataString($image.subfolder))&type=$($image.type)"
    $viewResponse = $client.GetAsync("$base/view?$query").Result
    $viewResponse.EnsureSuccessStatusCode() | Out-Null

    [pscustomobject]@{
        PromptId = $promptId
        ElapsedSeconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 2)
        FileName = $image.filename
        Bytes = $viewResponse.Content.ReadAsByteArrayAsync().Result
    }
}

# --- upload the frozen base once, reuse for all six cells ---------------------
$uploadName = 'six-edit-source-' + [System.IO.Path]::GetFileName($SourceImage)
$sourceBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $SourceImage))
$multipart = New-Object System.Net.Http.MultipartFormDataContent
$imageContent = New-Object System.Net.Http.ByteArrayContent (,$sourceBytes)
$imageContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('image/png')
$multipart.Add($imageContent, 'image', $uploadName)
$multipart.Add((New-Object System.Net.Http.StringContent 'true'), 'overwrite')
$uploadResponse = $client.PostAsync("$base/upload/image", $multipart).Result
if (-not $uploadResponse.IsSuccessStatusCode) {
    throw "Upload failed: $($uploadResponse.StatusCode) $($uploadResponse.Content.ReadAsStringAsync().Result)"
}
$uploaded = ($uploadResponse.Content.ReadAsStringAsync().Result | ConvertFrom-Json).name
"source uploaded: $uploaded (from $SourceImage)"

# --- replay the six controlled edits ------------------------------------------
$results = @()
foreach ($edit in $manifest.edits) {
    if ($Only.Count -gt 0 -and ($Only -notcontains $edit.id)) { continue }

    '--- PROMPT SENT TO THE MODEL -------------------------------------------------'
    "case       : $($edit.id)"
    "instruction: $($edit.prompt)"
    "checkpoint : $Checkpoint"
    "sampler    : $Sampler / $Scheduler, $Steps steps, CFG $Cfg, denoise $Denoise, seed $($edit.seed)"
    '-----------------------------------------------------------------------------'

    $result = Send-MergedCheckpointEdit -UploadedImage $uploaded -Instruction $edit.prompt -Seed ([long]$edit.seed)
    $outPath = Join-Path $OutDir ("{0}.png" -f $edit.id)
    [System.IO.File]::WriteAllBytes((Join-Path $repoRoot $outPath), $result.Bytes)

    $results += [pscustomobject]@{
        id = $edit.id
        seed = $edit.seed
        promptId = $result.PromptId
        elapsedSeconds = $result.ElapsedSeconds
        output = $outPath
        bytes = $result.Bytes.Length
    }
    "RENDERED $($edit.id): $($result.ElapsedSeconds)s -> $outPath ($($result.Bytes.Length) bytes)"
}

$manifestOut = Join-Path $OutDir 'run-manifest.json'
$results | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $repoRoot $manifestOut) -Encoding UTF8
"run manifest: $manifestOut"
"cases rendered: $($results.Count)"
