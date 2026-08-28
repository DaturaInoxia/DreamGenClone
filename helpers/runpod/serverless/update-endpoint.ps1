# update-endpoint.ps1 - PATCH a RunPod Serverless endpoint config (billable action, no prompt).
# Usage: update-endpoint.ps1 -EndpointKey <key> [-TimeoutMs <ms>] [-Image <tag>] [-Pools <comma-list>] [-MaxWorkers <n>] [-IdleTimeoutSec <n>]
param(
    [Parameter(Mandatory=$true)][string]$EndpointKey,
    [Parameter(Mandatory=$false)][string]$TimeoutMs = "",
    [Parameter(Mandatory=$false)][string]$Image = "",
    [Parameter(Mandatory=$false)][string]$Pools = "",
    [Parameter(Mandatory=$false)][string]$MaxWorkers = "",
    [Parameter(Mandatory=$false)][string]$IdleTimeoutSec = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "..\.runpod-env.ps1")

$reg = Get-Content (Join-Path $scriptDir "endpoints.json") -Raw | ConvertFrom-Json
$ep = $reg.endpoints | Where-Object { $_.endpointKey -eq $EndpointKey }
if (-not $ep) { throw "Endpoint '$EndpointKey' not found in endpoints.json" }
$epId = $ep.endpointId

$body = @{}
if ($TimeoutMs)  { $body.timeout = [int]$TimeoutMs }
if ($Image)      { $body.image = $Image }
if ($Pools)      { $body.gpu = @{ pools = $Pools -split ","; count = 1 } }
if ($MaxWorkers) { $body.workers = @{ min = 0; max = [int]$MaxWorkers } }
if ($IdleTimeoutSec) { if ($body.workers) { $body.workers.idleTimeout = [int]$IdleTimeoutSec } else { $body.workers = @{ idleTimeout = [int]$IdleTimeoutSec } } }

if ($body.Count -eq 0) { throw "Nothing to update - pass at least one of -TimeoutMs/-Image/-Pools/-MaxWorkers/-IdleTimeoutSec" }

Write-Host "PATCH /v2/serverless/$epId"
Write-Host "Body: $($body | ConvertTo-Json -Depth 6)"

$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
try {
    $r = Invoke-RestMethod -Method Patch -Uri "https://api.runpod.io/v2/serverless/$epId" -Headers $headers -ContentType "application/json" -Body ($body | ConvertTo-Json -Depth 6)
    $r | ConvertTo-Json -Depth 8
} catch {
    Write-Host "PATCH failed: $_"
    exit 1
}
