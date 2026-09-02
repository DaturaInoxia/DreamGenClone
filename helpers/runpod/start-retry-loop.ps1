# start-retry-loop.ps1 - Attempt to resume a RunPod pod every N seconds for a max duration.
# Exits 0 on success (pod RUNNING), 2 on timeout, 1 on fatal error.
param(
    [Parameter(Mandatory = $true)][string]$PodId,
    [int]$IntervalSeconds = 10,
    [int]$DurationMinutes = 30,
    [string]$LogPath = ""
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

if (-not $LogPath) {
    $LogPath = Join-Path (Join-Path $PSScriptRoot "..\..\artifacts\tmp\runpod") "start-retry-$PodId.log"
}
$logDir = Split-Path -Parent $LogPath
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }

function Write-Log {
    param([string]$Msg)
    $line = "{0}  {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Msg
    Add-Content -Path $LogPath -Value $line
    Write-Output $line
}

$apiUrl = "https://api.runpod.io/graphql"

function Invoke-GraphQL {
    param([string]$Query, [hashtable]$Vars = @{})
    $body = @{ query = $Query; variables = $Vars } | ConvertTo-Json -Depth 10
    Invoke-RestMethod -Uri $apiUrl -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" } -Body $body
}

function Get-PodStatus {
    $r = Invoke-GraphQL -Query 'query($id: String!){ pod(input:{podId:$id}){ id name desiredStatus runtime{ ports{ ip privatePort publicPort type } } } }' -Vars @{ id = $PodId }
    $pod = $r.data.pod
    return $pod
}

$deadline = (Get-Date).AddMinutes($DurationMinutes)
$attempt = 0

Write-Log "=== start-retry-loop for pod $PodId (interval=$IntervalSeconds s, duration=$DurationMinutes min, deadline=$($deadline.ToString('yyyy-MM-dd HH:mm:ss'))) ==="

while ((Get-Date) -lt $deadline) {
    $attempt++
    try {
        $res = Invoke-GraphQL -Query 'mutation($id: String!, $gpuCount: Int!){ podResume(input:{podId:$id, gpuCount:$gpuCount}){ id desiredStatus } }' -Vars @{ id = $PodId; gpuCount = 1 }
        $desired = $res.data.podResume.desiredStatus
        Write-Log "attempt #$attempt : podResume OK -> desiredStatus=$desired"
    } catch {
        Write-Log "attempt #$attempt : podResume FAILED -> $($_.Exception.Message)"
    }

    # Give RunPod a moment to transition, then check real status.
    try {
        Start-Sleep -Seconds 3
        $pod = Get-PodStatus
        $status = if ($pod) { $pod.desiredStatus } else { "UNKNOWN" }
        $hasRuntime = if ($pod -and $pod.runtime -and $pod.runtime.ports) { $true } else { $false }
        Write-Log "attempt #$attempt : status=$status runtimeExposed=$hasRuntime"

        if ($status -eq "RUNNING" -and $hasRuntime) {
            Write-Log "SUCCESS: pod $PodId is RUNNING with runtime exposed."
            Write-Log "=== exiting 0 ==="
            exit 0
        }
        if ($status -eq "RUNNING") {
            Write-Log "pod $PodId desiredStatus=RUNNING (runtime not yet exposed), continuing to verify..."
        }
    } catch {
        Write-Log "attempt #$attempt : status check FAILED -> $($_.Exception.Message)"
    }

    $remaining = ($deadline - (Get-Date)).TotalSeconds
    if ($remaining -gt 0) {
        $sleep = [Math]::Min($IntervalSeconds, [Math]::Max(1, [int]$remaining))
        Start-Sleep -Seconds $sleep
    }
}

Write-Log "TIMEOUT: pod $PodId did not reach RUNNING within $DurationMinutes minutes."
Write-Log "=== exiting 2 ==="
exit 2
