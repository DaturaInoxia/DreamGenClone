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
param(
    [Parameter(Mandatory = $true)][string]$EndpointKey,
    [string]$ImagePath
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

$jobInput = @{}
# Official worker-comfyui contract: input.workflow (ComfyUI API JSON) + input.images[].
# DWPose workflow (node ids verified on 0.5.0). LoadImage 'image' must match images[].name.
$workflow = @{
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

$jobInput = @{ workflow = $workflow; images = @() }
if ($ImagePath) {
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path $ImagePath)))
    $jobInput.images = @(@{ name = "input_image.png"; image = "data:image/png;base64,$b64" })
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
elseif ($st.status -eq "COMPLETED") { Write-Host "`nPASS: job completed. Inspect output.images[] for the rendered pose PNG (base64)." }
else { Write-Host "`nFAIL: inspect 'output'/'error' above; check RunPod console for job logs (no SSH)." }
