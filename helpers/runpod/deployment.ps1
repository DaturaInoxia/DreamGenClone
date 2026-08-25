# deployment.ps1 - validate, preview, and operate one manifest-defined RunPod deployment.
# Resource creation is explicit-confirm only. Termination and volume deletion are not supported here.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("validate", "preview", "create", "status", "start", "stop")]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [string]$ManifestPath
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

if ($Action -in @("create", "status", "start", "stop")) {
    Get-RunPodEnv
}

function Read-DeploymentManifest {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path $Path)) {
        throw "Deployment manifest not found: $Path"
    }

    $manifest = Get-Content -Raw -Path $Path | ConvertFrom-Json
    $required = @(
        "deploymentKey", "displayName", "capability", "modelIdentifier", "podId",
        "podName", "containerImage", "cloudType", "interruptible", "gpuTypeId", "gpuCount", "volumeInGb",
        "containerDiskInGb", "volumeMountPath", "inferencePort", "sshTcpPort",
        "sshUser", "readinessPath", "expectedIdentity"
    )
    foreach ($property in $required) {
        $value = $manifest.$property
        if ($null -eq $value -or ([string]::IsNullOrWhiteSpace([string]$value) -and $property -notin @("podId", "gpuCount", "volumeInGb", "containerDiskInGb", "inferencePort", "sshTcpPort"))) {
            throw "Manifest '$Path' is missing required property '$property'."
        }
    }

    foreach ($number in @("gpuCount", "volumeInGb", "containerDiskInGb", "inferencePort", "sshTcpPort")) {
        if ([int]$manifest.$number -le 0) {
            throw "Manifest '$Path' property '$number' must be greater than zero."
        }
    }
    if ([int]$manifest.sshTcpPort -eq [int]$manifest.inferencePort) {
        throw "Manifest '$Path' must use different SSH and inference public ports."
    }
    if ([string]$manifest.sshUser -ne "root") {
        throw "Manifest '$Path' must use the supported RunPod SSH user 'root'."
    }
    if ([string]$manifest.volumeMountPath -ne "/workspace") {
        throw "Manifest '$Path' must mount its persistent volume at /workspace."
    }
    if ([string]$manifest.cloudType -ne "SECURE") {
        throw "Manifest '$Path' must use Secure Cloud for SSH TCP access."
    }
    if ([bool]$manifest.interruptible) {
        throw "Manifest '$Path' must not use interruptible capacity."
    }
    if ($manifest.podId -eq "legacy-combined-current") {
        throw "The legacy deployment is inventory-only and cannot be operated by deployment.ps1."
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

function Invoke-RunPodRest {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("GET", "POST")][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null
    )
    $parameters = @{
        Uri = "https://rest.runpod.io/v1$Path"
        Method = $Method
        Headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
    }
    if ($null -ne $Body) {
        $parameters.ContentType = "application/json"
        $parameters.Body = $Body | ConvertTo-Json -Depth 20
    }
    return Invoke-RestMethod @parameters
}

$manifest = Read-DeploymentManifest -Path $ManifestPath

switch ($Action) {
    "validate" {
        $manifest | ConvertTo-Json -Depth 20
        Write-Host "Manifest valid: $($manifest.deploymentKey)"
    }
    "preview" {
        [ordered]@{
            deploymentKey = $manifest.deploymentKey
            capability = $manifest.capability
            modelIdentifier = $manifest.modelIdentifier
            podName = $manifest.podName
            containerImage = $manifest.containerImage
            cloudType = $manifest.cloudType
            interruptible = [bool]$manifest.interruptible
            gpuTypeId = $manifest.gpuTypeId
            gpuCount = [int]$manifest.gpuCount
            volumeInGb = [int]$manifest.volumeInGb
            containerDiskInGb = [int]$manifest.containerDiskInGb
            volumeMountPath = $manifest.volumeMountPath
            inferencePort = [int]$manifest.inferencePort
            sshTcpPort = [int]$manifest.sshTcpPort
            sshRequired = $true
            destructiveActions = "create only; terminate and volume deletion are excluded"
        } | ConvertTo-Json -Depth 10
    }
    "create" {
        if (-not [string]::IsNullOrWhiteSpace([string]$manifest.podId)) {
            throw "Manifest '$ManifestPath' already has podId '$($manifest.podId)'; refusing to create a duplicate pod."
        }
        $request = [ordered]@{
            name = $manifest.podName
            computeType = "GPU"
            cloudType = $manifest.cloudType
            imageName = $manifest.containerImage
            gpuTypeIds = @($manifest.gpuTypeId)
            gpuTypePriority = "custom"
            gpuCount = [int]$manifest.gpuCount
            containerDiskInGb = [int]$manifest.containerDiskInGb
            volumeInGb = [int]$manifest.volumeInGb
            volumeMountPath = $manifest.volumeMountPath
            ports = @("$($manifest.inferencePort)/http", "$($manifest.sshTcpPort)/tcp")
            interruptible = [bool]$manifest.interruptible
        }
        $created = Invoke-RunPodRest -Method POST -Path "/pods" -Body $request
        $created | ConvertTo-Json -Depth 20
    }
    "status" {
        if ([string]::IsNullOrWhiteSpace([string]$manifest.podId)) {
            throw "Manifest '$ManifestPath' has no assigned podId. Create the pod through an approved provisioning workflow first."
        }
        Invoke-RunPodGraphQl -Query 'query($id: String!){ pod(input:{podId:$id}){ id name desiredStatus uptimeSeconds gpuCount machine{ gpuDisplayName } runtime{ ports{ ip privatePort publicPort type } } } }' -Variables @{ id = [string]$manifest.podId } | ConvertTo-Json -Depth 20
    }
    "start" {
        Invoke-RunPodGraphQl -Query 'mutation($id: String!, $gpuCount: Int!){ podResume(input:{podId:$id, gpuCount:$gpuCount}){ id desiredStatus } }' -Variables @{ id = [string]$manifest.podId; gpuCount = [int]$manifest.gpuCount } | ConvertTo-Json -Depth 20
    }
    "stop" {
        Invoke-RunPodGraphQl -Query 'mutation($id: String!){ podStop(input:{podId:$id}){ id desiredStatus } }' -Variables @{ id = [string]$manifest.podId } | ConvertTo-Json -Depth 20
    }
}