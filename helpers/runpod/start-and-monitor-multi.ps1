# start-and-monitor-multi.ps1 - Start one or more RunPod pods, then every N seconds for a max
# duration poll each pod's status (GraphQL) and probe its HTTP proxy URL when runtime is exposed.
# Logs transitions to console and a log file.
# Exits 0 on completion, 2 on timeout, 1 on fatal error.
param(
    [Parameter(Mandatory = $true)][string[]]$PodIds,
    [int]$IntervalSeconds = 10,
    [int]$DurationMinutes = 30,
    [string]$LogPath = ""
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

# Normalize input: accept a real string[] or a single comma-separated string.
$PodIds = @($PodIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($PodIds.Count -eq 0) { throw "Provide at least one PodId." }

if (-not $LogPath) {
    $LogPath = Join-Path (Join-Path $PSScriptRoot "..\..\artifacts\tmp\runpod") "start-monitor-multi-$([string]::Join('-', $PodIds)).log"
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

function Get-PodSnapshot {
    param([string]$PodId)
    $r = Invoke-GraphQL -Query 'query($id: String!){ pod(input:{podId:$id}){ id name desiredStatus runtime{ ports{ ip privatePort publicPort type } } } }' -Vars @{ id = $PodId }
    return $r.data.pod
}

function Get-ProxyUrl {
    param($Pod)
    if (-not $Pod -or -not $Pod.runtime -or -not $Pod.runtime.ports) { return $null }
    $httpPort = $Pod.runtime.ports | Where-Object { $_.type -eq "http" } | Select-Object -First 1
    if (-not $httpPort) { $httpPort = $Pod.runtime.ports | Select-Object -First 1 }
    if (-not $httpPort -or -not $httpPort.publicPort) { return $null }
    return "https://$($Pod.id)-$($httpPort.publicPort).proxy.runpod.net"
}

# Start all pods first.
foreach ($PodId in $PodIds) {
    try {
        $res = Invoke-GraphQL -Query 'mutation($id: String!, $gpuCount: Int!){ podResume(input:{podId:$id, gpuCount:$gpuCount}){ id desiredStatus } }' -Vars @{ id = $PodId; gpuCount = 1 }
        if ($res.errors) {
            Write-Log "START $PodId FAILED -> $($res.errors[0].message)"
        } else {
            $desired = $res.data.podResume.desiredStatus
            Write-Log "START $PodId -> desiredStatus=$desired"
        }
    } catch {
        Write-Log "START $PodId FAILED -> $($_.Exception.Message)"
    }
}

$deadline = (Get-Date).AddMinutes($DurationMinutes)
$attempt = 0
$connected = @{}
$resumeFailLogged = @{}

Write-Log "=== start-and-monitor-multi for [$($PodIds -join ', ')] (interval=$IntervalSeconds s, duration=$DurationMinutes min, deadline=$($deadline.ToString('yyyy-MM-dd HH:mm:ss'))) ==="

while ((Get-Date) -lt $deadline) {
    $attempt++
    $parts = @()
    foreach ($PodId in $PodIds) {
        $tag = "[$PodId]"
        try {
            $pod = Get-PodSnapshot -PodId $PodId
            $status = if ($pod -and $pod.desiredStatus) { $pod.desiredStatus } else { "UNKNOWN" }
            # Retry the start each cycle in case GPU capacity frees up (pods not yet running/reachable).
            if ($status -ne "RUNNING" -and -not $connected[$PodId]) {
                try {
                    $res = Invoke-GraphQL -Query 'mutation($id: String!, $gpuCount: Int!){ podResume(input:{podId:$id, gpuCount:$gpuCount}){ id desiredStatus } }' -Vars @{ id = $PodId; gpuCount = 1 }
                    if ($res.errors) {
                        if (-not $resumeFailLogged[$PodId]) {
                            $resumeFailLogged[$PodId] = $true
                            Write-Log "attempt #$attempt $tag resume-FAILED: $($res.errors[0].message)"
                        }
                    }
                } catch {
                    if (-not $resumeFailLogged[$PodId]) {
                        $resumeFailLogged[$PodId] = $true
                        Write-Log "attempt #$attempt $tag resume-FAILED: $($_.Exception.Message)"
                    }
                }
            }
            $url = Get-ProxyUrl -Pod $pod
            if ($url) {
                if (-not $connected[$PodId]) {
                    try {
                        $resp = Invoke-WebRequest -Uri "$url/system_stats" -UseBasicParsing -TimeoutSec 8
                        $connected[$PodId] = $true
                        Write-Log "attempt #$attempt $tag status=$status CONNECTED url=$url (HTTP $($resp.StatusCode))"
                        $parts += "$tag=RUNNING,reachable"
                        continue
                    } catch {
                        $parts += "$tag=$status,runtime-exposed-probe-fail"
                        continue
                    }
                }
                $parts += "$tag=$status,reachable"
            } else {
                $parts += "$tag=$status,no-runtime"
            }
        } catch {
            $parts += "$tag=STATUS-FAILED"
        }
    }
    Add-Content -Path $LogPath -Value ("{0}  attempt #{1} : {2}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $attempt, ($parts -join " | "))
    if ($attempt % 6 -eq 1) {
        Write-Output ("attempt #{0} : {1}" -f $attempt, ($parts -join " | "))
    }

    $remaining = ($deadline - (Get-Date)).TotalSeconds
    if ($remaining -gt 0) {
        $sleep = [Math]::Min($IntervalSeconds, [Math]::Max(1, [int]$remaining))
        Start-Sleep -Seconds $sleep
    }
}

Write-Log "=== COMPLETE after $DurationMinutes min. Final state: ==="
foreach ($PodId in $PodIds) {
    $state = if ($connected[$PodId]) { "REACHABLE" } else { "NOT-REACHABLE" }
    Write-Log "$PodId : $state"
}
Write-Log "=== exiting 0 ==="
exit 0
