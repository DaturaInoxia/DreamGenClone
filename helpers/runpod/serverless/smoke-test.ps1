# smoke-test.ps1 - send a test job to a Serverless endpoint and read the result/logs
#
# B-101. Replaces the pod readiness/identity probes (no persistent HTTP to probe).
# Uses the RunPod Serverless job API: POST /v2/{endpointId}/runsync (or /run + /status).
# P0 GATE: verify the /v2 job API response shape against live RunPod before first use.
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
if ($ImagePath) {
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path $ImagePath)))
    $jobInput.image_b64 = "data:image/png;base64,$b64"
} else {
    $jobInput = @{ image_b64 = "<placeholder>" }
}

$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
$body = @{ input = $jobInput } | ConvertTo-Json -Depth 10

Write-Host "Sending test job to $EndpointKey ($endpointId) ..."
$resp = Invoke-RestMethod -Uri "https://api.runpod.io/v2/$endpointId/runsync" -Method POST -Headers $headers -ContentType "application/json" -Body $body
$resp | ConvertTo-Json -Depth 12

if ($resp.status -eq "COMPLETED") {
    Write-Host "`nPASS: job completed. Inspect output for OpenPose JSON + rendered PNG."
} else {
    Write-Host "`nFAIL/other: inspect 'status' + 'output' above; check RunPod console for job logs (no SSH)."
}
