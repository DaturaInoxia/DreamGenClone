# watch-scale-down.ps1 - poll a RunPod serverless endpoint until it scales to zero workers.
# Verifies the endpoint honors idleTimeout (scale-to-zero). Prints state changes; exits 0 when
# total workers == 0 OR when all workers are Idle (RunPod docs: Idle = "scaled down", NOT billed;
# flex workers with minWorkers=0 keep their worker slot at total=1 while idle, so total=0 never
# happens for them). Exits 1 if it is still Running (billing) at MaxMinutes.
#
# Usage:
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/watch-scale-down.ps1 `
#     -EndpointKey img-juggernaut-serverless -MaxMinutes 40
param(
    [Parameter(Mandatory = $true)][string]$EndpointKey,
    [int]$MaxMinutes = 40,
    [int]$PollSeconds = 60,
    [switch]$RequireTotalZero   # strictly require total==0 (default: Idle counts as scaled-down)
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
Set-Location $repoRoot

. (Join-Path $PSScriptRoot "..\common.ps1")
Get-RunPodEnv

$registry = Get-Content -Raw -Path (Join-Path $PSScriptRoot "endpoints.json") | ConvertFrom-Json
$entry = $registry.endpoints | Where-Object { $_.endpointKey -eq $EndpointKey }
if ($null -eq $entry -or [string]::IsNullOrWhiteSpace($entry.endpointId)) {
    throw "endpoints.json has no endpointId for '$EndpointKey'."
}
$endpointId = $entry.endpointId
$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }

Write-Host "Watching $EndpointKey ($endpointId) for scale-to-zero (idleTimeout $($entry.idleTimeoutSec)s)..."
$deadline = (Get-Date).AddMinutes($MaxMinutes)
$last = ""
while ((Get-Date) -lt $deadline) {
    try {
        $w = Invoke-RestMethod -Uri "https://api.runpod.io/v2/serverless/$endpointId/workers" -Headers $headers -TimeoutSec 30
        $s = $w.summary
        $state = "total=$($s.total) idle=$($s.idle) running=$($s.running) initializing=$($s.initializing) unhealthy=$($s.unhealthy)"
        if ($state -ne $last) {
            Write-Host "$(Get-Date -Format 'HH:mm:ss') $state"
            $last = $state
        }
        if ($s.total -eq 0 -or (-not $RequireTotalZero -and $s.total -gt 0 -and $s.idle -eq $s.total -and $s.running -eq 0 -and $s.initializing -eq 0)) {
            if ($s.total -eq 0) {
                Write-Host "SCALED DOWN: zero workers (idleTimeout honored)."
            } else {
                Write-Host "SCALED DOWN: all workers Idle (total=$($s.total)) - per RunPod docs Idle = scaled down, NOT billed. idleTimeout honored."
            }
            exit 0
        }
    } catch {
        Write-Host "$(Get-Date -Format 'HH:mm:ss') poll error: $($_.Exception.Message)"
    }
    Start-Sleep -Seconds $PollSeconds
}
Write-Host "TIMEOUT after $MaxMinutes min: worker did NOT scale down. Investigate idleTimeout/stuck job."
exit 1
