# get-available-gpus.ps1 - query the current RunPod GPU catalog + pricing, and (with
# -IncludeAvailability) LIVE per-data-center stock.
#
# CORRECTED 2026-10-02: this file previously claimed "There is NO RunPod REST availability
# endpoint". That is no longer true. RunPod's v2 catalog API reports availability
# (NONE/LOW/MEDIUM/HIGH) per GPU AND per data center:
#     GET https://api.runpod.io/v2/catalog/gpus?include=AVAILABILITY&product=<POD|SERVERLESS|CLUSTER>
#     GET https://api.runpod.io/v2/catalog/datacenters?include=GPU_AVAILABILITY
# `include=AVAILABILITY` REQUIRES `product` (400 otherwise): stock differs per product, so the
# context must be stated rather than assumed. Use -IncludeAvailability to READ availability
# instead of guessing, or instead of paying for an empirical create-and-see probe.
#
# An ordered candidate list is still the right hedge at creation time - stock changes
# constantly - but the list no longer has to be blind.
#
# Usage:
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1 -Cloud secure
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1 -MinVramGb 40
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1 -SortByPrice
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1 -IncludeAvailability -Product SERVERLESS -MinVramGb 48
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/get-available-gpus.ps1 -IncludeAvailability -Product SERVERLESS -DataCenter EU-RO-1
param(
    [ValidateSet("secure", "community", "all")]
    [string]$Cloud = "all",
    [int]$MinVramGb = 0,
    [switch]$SortByPrice,
    [switch]$IncludeAvailability,
    [ValidateSet("POD", "SERVERLESS", "CLUSTER")]
    [string]$Product = "SERVERLESS",
    [string]$DataCenter = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

if ($IncludeAvailability) {
    $qs = "include=AVAILABILITY&product=$Product"
    if ($Cloud -eq "secure")    { $qs += "&cloud=SECURE" }
    if ($Cloud -eq "community") { $qs += "&cloud=COMMUNITY" }
    $cat = (Invoke-WebRequest -Uri "https://api.runpod.io/v2/catalog/gpus?$qs" -UseBasicParsing -TimeoutSec 60 `
        -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }).Content | ConvertFrom-Json

    $rows = foreach ($g in $cat.gpus) {
        $dcs = @($g.dataCenters)
        if ($DataCenter) { $dcs = @($dcs | Where-Object { $_.id -eq $DataCenter }) }
        if ($DataCenter -and $dcs.Count -eq 0) { continue }
        [PSCustomObject]@{
            id           = $g.id
            displayName  = $g.name
            memoryInGb   = $g.memory
            pool         = $g.pool
            securePrice  = $g.price.secure
            availability = $g.availability
            dataCenters  = (($dcs | Where-Object { $_.availability -ne 'NONE' } |
                              ForEach-Object { "$($_.id):$($_.availability)" }) -join ' ')
        }
    }
    if ($MinVramGb -gt 0) { $rows = $rows | Where-Object { [int]$_.memoryInGb -ge $MinVramGb } }
    $rows | Sort-Object memoryInGb, displayName |
        Format-Table -AutoSize id, displayName, memoryInGb, pool, availability, dataCenters |
        Out-String -Width 220
    return
}

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

