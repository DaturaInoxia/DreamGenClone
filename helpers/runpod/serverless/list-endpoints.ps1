# list-endpoints.ps1 - List RunPod Serverless endpoints and their current release/build status.
# Usage: list-endpoints.ps1 [-NameFilter <substring>]
param(
    [Parameter(Mandatory=$false)][string]$NameFilter = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "..\.runpod-env.ps1")

$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
$raw = Invoke-WebRequest -Uri "https://api.runpod.io/v2/serverless" -Headers $headers -UseBasicParsing
Write-Host "HTTP $($raw.StatusCode)"
Write-Host "RAW: $($raw.Content)"
$eps = $raw.Content | ConvertFrom-Json

# API returns an array, but PowerShell unwraps single-element arrays -> normalize.
if ($eps -isnot [array]) { $eps = @($eps) }

foreach ($ep in $eps) {
    if ($NameFilter -and $ep.name -notmatch $NameFilter) { continue }
    Write-Host "=== $($ep.name) ($($ep.id)) ==="
    Write-Host "  image:      $($ep.image)"
    Write-Host "  timeout:    $($ep.timeout)  idle: $($ep.workers.idleTimeout)  max: $($ep.workers.max)"
    Write-Host "  pools:      $($ep.gpu.pools -join ', ')"
    try {
        $rel = Invoke-RestMethod -Uri "https://api.runpod.io/v2/serverless/$($ep.id)/releases" -Headers $headers
        Write-Host "  releases:   $($rel | ConvertTo-Json -Depth 6 -Compress)"
    } catch {
        Write-Host "  releases:   (error: $($_.Exception.Message))"
    }
    Write-Host ""
}
