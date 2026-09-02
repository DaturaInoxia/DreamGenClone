# smoke-test.ps1 - send a test job to a Serverless endpoint and read the result/logs
#
# B-101. Replaces the pod readiness/identity probes (no persistent HTTP to probe).
# Uses the RunPod Serverless job API: POST https://api.runpod.ai/v2/{endpointId}/runsync?wait=N
# (the base URL + requestUrls come from the created endpoint; the create returned api.runpod.ai).
# First call cold-starts (pulls image + boots), so wait is set high (600s) for the smoke test.
#
# Usage:
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/smoke-test.ps1 `
#     -EndpointKey pose-dwpose-serverless -ImagePath path/to/image.png
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/smoke-test.ps1 `
#     -EndpointKey img-juggernaut-serverless -WorkflowJsonPath helpers/runpod/workflows/juggernaut-t2i.json
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/smoke-test.ps1 `
#     -EndpointKey img-identity-serverless -WorkflowJsonPath helpers/runpod/serverless/proofs/identity-2char.json `
#     -ImagesJsonPath helpers/runpod/serverless/proofs/identity-2char.images.json -OutDir artifacts/tmp/proofs/identity
#
# -ImagesJsonPath: a JSON file that is an ARRAY of { "name": <ComfyUI image field name>, "path": <local file> }.
#   Each entry is attached to input.images under the given name (must match the LoadImage/LoadImageMask
#   'image' fields in the workflow). Used for identity (refs + regional masks) and qwen-edit (source) proofs.
# -OutDir: when the job COMPLETES, output.images[].data (base64) is decoded and written here as
#   <endpointKey>_<i>.png, and the saved paths are printed on their own lines as SAVED:<path>.
param(
    [Parameter(Mandatory = $true)][string]$EndpointKey,
    [string]$ImagePath,
    [string]$WorkflowJsonPath,
    [string]$ImagesJsonPath,
    [string]$OutDir
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
Set-Location $repoRoot

. (Join-Path $PSScriptRoot "..\common.ps1")
Get-RunPodEnv

$registry = Get-Content -Raw -Path (Join-Path $PSScriptRoot "endpoints.json") | ConvertFrom-Json
$entry = $registry.endpoints | Where-Object { $_.endpointKey -eq $EndpointKey }
if ($null -eq $entry) { throw "endpoints.json has no entry for '$EndpointKey'." }
$endpointId = $entry.endpointId
if ([string]::IsNullOrWhiteSpace($endpointId)) {
    throw "endpoints.json entry '$EndpointKey' has no endpointId yet (create the endpoint first)."
}

# Official worker-comfyui contract: input.workflow (ComfyUI API JSON) + input.images[].
$jobInput = @{ workflow = $null; images = @() }

if ($WorkflowJsonPath) {
    # Use an external ComfyUI API-format workflow file (e.g. a T2I workflow for Juggernaut).
    Write-Host "Using workflow file: $WorkflowJsonPath"
    $jobInput.workflow = Get-Content -Raw -Path (Resolve-Path $WorkflowJsonPath) | ConvertFrom-Json
} else {
    # Default DWPose workflow (node ids verified on 0.5.0).
    # LoadImage 'image' must match images[].name.
    $jobInput.workflow = @{
        "1" = @{ class_type = "LoadImage"; inputs = @{ image = "input_image.png" } }
        "2" = @{
            class_type = "DWPreprocessor"
            inputs     = @{
                image            = @("1", 0)
                detect_hand      = "enable"
                detect_body      = "enable"
                detect_face      = "enable"
                resolution       = 512
                bbox_detector    = "yolox_l.torchscript.pt"
                pose_estimator   = "dw-ll_ucoco_384_bs5.torchscript.pt"
                scale_stick_for_xinsr_cn = "disable"
            }
        }
        "3" = @{ class_type = "SaveImage"; inputs = @{ filename_prefix = "dwpose_sls"; images = @("2", 0) } }
    }
}

if ($ImagePath) {
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path $ImagePath)))
    $jobInput.images = @(@{ name = "input_image.png"; image = "data:image/png;base64,$b64" })
}

if ($ImagesJsonPath) {
    # Multi-image manifest: [ { name, path }, ... ] -> input.images[]. Names MUST match the
    # workflow's LoadImage / LoadImageMask 'image' fields (the worker substitutes by name).
    $manifest = Get-Content -Raw -Path (Resolve-Path $ImagesJsonPath) | ConvertFrom-Json
    $jobInput.images = @()
    foreach ($item in $manifest) {
        $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path $item.path)))
        $jobInput.images += @{ name = $item.name; image = "data:image/png;base64,$b64" }
        Write-Host "  attached image '$($item.name)' from $($item.path)"
    }
}

$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
$body = @{ input = $jobInput } | ConvertTo-Json -Depth 10

# Async submit + poll (runsync 'wait' is capped at 300000 ms = 5 min, which is too
# short for a cold start that pulls a ~15-20 GB image; this is also the pattern the
# app will need for long jobs).
$base = "https://api.runpod.ai/v2/$endpointId"
Write-Host "Submitting job to $EndpointKey ($endpointId) ... (cold start may take several minutes)"
$submit = Invoke-RestMethod -Uri "$base/run" -Method POST -Headers $headers -ContentType "application/json" -Body $body -TimeoutSec 60
$jobId = $submit.id
Write-Host "submitted job: $jobId (status $($submit.status))"

$done = $false
for ($i = 1; $i -le 90; $i++) {   # up to ~15 min
    Start-Sleep -Seconds 10
    try {
        $st = Invoke-RestMethod -Uri "$base/status/$jobId" -Headers $headers -TimeoutSec 60
    } catch {
        Write-Host "  ... status poll error: $($_.Exception.Message) (retrying)"
        continue
    }
    Write-Host "  poll $i : status=$($st.status)"
    if ($st.status -in @("COMPLETED", "FAILED", "CANCELLED", "TIMED_OUT")) {
        $st | ConvertTo-Json -Depth 12
        $done = $true
        break
    }
}
if (-not $done) { Write-Host "`nTIMEOUT after 15 min - check RunPod console for the job's status/logs." }
elseif ($st.status -eq "COMPLETED") {
    Write-Host "`nPASS: job completed. Inspect output.images[] for the rendered PNG (base64)."
    # Decode output images when an output dir is requested (proof-runner flow).
    if ($OutDir) {
        $outRoot = Resolve-Path $OutDir -ErrorAction SilentlyContinue
        if (-not $outRoot) { $outRoot = Join-Path $repoRoot $OutDir; New-Item -ItemType Directory -Force -Path $outRoot | Out-Null }
        $outRoot = (Resolve-Path $outRoot).Path
        $idx = 0
        if ($st.output.images) {
            foreach ($img in $st.output.images) {
                if ($img.data) {
                    $raw = $img.data
                    if ($raw -match '^data:') { $raw = ($raw -split ',', 2)[1] }
                    $bytes = [Convert]::FromBase64String($raw)
                    $outPath = Join-Path $outRoot "$EndpointKey`_$idx.png"
                    [IO.File]::WriteAllBytes($outPath, $bytes)
                    Write-Host "SAVED:$outPath"
                    $idx++
                }
            }
        }
        if ($idx -eq 0) { Write-Host "WARN: no output.images[].data found to save." }
    }
}
else { Write-Host "`nFAIL: inspect 'output'/'error' above; check RunPod console for job logs (no SSH)." }
