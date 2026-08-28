# check-job.ps1 - Check a RunPod Serverless job status + list workers.
# Usage: check-job.ps1 -EndpointKey <key-in-endpoints.json> -JobId <job_id>
param(
    [Parameter(Mandatory=$true)][string]$EndpointKey,
    [Parameter(Mandatory=$false)][string]$JobId = "",
    [Parameter(Mandatory=$false)][string]$WorkerId = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Load env (RUNPOD_API_KEY) - file is git-ignored, safe to dot-source
. (Join-Path $scriptDir "..\.runpod-env.ps1")

# Load endpoint registry
$reg = Get-Content (Join-Path $scriptDir "endpoints.json") -Raw | ConvertFrom-Json
$ep = $reg.endpoints | Where-Object { $_.endpointKey -eq $EndpointKey }
if (-not $ep) { throw "Endpoint '$EndpointKey' not found in endpoints.json" }
$epId = $ep.endpointId

$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }

Write-Host "=== Endpoint: $EndpointKey ($epId) ==="

if ($JobId) {
    Write-Host "`n--- Job $JobId status ---"
    try {
        $st = Invoke-RestMethod -Uri "https://api.runpod.ai/v2/$epId/status/$JobId" -Headers $headers
        $st | ConvertTo-Json -Depth 10
    } catch {
        Write-Host "Job status error: $_"
    }
}

Write-Host "`n--- Workers ---"
try {
    $wk = Invoke-RestMethod -Uri "https://api.runpod.io/v2/serverless/$epId/workers" -Headers $headers
    $wk | ConvertTo-Json -Depth 6
} catch {
    Write-Host "Workers error: $_"
}

if ($WorkerId) {
    Write-Host "`n--- Worker $WorkerId container logs ---"
    try {
        $logs = Invoke-RestMethod -Uri "https://api.runpod.io/v2/serverless/$epId/workers/$WorkerId/logs?source=container" -Headers $headers
        $logs | ConvertTo-Json -Depth 6
    } catch {
        Write-Host "Worker logs error: $_"
    }
}
