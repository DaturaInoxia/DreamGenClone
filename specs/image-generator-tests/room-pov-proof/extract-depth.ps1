<#
.SYNOPSIS
  Extract a DepthAnythingV2 depth map from an image, via the local ComfyUI host.

.DESCRIPTION
  The room-POV proof showed Qwen 2.1 reproduces a room when the REQUESTED view's depth map travels
  as a normal reference image (image_2). For a synthetic room that depth is analytic. For a real
  photo it has to be ESTIMATED, which is what this does -- so the recipe can be tested on real
  photographs, where no ground-truth geometry exists.

  Writes a single greyscale PNG (near = bright, DepthAnythingV2's convention).

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File .../extract-depth.ps1 `
    -Image path/to/back.png -OutFile path/to/back-depth.png
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'http://192.168.0.11:8188',
    [Parameter(Mandatory = $true)][string]$Image,
    [Parameter(Mandatory = $true)][string]$OutFile,
    [string]$DepthModel = 'depth_anything_v2_vitl.pth',
    [int]$Resolution = 1024,
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$base = $ComfyUiUrl.TrimEnd('/')

$full = (Resolve-Path $Image).Path
$outFull = [System.IO.Path]::GetFullPath(
    [System.IO.Path]::Combine((Get-Location).Path, $OutFile))
New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($outFull)) | Out-Null

$oi = Invoke-RestMethod -Uri "$base/object_info" -TimeoutSec 120
if ($oi.PSObject.Properties.Name -notcontains 'DepthAnythingV2Preprocessor') {
    throw "Host has no DepthAnythingV2Preprocessor node."
}
# ckpt_name and resolution sit under `optional` for this node, not `required` - reading only
# `required` yields $null and the validation then throws on a node that is perfectly usable.
$pre = $oi.DepthAnythingV2Preprocessor.input
$ckptChoices = @()
foreach ($scope in @($pre.required, $pre.optional)) {
    if ($scope -and $scope.PSObject.Properties['ckpt_name']) { $ckptChoices = $scope.ckpt_name[0] }
}
if ($ckptChoices.Count -eq 0) { throw "Could not read DepthAnythingV2Preprocessor depth model choices." }
if ($ckptChoices -notcontains $DepthModel) {
    throw "Host does not list depth model '$DepthModel'. Available: $($ckptChoices -join ', ')"
}

$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds([Math]::Max(300, $TimeoutSeconds + 120))

$bytes = [System.IO.File]::ReadAllBytes($full)
$multipart = New-Object System.Net.Http.MultipartFormDataContent
$content = New-Object System.Net.Http.ByteArrayContent (, $bytes)
$content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('image/png')
$multipart.Add($content, 'image', 'depth-source.png')
$multipart.Add((New-Object System.Net.Http.StringContent 'true'), 'overwrite')
$up = $client.PostAsync("$base/upload/image", $multipart).Result
$upBody = $up.Content.ReadAsStringAsync().Result
if (-not $up.IsSuccessStatusCode) { throw "Upload failed: $($up.StatusCode) $upBody" }
$uploaded = ($upBody | ConvertFrom-Json).name

$g = [ordered]@{
    '1' = @{ class_type = 'LoadImage'; inputs = @{ image = $uploaded } }
    '2' = @{ class_type = 'ImageScale'; inputs = @{ image = @('1', 0); upscale_method = 'bilinear'; width = $Resolution; height = $Resolution; crop = 'disabled' } }
    '3' = @{ class_type = 'DepthAnythingV2Preprocessor'; inputs = @{ image = @('2', 0); ckpt_name = $DepthModel; resolution = $Resolution } }
    '4' = @{ class_type = 'SaveImage'; inputs = @{ images = @('3', 0); filename_prefix = 'depthmap' } }
}

$payload = @{ prompt = $g; client_id = [guid]::NewGuid().ToString() } | ConvertTo-Json -Depth 32
$sc = [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, 'application/json')
$resp = $client.PostAsync("$base/prompt", $sc).Result
$body = $resp.Content.ReadAsStringAsync().Result
if (-not $resp.IsSuccessStatusCode) { throw "Submit failed: $($resp.StatusCode) $body" }
$id = ($body | ConvertFrom-Json).prompt_id
Write-Host "Extracting depth: prompt_id=$id"

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$result = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $h = Invoke-RestMethod -Uri "$base/history/$id" -TimeoutSec 60
    if ($h.PSObject.Properties[$id]) {
        $entry = $h.$id
        if ($entry.status.messages | Where-Object { $_[0] -eq 'execution_error' }) {
            throw "Depth extraction errored: $($entry.status.messages | ConvertTo-Json -Depth 8 -Compress)"
        }
        if ($entry.status.completed -eq $true -or $entry.outputs) { $result = $entry; break }
    }
}
if (-not $result) { throw "Timed out waiting for depth extraction." }

$img = $result.outputs.'4'.images | Select-Object -First 1
if (-not $img) { throw "Depth node produced no image." }
$url = "$base/view?filename=$([uri]::EscapeDataString($img.filename))&subfolder=$([uri]::EscapeDataString($img.subfolder))&type=$($img.type)"
Invoke-WebRequest -Uri $url -OutFile $outFull -TimeoutSec 300
Write-Host "Wrote $outFull"
