# list-pods.ps1 - list all RunPod account pods (id, name, status, GPU, runtime ports)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

$apiUrl = "https://api.runpod.io/graphql"

function Invoke-GraphQL {
    param([string]$Query, [hashtable]$Vars = @{})
    $body = @{ query = $Query; variables = $Vars } | ConvertTo-Json -Depth 10
    Invoke-RestMethod -Uri $apiUrl -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" } -Body $body
}

$r = Invoke-GraphQL -Query 'query { myself { pods { id name desiredStatus imageName gpuCount machine { gpuDisplayName } runtime { ports { ip privatePort publicPort type } uptimeInSeconds } } } }'

$rows = foreach ($pod in $r.data.myself.pods) {
    $httpPorts = @($pod.runtime.ports | Where-Object { $_.type -eq "http" })
    $tcp = $pod.runtime.ports | Where-Object { $_.type -eq "tcp" } | Select-Object -First 1
    # A pod may expose multiple HTTP ports (e.g. the configured inference port plus the base
    # ComfyUI on 19123). Each is a valid proxy endpoint https://<podId>-<privatePort>.proxy.runpod.net
    $httpList = @()
    foreach ($h in $httpPorts) {
        $httpList += "https://$($pod.id)-$($h.privatePort).proxy.runpod.net"
    }
    [PSCustomObject]@{
        Id       = $pod.id
        Name     = $pod.name
        Status   = $pod.desiredStatus
        Gpu      = $pod.machine.gpuDisplayName
        GpuCount = $pod.gpuCount
        Http     = ($httpList -join "`n")
        TcpPort  = if ($tcp) { "$($tcp.publicPort)" } else { "" }
        UptimeSec = $pod.runtime.uptimeInSeconds
    }
}
$rows | Format-List Id, Name, Status, Gpu, GpuCount, Http, TcpPort, UptimeSec | Out-String -Width 200
