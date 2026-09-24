<#
.SYNOPSIS
  Run ONE ComfyUI API-format workflow against the LOCAL ComfyUI host and save every output image.

.DESCRIPTION
  Local-ComfyUI replacement for the RunPod Serverless proof path
  (helpers/runpod/serverless/smoke-test.ps1). Same proof inputs - a workflow JSON plus an
  optional [{name,path}] image manifest - but submitted over the ComfyUI HTTP API that the
  local host already serves:

      POST /upload/image   (each manifest image, uploaded under its manifest name so the
                            workflow's LoadImage 'image' fields match unchanged)
      POST /prompt         ({ prompt = <workflow>, client_id })
      GET  /history/<id>   (poll for this exact prompt_id)
      GET  /view           (fetch each produced image)

  Differences from the serverless path, deliberately:
    - input.workflow + input.images (base64 into the worker) is replaced by a real upload.
    - Outputs are read from the run's own history entry, so no base64 decoding.
    - A preflight checks that EVERY class_type in the workflow exists on the target host and
      fails fast listing the missing ones. This is the guard that catches host/worker drift
      (e.g. the XLabs FLUX nodes missing on the local host) BEFORE an image job is queued.

  No retries, no resubmit: one submit per invocation, exactly one prompt_id tracked.

.PARAMETER WorkflowPath
  ComfyUI API-format workflow JSON (the same files the serverless proofs used).

.PARAMETER ImagesJsonPath
  Optional JSON array of { "name": <ComfyUI LoadImage image name>, "path": <local file> }.
  Each file is uploaded under its manifest 'name', so a workflow file is portable between
  the serverless worker and the local host unchanged.

.PARAMETER OutDir
  Directory for the produced images. Saved as <Prefix>_<i>.<ext>. Printed as SAVED:<path>.

.PARAMETER Seed
  Optional fixed seed applied to every seed/noise_seed input in the workflow.

.PARAMETER DryRun
  Preflight only: resolve the host, list missing node classes, print the planned uploads and
  the output dirs. Nothing is queued.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-local-proof.ps1 `
    -WorkflowPath specs/image-generator-tests/dual-base-location/proofs-local/flux-figures-A.workflow.json `
    -ImagesJsonPath specs/image-generator-tests/dual-base-location/proofs-local/flux-figures-A.images.json `
    -OutDir specs/image-generator-tests/dual-base-location/runs/<run>/03-studio-a -Prefix img-local
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$WorkflowPath,
    [string]$ImagesJsonPath,
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Prefix = 'img-local',
    [int]$TimeoutSec = 2400,
    [int]$Seed = -1,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot
$base = $ComfyUiUrl.TrimEnd('/')

function Fail($msg) { throw $msg }

# ------------------------------------------------------------------ workflow
if (-not (Test-Path $WorkflowPath)) { Fail "Workflow not found: $WorkflowPath" }
$wf = Get-Content -Raw -Path $WorkflowPath | ConvertFrom-Json
$nodeIds = @($wf.PSObject.Properties.Name)
if ($nodeIds.Count -eq 0) { Fail "Workflow has no nodes: $WorkflowPath" }
$usedClasses = @($wf.PSObject.Properties.Value.class_type | Sort-Object -Unique)
Write-Host "Workflow : $WorkflowPath"
Write-Host "Nodes    : $($nodeIds.Count)  classes: $($usedClasses -join ', ')"

# ------------------------------------------------------------------- images
$manifest = @()
if ($ImagesJsonPath) {
    if (-not (Test-Path $ImagesJsonPath)) { Fail "Images manifest not found: $ImagesJsonPath" }
    # Note: do NOT wrap ConvertFrom-Json in @() on PowerShell 5.1 - it nests the array
    # as a single element. foreach enumerates the returned object array correctly.
    $parsedManifest = Get-Content -Raw -Path $ImagesJsonPath | ConvertFrom-Json
    foreach ($m in $parsedManifest) { $manifest += $m }
    foreach ($item in $manifest) {
        if (-not (Test-Path $item.path)) { Fail "Manifest image missing: $($item.path)" }
    }
}

if ($Seed -ge 0) {
    $overridden = 0
    foreach ($prop in $wf.PSObject.Properties) {
        $inputs = $prop.Value.inputs
        if ($null -eq $inputs) { continue }
        foreach ($field in @('seed', 'noise_seed')) {
            if ($inputs.PSObject.Properties.Name -contains $field) {
                $inputs.$field = [int]$Seed
                $overridden++
            }
        }
    }
    Write-Host "Seed override: $Seed applied to $overridden field(s)."
}

# ---------------------------------------------------------------- preflight
$host_info = $null
try {
    $host_info = Invoke-RestMethod -Uri "$base/system_stats" -TimeoutSec 30
} catch {
    Fail "Local ComfyUI is not reachable at $base : $($_.Exception.Message)"
}
Write-Host "Host     : $base  (ComfyUI $($host_info.system.comfyui_version), $($host_info.devices[0].name))"

$objectInfo = Invoke-RestMethod -Uri "$base/object_info" -TimeoutSec 120
$known = $objectInfo.PSObject.Properties.Name
$missing = @($usedClasses | Where-Object { $known -notcontains $_ })
if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "MISSING NODE CLASSES on $base :" -ForegroundColor Red
    foreach ($m in $missing) { Write-Host "  - $m" -ForegroundColor Red }
    if ($missing -contains 'LoadFluxControlNet' -or $missing -contains 'ApplyFluxControlNet' -or $missing -contains 'XlabsSampler') {
        Write-Host ""
        Write-Host "The XLabs FLUX runtime is not installed on the local host." -ForegroundColor Yellow
        Write-Host "Run helpers/local-comfyui-host/provision-xlabs-flux.ps1 ON the ComfyUI host, restart" -ForegroundColor Yellow
        Write-Host "ComfyUI, then re-run this proof." -ForegroundColor Yellow
    }
    Fail "Workflow requires $($missing.Count) node class(es) the target host does not provide."
}
Write-Host "Preflight: all $($usedClasses.Count) node classes present." -ForegroundColor Green

if ($DryRun) {
    Write-Host ""
    Write-Host "DRY RUN - nothing queued."
    foreach ($item in $manifest) { Write-Host "  would upload '$($item.name)'  <- $($item.path)" }
    Write-Host "  would write outputs to: $OutDir"
    return
}

# ------------------------------------------------------------------ posting
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds([Math]::Max(300, $TimeoutSec + 120))

function Upload-Image([string]$filePath, [string]$uploadName) {
    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $filePath))
    $multipart = New-Object System.Net.Http.MultipartFormDataContent
    $content = New-Object System.Net.Http.ByteArrayContent (, $bytes)
    $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('image/png')
    $multipart.Add($content, 'image', $uploadName)
    $multipart.Add((New-Object System.Net.Http.StringContent 'true'), 'overwrite')
    $resp = $client.PostAsync("$base/upload/image", $multipart).Result
    $body = $resp.Content.ReadAsStringAsync().Result
    if (-not $resp.IsSuccessStatusCode) { Fail "Upload of '$uploadName' failed: $($resp.StatusCode) $body" }
    $parsed = $body | ConvertFrom-Json
    if ($parsed.subfolder) { return "$($parsed.subfolder)/$($parsed.name)" }
    return $parsed.name
}

$uploaded = @{}
foreach ($item in $manifest) {
    $name = Upload-Image -filePath $item.path -uploadName $item.name
    $uploaded[$item.name] = $name
    Write-Host "  uploaded '$($item.name)' -> $name"
}

# Uploaded name must be what the graph asks for; verify, do not guess.
$loadImageNames = @()
foreach ($prop in $wf.PSObject.Properties) {
    if ($prop.Value.class_type -in @('LoadImage', 'LoadImageMask')) {
        $loadImageNames += $prop.Value.inputs.image
    }
}
$neededImages = @($loadImageNames | Sort-Object -Unique)
foreach ($need in $neededImages) {
    if (-not $uploaded.ContainsKey($need)) {
        $provided = if ($manifest.Count -gt 0) { ($manifest | ForEach-Object { $_.name }) -join ', ' } else { '<none>' }
        Fail "Workflow needs image '$need' but the manifest provides: $provided"
    }
}

# ------------------------------------------------------------------ submit
$payload = @{ prompt = $wf; client_id = "dreamgen-local-proof" } | ConvertTo-Json -Depth 30
$content = New-Object System.Net.Http.StringContent $payload
$content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/json')
$queueResp = $client.PostAsync("$base/prompt", $content).Result
$queueBody = $queueResp.Content.ReadAsStringAsync().Result
if (-not $queueResp.IsSuccessStatusCode) {
    Fail "ComfyUI rejected the workflow ($([int]$queueResp.StatusCode)): $queueBody"
}
$queued = $queueBody | ConvertFrom-Json
if ($queued.error) { Fail "ComfyUI returned an error: $($queued.error | ConvertTo-Json -Depth 8)" }
$promptId = $queued.prompt_id
Write-Host "Queued prompt_id=$promptId  (queue number $($queued.number))"

# -------------------------------------------------------------------- poll
$deadline = (Get-Date).AddSeconds($TimeoutSec)
$entry = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    try {
        $hist = Invoke-RestMethod -Uri "$base/history/$promptId" -TimeoutSec 60
    } catch {
        Write-Host "  ... history poll error: $($_.Exception.Message) (retrying)"
        continue
    }
    if ($hist.PSObject.Properties.Name -contains $promptId) { $entry = $hist.$promptId; break }
}
if ($null -eq $entry) { Fail "Timed out after ${TimeoutSec}s waiting for prompt_id $promptId." }

$statusStr = $entry.status.status_str
if ($statusStr -eq 'error') {
    Write-Host ($entry.status | ConvertTo-Json -Depth 10)
    Fail "Workflow error for prompt_id $promptId"
}
if ($statusStr -ne 'success') { Fail "Unexpected status '$statusStr' for prompt_id $promptId" }

# ------------------------------------------------------------------ outputs
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$outRoot = (Resolve-Path $OutDir).Path

$images = @()
foreach ($nodeOut in $entry.outputs.PSObject.Properties) {
    foreach ($img in $nodeOut.Value.images) { if ($img) { $images += , $img } }
}
if ($images.Count -eq 0) { Fail "No images produced for prompt_id $promptId." }

$idx = 0
foreach ($img in $images) {
    $query = "filename=$([uri]::EscapeDataString($img.filename))"
    if ($img.subfolder) { $query += "&subfolder=$([uri]::EscapeDataString($img.subfolder))" }
    if ($img.type)      { $query += "&type=$([uri]::EscapeDataString($img.type))" }
    $ext = [IO.Path]::GetExtension($img.filename)
    if (-not $ext) { $ext = '.png' }
    $outPath = Join-Path $outRoot ("{0}_{1}{2}" -f $Prefix, $idx, $ext)
    [IO.File]::WriteAllBytes($outPath, $client.GetByteArrayAsync("$base/view?$query").Result)
    Write-Host "SAVED:$outPath"
    $idx++
}

$client.Dispose()
Write-Host ""
Write-Host "PASS: prompt_id $promptId completed, $idx image(s) saved to $outRoot" -ForegroundColor Green
