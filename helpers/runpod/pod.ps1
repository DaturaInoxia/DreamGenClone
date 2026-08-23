# pod.ps1 - RunPod pod status/start/stop and (confirmed) terminate
param(
    [Parameter(Mandatory=$false)][ValidateSet("status","start","stop","terminate","usage")][string]$Action = "status",
    [Parameter(Mandatory=$false)][string]$PodId = $env:RUNPOD_POD_ID,
    [Parameter(Mandatory=$false)][int]$GpuCount = 1
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

# Param default for $PodId is bound before Get-RunPodEnv loads env file, so
# fall back to the env value now.
if (!$PodId) { $PodId = $env:RUNPOD_POD_ID }
if (-not $PodId) { throw "Set PodId or RUNPOD_POD_ID in .runpod-env.ps1." }

# These calls use the RunPod GraphQL endpoint. URL/fields may need updating to match your account.
$apiUrl = "https://api.runpod.io/graphql"

function Invoke-GraphQL {
    param([string]$Query, [hashtable]$Vars = @{})
    $body = @{ query = $Query; variables = $Vars } | ConvertTo-Json -Depth 10
    Invoke-RestMethod -Uri $apiUrl -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" } -Body $body
}

switch ($Action) {
    "status" {
        $r = Invoke-GraphQL -Query 'query($id: String!){ pod(input:{podId:$id}){ id name desiredStatus uptimeSeconds gpuCount machine{ gpuDisplayName } runtime{ ports{ ip privatePort publicPort type } } } }' -Vars @{ id = $PodId }
        $r | ConvertTo-Json -Depth 10
    }
    "start" {
        Invoke-GraphQL -Query 'mutation($id: String!, $gpuCount: Int!){ podResume(input:{podId:$id, gpuCount:$gpuCount}){ id desiredStatus } }' -Vars @{ id = $PodId; gpuCount = $GpuCount }
        Write-Host "Sent start (resume) for $PodId with gpuCount=$GpuCount"
    }
    "stop" {
        Invoke-GraphQL -Query 'mutation($id: String!){ podStop(input:{podId:$id}){ id desiredStatus } }' -Vars @{ id = $PodId }
        Write-Host "Sent stop for $PodId"
    }
    "usage" {
        $r = Invoke-RestMethod -Uri "https://api.runpod.io/usage" -Method GET -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
        $r | ConvertTo-Json -Depth 10
    }
    "terminate" {
        Write-Host ""
        Write-Host "WARNING: Terminating pod '$PodId' is irreversible and may delete its volume data."
        $confirm = Read-Host "Type the exact pod id to confirm, or press Enter to abort"
        if ($confirm -ne $PodId) {
            Write-Host "Aborted. Provide the exact pod id to terminate."
            exit 1
        }
        Invoke-GraphQL -Query 'mutation($id: String!){ podTerminate(input:{podId:$id}) }' -Vars @{ id = $PodId }
        Write-Host "Terminated $PodId"
    }
    default { throw "Unsupported action $Action" }
}