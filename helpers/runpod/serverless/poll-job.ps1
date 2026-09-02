# poll-job.ps1 - Poll a RunPod Serverless job until COMPLETED/FAILED/CANCELLED.
# Usage: poll-job.ps1 -EndpointKey <key> -JobId <job_id> [-MaxPolls 90] [-IntervalSec 10]
param(
    [Parameter(Mandatory=$true)][string]$EndpointKey,
    [Parameter(Mandatory=$true)][string]$JobId,
    [Parameter(Mandatory=$false)][int]$MaxPolls = 90,
    [Parameter(Mandatory=$false)][int]$IntervalSec = 10
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "..\.runpod-env.ps1")

$reg = Get-Content (Join-Path $scriptDir "endpoints.json") -Raw | ConvertFrom-Json
$ep = $reg.endpoints | Where-Object { $_.endpointKey -eq $EndpointKey }
if (-not $ep) { throw "Endpoint '$EndpointKey' not found in endpoints.json" }
$epId = $ep.endpointId

$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
$uri = "https://api.runpod.ai/v2/$epId/status/$JobId"

for ($i = 1; $i -le $MaxPolls; $i++) {
    $s = Invoke-RestMethod -Uri $uri -Headers $headers
    $execMs = if ($s.executionTime) { $s.executionTime } else { "-" }
    Write-Host "poll $i : $($s.status) (exec ${execMs}ms)"
    if ($s.status -in @('COMPLETED','FAILED','CANCELLED')) {
        Write-Host "`n--- Final ---"
        $s | ConvertTo-Json -Depth 10
        exit 0
    }
    Start-Sleep -Seconds $IntervalSec
}
Write-Host "TIMEOUT after $($MaxPolls * $IntervalSec) sec - job still running. Check RunPod console."
exit 1
