# Starts one manifest-defined pod, verifies its runtime identity, then atomically updates one
# Model Manager provider endpoint. A running migrated successor or explicitly supplied replacement
# manifest is used only when the primary start fails for GPU capacity. This script never creates,
# migrates, terminates, or deletes pods.
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$ProviderId,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedCurrentBaseUrl,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 7200)]
    [int]$RuntimeTimeoutSeconds,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 300)]
    [int]$PollIntervalSeconds,

    [string]$ReplacementManifestPath
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

function Read-OperationalManifest {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path $Path)) {
        throw "Deployment manifest not found: $Path"
    }
    $manifest = Get-Content -Raw -Path $Path | ConvertFrom-Json
    foreach ($property in @("deploymentKey", "podId", "podName", "containerImage", "inferencePort", "readinessPath", "identityProbePath", "identityExpectedText")) {
        if ([string]::IsNullOrWhiteSpace([string]$manifest.$property)) {
            throw "Manifest '$Path' is missing operational property '$property'."
        }
    }
    if ([int]$manifest.inferencePort -le 0) {
        throw "Manifest '$Path' inferencePort must be greater than zero."
    }
    return $manifest
}

function Invoke-RunPodGraphQl {
    param(
        [Parameter(Mandatory = $true)][string]$Query,
        [hashtable]$Variables = @{}
    )

    $body = @{ query = $Query; variables = $Variables } | ConvertTo-Json -Depth 20
    $response = Invoke-RestMethod -Uri "https://api.runpod.io/graphql" -Method POST `
        -ContentType "application/json" `
        -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" } `
        -Body $body
    if ($response.errors) {
        throw (($response.errors | ConvertTo-Json -Depth 10 -Compress))
    }
    return $response.data
}

function Start-ManifestPod {
    param([Parameter(Mandatory = $true)][object]$Manifest)

    return Invoke-RunPodGraphQl `
        -Query 'mutation($id: String!, $gpuCount: Int!){ podResume(input:{podId:$id, gpuCount:$gpuCount}){ id desiredStatus machineId } }' `
        -Variables @{ id = [string]$Manifest.podId; gpuCount = [int]$Manifest.gpuCount }
}

function Get-ManifestPod {
    param([Parameter(Mandatory = $true)][object]$Manifest)

    return (Invoke-RunPodGraphQl `
        -Query 'query($id: String!){ pod(input:{podId:$id}){ id desiredStatus machineId runtime{ ports{ ip privatePort publicPort type } } } }' `
        -Variables @{ id = [string]$Manifest.podId }).pod
}

function Find-RunningMigratedPod {
    param([Parameter(Mandatory = $true)][object]$Manifest)

    $pods = (Invoke-RunPodGraphQl `
        -Query 'query { myself { pods { id name imageName desiredStatus } } }').myself.pods
    $migrationNamePrefix = "$($Manifest.podName)-migration"
    $candidatePods = @($pods | Where-Object {
        [string]$_.id -ne [string]$Manifest.podId -and
        ([string]$_.name -eq [string]$Manifest.podName -or [string]$_.name -like "$migrationNamePrefix*") -and
        [string]$_.imageName -eq [string]$Manifest.containerImage -and
        [string]$_.desiredStatus -eq "RUNNING"
    })

    if ($candidatePods.Count -eq 0) {
        throw "Primary pod '$($Manifest.podId)' has no GPU capacity, and no running migrated successor matched pod name '$($Manifest.podName)' or '$migrationNamePrefix*' with image '$($Manifest.containerImage)'. Use RunPod's Migrate action, wait for the migrated pod to be running, then run this command again."
    }
    if ($candidatePods.Count -gt 1) {
        $podIds = ($candidatePods | ForEach-Object { [string]$_.id }) -join ", "
        throw "Primary pod '$($Manifest.podId)' has no GPU capacity, and multiple running migrated successors matched: $podIds. Supply -ReplacementManifestPath to select one explicitly."
    }
    return $candidatePods[0]
}

function Wait-ManifestRuntime {
    param([Parameter(Mandatory = $true)][object]$Manifest)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($RuntimeTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $pod = Get-ManifestPod -Manifest $Manifest
        $inferencePort = $pod.runtime.ports | Where-Object {
            [int]$_.privatePort -eq [int]$Manifest.inferencePort -and [string]$_.type -eq "http"
        } | Select-Object -First 1
        if ([string]$pod.desiredStatus -eq "RUNNING" -and $null -ne $inferencePort) {
            return $pod
        }
        [Threading.Thread]::Sleep($PollIntervalSeconds * 1000)
    }
    throw "Pod '$($Manifest.podId)' did not expose HTTP port '$($Manifest.inferencePort)' within $RuntimeTimeoutSeconds seconds; provider endpoint was not updated."
}

function Assert-ManifestService {
    param(
        [Parameter(Mandatory = $true)][object]$Manifest,
        [Parameter(Mandatory = $true)][string]$BaseUrl
    )

    $readinessUrl = "$BaseUrl$($Manifest.readinessPath)"
    try {
        $null = Invoke-RestMethod -Uri $readinessUrl -Method GET -TimeoutSec $RuntimeTimeoutSeconds
    }
    catch {
        throw "Readiness check failed at '$readinessUrl'; provider endpoint was not updated. $($_.Exception.Message)"
    }

    $identityUrl = "$BaseUrl$($Manifest.identityProbePath)"
    try {
        $identity = Invoke-RestMethod -Uri $identityUrl -Method GET -TimeoutSec $RuntimeTimeoutSeconds
        $identityText = $identity | ConvertTo-Json -Depth 100 -Compress
    }
    catch {
        throw "Identity check failed at '$identityUrl'; provider endpoint was not updated. $($_.Exception.Message)"
    }
    if ($identityText -notmatch [regex]::Escape([string]$Manifest.identityExpectedText)) {
        throw "Identity response from '$identityUrl' did not contain '$($Manifest.identityExpectedText)'; provider endpoint was not updated."
    }
}

$selectedManifest = Read-OperationalManifest -Path $ManifestPath
$isMigratedPod = $false
try {
    $null = Start-ManifestPod -Manifest $selectedManifest
}
catch {
    $message = $_.Exception.Message
    $capacityFailure = $message -match "not enough free GPUs|not enough.*GPU|GPU.*not available|no available.*GPU"
    if (-not $capacityFailure) {
        throw
    }
    if ([string]::IsNullOrWhiteSpace($ReplacementManifestPath)) {
        $migratedPod = Find-RunningMigratedPod -Manifest $selectedManifest
        Write-Host "Primary capacity unavailable; using running migrated pod '$($migratedPod.name)' ($($migratedPod.id))."
        $selectedManifest.podId = [string]$migratedPod.id
        $isMigratedPod = $true
    }
    else {
        $selectedManifest = Read-OperationalManifest -Path $ReplacementManifestPath
        Write-Host "Primary capacity unavailable; starting configured replacement deployment '$($selectedManifest.deploymentKey)' ($($selectedManifest.podId))."
        $null = Start-ManifestPod -Manifest $selectedManifest
    }
}

$pod = Wait-ManifestRuntime -Manifest $selectedManifest
$baseUrl = "https://$($selectedManifest.podId)-$($selectedManifest.inferencePort).proxy.runpod.net"
if (-not $isMigratedPod) {
    Assert-ManifestService -Manifest $selectedManifest -BaseUrl $baseUrl
}
else {
    Write-Host "Migrated pod is RUNNING; skipping readiness and model-identity probes."
}

& dotnet run --project DreamGenClone.DbQuery -- `
    provider-endpoint-update $ProviderId $ExpectedCurrentBaseUrl $baseUrl
if ($LASTEXITCODE -ne 0) {
    throw "Provider endpoint update failed with exit code $LASTEXITCODE."
}

[ordered]@{
    deploymentKey = $selectedManifest.deploymentKey
    podId = $selectedManifest.podId
    machineId = $pod.machineId
    providerId = $ProviderId
    baseUrl = $baseUrl
    readinessPath = $selectedManifest.readinessPath
    identityProbePath = $selectedManifest.identityProbePath
    identityExpectedText = $selectedManifest.identityExpectedText
    migratedPodValidationSkipped = $isMigratedPod
} | ConvertTo-Json -Depth 10