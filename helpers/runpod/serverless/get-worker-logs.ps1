# get-worker-logs.ps1 - Fetch a RunPod Serverless worker's logs (container + system).
# Usage: get-worker-logs.ps1 -EndpointKey <key> -WorkerId <id>
param(
    [Parameter(Mandatory=$true)][string]$EndpointKey,
    [Parameter(Mandatory=$true)][string]$WorkerId
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "..\.runpod-env.ps1")

$reg = Get-Content (Join-Path $scriptDir "endpoints.json") -Raw | ConvertFrom-Json
$ep = $reg.endpoints | Where-Object { $_.endpointKey -eq $EndpointKey }
if (-not $ep) { throw "Endpoint '$EndpointKey' not found in endpoints.json" }
$epId = $ep.endpointId

$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
$base = "https://api.runpod.io/v2/serverless/$epId/workers/$WorkerId/logs"

foreach ($src in @("container", "system")) {
    Write-Host "`n=== source=$src ==="
    $url = "https://api.runpod.io/v2/serverless/$epId/workers/$WorkerId/logs?source=$src&limit=200"
    & curl.exe -s --max-time 60 -H "Authorization: Bearer $env:RUNPOD_API_KEY" $url
    Write-Host ""
}
