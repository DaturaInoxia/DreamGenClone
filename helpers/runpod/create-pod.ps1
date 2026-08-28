# create-pod.ps1 - create a FRESH RunPod pod from a deployment manifest with automatic GPU
# selection. Used by the runpod-pod-creation skill when a pod cannot be started and RunPod
# migration fails (no available GPU). This is the ONLY sanctioned pod-creation entry point.
#
# GPU selection: pass -GpuTypeIds (ordered, cheapest-first that meets the pod's VRAM tier) and the
# API uses gpuTypePriority=custom to rent the FIRST GPU with current capacity. If no candidate has
# capacity the create fails with RunPod's "no available GPUs" error and no pod is created.
#
# After a successful create the manifest's podId (and gpuTypeId) are updated to the new pod and the
# previous pod id is recorded in manifest.previousPodIds. The old (dead) pod is never terminated
# without explicit approval.
#
# Usage:
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/create-pod.ps1 `
#     -ManifestPath helpers/runpod/deployments/image-gen-juggernaut/deployment.json
#   # ...or explicit ordered GPU candidates (single string array):
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/create-pod.ps1 `
#     -ManifestPath <manifest> -GpuTypeIds "NVIDIA A40","NVIDIA RTX A6000","NVIDIA L40"
param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [string[]]$GpuTypeIds,
    [string]$GpuTypeIdsCsv,
    [ValidateSet("SECURE", "COMMUNITY")][string]$CloudType = "SECURE",
    [int]$RuntimeTimeoutSeconds = 1800,
    [int]$PollIntervalSeconds = 15
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
foreach ($p in @("deploymentKey", "podName", "containerImage", "inferencePort", "volumeInGb", "containerDiskInGb", "gpuCount")) {
    if ([string]::IsNullOrWhiteSpace([string]$manifest.$p)) { throw "Manifest '$ManifestPath' is missing property '$p'." }
}

# --- guard: never create a duplicate while the same-named pod is already running ---
$podsBody = @{ query = 'query { myself { pods { id name desiredStatus } } }' } | ConvertTo-Json -Depth 6
$pods = (Invoke-RestMethod -Uri "https://api.runpod.io/graphql" -Method POST -ContentType "application/json" `
    -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" } -Body $podsBody).data.myself.pods
$runningSameName = @($pods | Where-Object { [string]$_.name -eq [string]$manifest.podName -and [string]$_.desiredStatus -eq "RUNNING" })
if ($runningSameName.Count -gt 0) {
    throw "A RUNNING pod named '$($manifest.podName)' already exists ($($runningSameName[0].id)). Refusing to create a duplicate. If it is unusable, stop/terminate it with explicit approval first."
}

# --- resolve candidate GPUs (csv > array > registry > manifest) ---
# The registry candidateGpuTypeIds are the cost-policy source of truth (cheapest-first that fits
# and is fast enough); manifest.gpuTypeId is only a last-resort single proven GPU.
$candidates = @()
if (-not [string]::IsNullOrWhiteSpace($GpuTypeIdsCsv)) {
    $candidates = @($GpuTypeIdsCsv -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}
elseif ($GpuTypeIds -and $GpuTypeIds.Count -gt 0) {
    $candidates = $GpuTypeIds
}
else {
    $registryPath = Join-Path $PSScriptRoot "pod-registry.json"
    if (Test-Path $registryPath) {
        $reg = Get-Content -Raw $registryPath | ConvertFrom-Json
        $entry = $reg.pods | Where-Object { [string]$_.deploymentKey -eq [string]$manifest.deploymentKey }
        if ($null -ne $entry -and $entry.candidateGpuTypeIds.Count -gt 0) {
            $candidates = @([string[]]$entry.candidateGpuTypeIds)
        }
    }
    if ($candidates.Count -eq 0 -and -not [string]::IsNullOrWhiteSpace([string]$manifest.gpuTypeId)) {
        $candidates = @([string]$manifest.gpuTypeId)
    }
}
if ($candidates.Count -eq 0) {
    throw "No GPU candidates resolved. Supply -GpuTypeIds/-GpuTypeIdsCsv, or ensure the registry candidateGpuTypeIds (or manifest.gpuTypeId) are set."
}

# --- filter candidates to those the requested cloud actually supports ---
$catalogBody = @{ query = 'query { gpuTypes { id secureCloud communityCloud } }' } | ConvertTo-Json -Depth 6
$catalog = (Invoke-RestMethod -Uri "https://api.runpod.io/graphql" -Method POST -ContentType "application/json" `
    -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" } -Body $catalogBody).data.gpuTypes
$filtered = @()
$dropped = @()
foreach ($g in $candidates) {
    $info = $catalog | Where-Object { [string]$_.id -eq [string]$g } | Select-Object -First 1
    $supports = $false
    if ($null -ne $info) {
        $supports = if ($CloudType -eq "SECURE") { [bool]$info.secureCloud } else { [bool]$info.communityCloud }
    }
    if ($supports) { $filtered += $g } else { $dropped += $g }
}
if ($dropped.Count -gt 0) {
    Write-Host "  dropped for $CloudType cloud (not supported): $($dropped -join ', ')"
}
if ($filtered.Count -eq 0) {
    throw "None of the candidate GPUs support $CloudType cloud. Supply candidates that support $CloudType or choose a different -CloudType."
}
$candidates = $filtered

Write-Host "Creating pod '$($manifest.podName)'"
Write-Host "  image      : $($manifest.containerImage)"
Write-Host "  cloud      : $CloudType"
Write-Host "  gpu order  : $($candidates -join ' -> ')"
Write-Host "  volume     : $($manifest.volumeInGb) GB at $($manifest.volumeMountPath)"
Write-Host "  ports      : $($manifest.inferencePort)/http, $($manifest.sshTcpPort)/tcp"

$request = [ordered]@{
    name                  = [string]$manifest.podName
    computeType           = "GPU"
    cloudType             = $CloudType
    imageName             = [string]$manifest.containerImage
    gpuTypeIds            = @($candidates)
    gpuTypePriority       = "custom"
    gpuCount              = [int]$manifest.gpuCount
    containerDiskInGb     = [int]$manifest.containerDiskInGb
    volumeInGb            = [int]$manifest.volumeInGb
    volumeMountPath       = [string]$manifest.volumeMountPath
    ports                 = @("$($manifest.inferencePort)/http", "$($manifest.sshTcpPort)/tcp")
    interruptible         = [bool]$manifest.interruptible
}
try {
    $created = Invoke-RestMethod -Uri "https://rest.runpod.io/v1/pods" -Method POST `
        -ContentType "application/json" -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" } `
        -Body ($request | ConvertTo-Json -Depth 10)
}
catch {
    $detail = $_.ErrorDetails.Message
    if (-not [string]::IsNullOrWhiteSpace($detail)) { throw "Pod creation failed: $detail" }
    throw "Pod creation failed: $($_.Exception.Message)"
}

$newPodId = [string]$created.id
if ([string]::IsNullOrWhiteSpace($newPodId)) {
    $newPodId = [string]$created.podId
}
if ([string]::IsNullOrWhiteSpace($newPodId)) {
    throw "Created pod response did not include an id. Full response: $($created | ConvertTo-Json -Depth 6 -Compress)"
}

Write-Host "Pod created: $newPodId ($($created.name))"
Write-Host "Endpoint   : https://$newPodId-$($manifest.inferencePort).proxy.runpod.net"

# --- record the new pod id + gpu in the manifest (previous id preserved for audit) ---
$oldPodId = [string]$manifest.podId
$manifest.podId = $newPodId
$manifest.gpuTypeId = $candidates[0]

$previousIds = @()
if ($manifest.PSObject.Properties.Match("previousPodIds").Count -gt 0 -and $null -ne $manifest.previousPodIds) {
    $previousIds = @($manifest.previousPodIds)
}
if (-not [string]::IsNullOrWhiteSpace($oldPodId) -and $oldPodId -ne $newPodId -and $previousIds -notcontains $oldPodId) {
    $previousIds += $oldPodId
}
if ($manifest.PSObject.Properties.Match("previousPodIds").Count -gt 0) {
    $manifest.previousPodIds = $previousIds
}
else {
    $manifest = $manifest | Add-Member -NotePropertyName "previousPodIds" -NotePropertyValue $previousIds -PassThru
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content -Path $ManifestPath -Encoding utf8
Write-Host "Manifest updated: $ManifestPath (podId -> $newPodId)"

# --- wait for RUNNING + runtime ports ---
function Invoke-GraphQL {
    param([string]$Query, [hashtable]$Vars = @{})
    $body = @{ query = $Query; variables = $Vars } | ConvertTo-Json -Depth 10
    (Invoke-RestMethod -Uri "https://api.runpod.io/graphql" -Method POST -ContentType "application/json" `
        -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" } -Body $body).data
}

Write-Host "Waiting for '$newPodId' to expose runtime ports (up to ${RuntimeTimeoutSeconds}s)..."
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($RuntimeTimeoutSeconds)
$http = $null
$tcp = $null
while ([DateTimeOffset]::UtcNow -lt $deadline) {
    $pod = (Invoke-GraphQL -Query 'query($id: String!){ pod(input:{podId:$id}){ id desiredStatus runtime{ ports{ ip privatePort publicPort type } } } }' -Vars @{ id = $newPodId }).pod
    if ([string]$pod.desiredStatus -eq "RUNNING" -and $null -ne $pod.runtime -and $null -ne $pod.runtime.ports) {
        $http = $pod.runtime.ports | Where-Object { [int]$_.privatePort -eq [int]$manifest.inferencePort -and [string]$_.type -eq "http" } | Select-Object -First 1
        $tcp = $pod.runtime.ports | Where-Object { [int]$_.privatePort -eq [int]$manifest.sshTcpPort -and [string]$_.type -eq "tcp" } | Select-Object -First 1
        if ($null -ne $http -and $null -ne $tcp) { break }
    }
    [Threading.Thread]::Sleep($PollIntervalSeconds * 1000)
}
if ($null -eq $http -or $null -eq $tcp) {
    throw "Pod '$newPodId' did not expose HTTP:$($manifest.inferencePort) and SSH:$($manifest.sshTcpPort) within ${RuntimeTimeoutSeconds}s. Provisioning cannot proceed; see RunPod console."
}

[ordered]@{
    deploymentKey = [string]$manifest.deploymentKey
    podId         = $newPodId
    podName       = [string]$manifest.podName
    gpuTypeId     = $candidates[0]
    cloudType     = $CloudType
    endpoint      = "https://$newPodId-$($manifest.inferencePort).proxy.runpod.net"
    sshIp         = $http.ip
    sshPort       = $tcp.publicPort
    previousPodId = $oldPodId
} | ConvertTo-Json -Depth 6
