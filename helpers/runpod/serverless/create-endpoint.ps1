# create-endpoint.ps1 - create a RunPod Serverless endpoint from endpoints.json
#
# B-101. Serverless workers have no SSH; "provisioning" = creating the endpoint.
# This drives the RunPod GraphQL `createEndpoint` mutation using the API key from
# helpers/runpod/.runpod-env.ps1.
#
# P0 GATE: the exact mutation field names (workersMin/workersMax/idleTimeout/gpuIds/
# container.env) MUST be verified against the live RunPod serverless API/console before
# the first real create. This script prints the intended payload with -DryRun and never
# creates anything without explicit confirmation (billable).
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

$containerJson = @{
    image = [string]$entry.workerImage
    env   = @{}
} | ConvertTo-Json -Depth 5 -Compress

# P0 GATE: verify these GraphQL field names against the live schema before first use.
$mutation = @"
mutation CreateEndpoint {
  createEndpoint(
    input: {
      name: "$EndpointKey"
      gpuIds: ["$($entry.gpuTypeId)"]
      workersMin: $($entry.minWorkers)
      workersMax: $($entry.maxWorkers)
      idleTimeout: $($entry.idleTimeoutSec)
      container: $containerJson
    }
  ) {
    id
    name
    status
  }
}
"@

Write-Host "============================================================"
Write-Host "Create Serverless endpoint: $($entry.function) [$EndpointKey]"
Write-Host "  image      : $($entry.workerImage)"
Write-Host "  gpu        : $($entry.gpuTypeId)  min=$($entry.minWorkers) max=$($entry.maxWorkers) idle=$($entry.idleTimeoutSec)s"
Write-Host "============================================================"
Write-Host "Mutation (P0: verify schema before running):"
Write-Host $mutation

if ($DryRun) {
    Write-Host "`nDRY RUN - no endpoint created. Verify the GraphQL schema, then re-run without -DryRun."
    exit 0
}

$confirm = Read-Host "Create this endpoint now? (billable) type 'yes' to continue"
if ($confirm -ne "yes") { Write-Host "Aborted."; exit 1 }

$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
$body = @{ query = $mutation } | ConvertTo-Json -Depth 5
$resp = Invoke-RestMethod -Uri "https://api.runpod.io/graphql" -Method POST -Headers $headers -ContentType "application/json" -Body $body
$resp | ConvertTo-Json -Depth 10
Write-Host "`nRecord the returned endpoint id in endpoints.json ($EndpointKey.endpointId) once verified."
