# create-endpoint.ps1 - create a RunPod Serverless endpoint from endpoints.json
#
# B-101. Serverless workers have no SSH; "provisioning" = creating the endpoint.
# Uses the RunPod REST API v2 (NOT GraphQL - the legacy `createEndpoint` mutation no
# longer exists on the Mutation type).
# Docs: https://docs.runpod.io/api-reference-v2/serverless/create-a-serverless-endpoint.md
#
# P0 VERIFIED 2026-08-28: REST v2 `POST /v2/serverless` shape confirmed from RunPod docs.
# - `gpu.pools` takes SERVERLESS POOL IDs (e.g. AMPERE_16), NOT GPU type IDs; resolved
#   live from `GET /v2/catalog/gpus` (pool field).
# - `type` (QUEUE) and `scaling` (QUEUE_DELAY) are required.
# - Returns 201 with `id` + `requestUrls`.
#
# Usage:
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/create-endpoint.ps1 `
#     -EndpointKey pose-dwpose-serverless -DryRun
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/create-endpoint.ps1 `
#     -EndpointKey pose-dwpose-serverless
param(
    [Parameter(Mandatory = $true)][string]$EndpointKey,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
Set-Location $repoRoot

. (Join-Path $PSScriptRoot "..\common.ps1")
Get-RunPodEnv

$registry = Get-Content -Raw -Path (Join-Path $PSScriptRoot "endpoints.json") | ConvertFrom-Json
$entry = $registry.endpoints | Where-Object { $_.endpointKey -eq $EndpointKey }
if ($null -eq $entry) { throw "endpoints.json has no entry for '$EndpointKey'." }

$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }

# Resolve the serverless GPU POOL for the requested GPU type (endpoints.json gpuTypeId
# is the catalog `name`, e.g. "RTX 4000 Ada"). gpu.pools needs the pool ID, not the type ID.
$catalog = Invoke-RestMethod -Uri "https://api.runpod.io/v2/catalog/gpus" -Headers $headers -TimeoutSec 30
$gpu = $catalog.gpus | Where-Object { $_.name -eq $entry.gpuTypeId } | Select-Object -First 1
if ($null -eq $gpu) { throw "No catalog GPU matches name '$($entry.gpuTypeId)'. Check endpoints.json." }
if ([string]::IsNullOrWhiteSpace($gpu.pool)) { throw "GPU '$($entry.gpuTypeId)' has no serverless pool (pool='$($gpu.pool)'). Pick a different GPU." }
$pool = [string]$gpu.pool

# Optional fields read from the endpoints.json entry (only added to the payload when present):
#   networkVolumes : array of Network Volume IDs (attaching a volume pins workers to its DC)
#   dataCenterIds  : array of DC IDs to constrain placement to
#   timeoutMs      : per-JOB execution cap in ms (default 300000 = 5 min, too short for cold starts)
#   flashboot      : "FLASHBOOT" / "PRIORITY_FLASHBOOT" / "OFF" (cold-start acceleration)
$bodyObj = @{
    name  = $EndpointKey
    image = [string]$entry.workerImage
    type  = "QUEUE"
    gpu   = @{ pools = @($pool); count = 1 }
    workers = @{
        min         = [int]$entry.minWorkers
        max         = [int]$entry.maxWorkers
        idleTimeout = [int]$entry.idleTimeoutSec
    }
    scaling = @{ type = "QUEUE_DELAY"; queueDelay = 4 }
    timeout = if ($null -ne $entry.timeoutMs) { [int]$entry.timeoutMs } else { 300000 }
}
if ($entry.PSObject.Properties.Name -contains 'networkVolumes' -and $entry.networkVolumes) {
    $bodyObj.networkVolumes = @($entry.networkVolumes)
}
if ($entry.PSObject.Properties.Name -contains 'dataCenterIds' -and $entry.dataCenterIds) {
    $bodyObj.dataCenterIds = @($entry.dataCenterIds)
}
if ($entry.PSObject.Properties.Name -contains 'flashboot' -and $entry.flashboot) {
    $bodyObj.flashboot = [string]$entry.flashboot
}
$body = $bodyObj | ConvertTo-Json -Depth 8

Write-Host "============================================================"
Write-Host "Create Serverless endpoint: $($entry.function) [$EndpointKey]"
Write-Host "  image      : $($entry.workerImage)"
Write-Host "  gpu        : $($entry.gpuTypeId)  pool=$pool  srv=$($gpu.price.serverless)/hr"
Write-Host "  workers    : min=$($entry.minWorkers) max=$($entry.maxWorkers) idle=$($entry.idleTimeoutSec)s  (QUEUE / QUEUE_DELAY)"
if ($bodyObj.ContainsKey('networkVolumes')) { Write-Host "  volume     : $($bodyObj.networkVolumes -join ',')" }
if ($bodyObj.ContainsKey('dataCenterIds'))  { Write-Host "  dc         : $($bodyObj.dataCenterIds -join ',')" }
if ($bodyObj.ContainsKey('flashboot'))      { Write-Host "  flashboot  : $($bodyObj.flashboot)" }
Write-Host "  timeout    : $($bodyObj.timeout) ms (per job)"
Write-Host "============================================================"
Write-Host "REST v2 payload (POST https://api.runpod.io/v2/serverless):"
Write-Host $body

if ($DryRun) {
    Write-Host "`nDRY RUN - no endpoint created. Re-run without -DryRun to create."
    exit 0
}

$confirm = Read-Host "Create this endpoint now? (billable) type 'yes' to continue"
if ($confirm -ne "yes") { Write-Host "Aborted."; exit 1 }

$resp = Invoke-RestMethod -Uri "https://api.runpod.io/v2/serverless" -Method POST -Headers $headers -ContentType "application/json" -Body $body -TimeoutSec 60
$resp | ConvertTo-Json -Depth 12
Write-Host "`nRecord the endpoint id in endpoints.json ($EndpointKey.endpointId): $($resp.id)"
if ($resp.requestUrls) { Write-Host "run url: $($resp.requestUrls.run)" }
