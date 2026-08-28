# get-available-gpus.ps1 - query the current RunPod GPU catalog + pricing.
#
# There is NO RunPod REST "availability" endpoint. GPU selection is done by passing an
# ORDERED list of candidate gpuTypeIds to the pod-create API (gpuTypePriority=custom) and
# letting RunPod rent the first GPU with current capacity. This script provides the catalog
# (VRAM + secure/community pricing) used to build those candidate lists.
#
# Usage:
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1 -Cloud secure
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1 -MinVramGb 40
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1 -SortByPrice
param(
    [ValidateSet("secure", "community", "all")]
    [string]$Cloud = "all",
    [int]$MinVramGb = 0,
    [switch]$SortByPrice
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

$body = @{
    query = 'query { gpuTypes { id displayName memoryInGb securePrice communityPrice secureCloud communityCloud maxGpuCount } }'
} | ConvertTo-Json -Depth 10
$r = Invoke-RestMethod -Uri "https://api.runpod.io/graphql" -Method POST `
    -ContentType "application/json" -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" } -Body $body
if ($r.errors) { throw (($r.errors | ConvertTo-Json -Depth 6 -Compress)) }

$rows = foreach ($g in $r.data.gpuTypes) {
    [PSCustomObject]@{
        id             = $g.id
        displayName    = $g.displayName
        memoryInGb     = $g.memoryInGb
        securePrice    = $g.securePrice
        communityPrice = $g.communityPrice
        secureCloud    = $g.secureCloud
        communityCloud = $g.communityCloud
    }
}

if ($MinVramGb -gt 0) { $rows = $rows | Where-Object { [int]$_.memoryInGb -ge $MinVramGb } }
if ($Cloud -eq "secure")    { $rows = $rows | Where-Object { $_.secureCloud } }
if ($Cloud -eq "community") { $rows = $rows | Where-Object { $_.communityCloud } }
if ($SortByPrice) { $rows = $rows | Sort-Object @{Expression = { $_.securePrice }; Ascending = $true} }

$rows | Format-Table -AutoSize id, displayName, memoryInGb, securePrice, communityPrice | Out-String -Width 120

