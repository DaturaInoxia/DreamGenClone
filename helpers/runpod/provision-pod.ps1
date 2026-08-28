# provision-pod.ps1 - provision a freshly created RunPod pod from scratch and smoke test it.
#
# Reads the pod's provisioning steps from helpers/runpod/pod-registry.json (keyed by the manifest
# deploymentKey), pipes each provision/start script over SSH, installs remote checkpoints, then
# verifies readiness + model identity over the HTTPS proxy endpoint.
#
# A fresh pod has an EMPTY /workspace, so provisioning re-downloads every model. Model download
# sizes are listed in the registry; large downloads (Qwen Edit ~30 GB, Qwen VL ~16 GB) take a while.
#
# Usage:
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/provision-pod.ps1 `
#     -ManifestPath helpers/runpod/deployments/image-gen-juggernaut/deployment.json
#
# Flags:
#   -SkipProvision   only run the smoke test (readiness + identity), skip SSH provisioning
#   -SkipSmokeTest   only run provisioning, skip the HTTPS smoke test
#   -RegistryPath    override the registry file (default helpers/runpod/pod-registry.json)
param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [switch]$SkipProvision,
    [switch]$SkipSmokeTest,
    [string]$RegistryPath = "helpers/runpod/pod-registry.json",
    [int]$ReadinessTimeoutSeconds = 900
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

$keyPath = Join-Path $repoRoot "artifacts\runpod\ssh_ed25519"
if (-not (Test-Path $keyPath)) { throw "SSH private key not found: $keyPath" }

$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
$registry = Get-Content -Raw -Path (Join-Path $repoRoot $RegistryPath) | ConvertFrom-Json
$entry = $registry.pods | Where-Object { [string]$_.deploymentKey -eq [string]$manifest.deploymentKey }
if ($null -eq $entry) { throw "Registry has no entry for deploymentKey '$($manifest.deploymentKey)'." }

$podId = [string]$manifest.podId
if ([string]::IsNullOrWhiteSpace($podId)) { throw "Manifest has no podId. Run create-pod.ps1 first." }

# --- resolve SSH + HTTP mappings from runtime ---
$body = @{ query = 'query($id: String!){ pod(input:{podId:$id}){ id desiredStatus runtime{ ports{ ip privatePort publicPort type } } } }'; variables = @{ id = $podId } } | ConvertTo-Json -Depth 10
$pod = (Invoke-RestMethod -Uri "https://api.runpod.io/graphql" -Method POST -ContentType "application/json" `
    -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" } -Body $body).data.pod
if ([string]$pod.desiredStatus -ne "RUNNING") { throw "Pod '$podId' is not RUNNING (status '$($pod.desiredStatus)'). Start it before provisioning." }

$tcp = $pod.runtime.ports | Where-Object { [int]$_.privatePort -eq [int]$manifest.sshTcpPort -and [string]$_.type -eq "tcp" } | Select-Object -First 1
if ($null -eq $tcp) { throw "Pod '$podId' has no SSH ($($manifest.sshTcpPort)/tcp) mapping." }
$sshHost = $tcp.ip
$sshPort = $tcp.publicPort
$baseUrl = "https://$podId-$($manifest.inferencePort).proxy.runpod.net"
Write-Host "Pod $podId ready: ssh root@$sshHost`:$sshPort | endpoint $baseUrl"

function Invoke-SshScript {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [string[]]$Args,
        [hashtable]$Env
    )
    $localScript = Join-Path $repoRoot $ScriptPath
    if (-not (Test-Path $localScript)) { throw "Provision script not found: $localScript" }
    $script = Get-Content -Raw -Path $localScript
    $script = $script -replace "`r", ""
    $envPrefix = ""
    if ($Env) {
        $parts = foreach ($k in $Env.Keys) { "$k=$($Env[$k])" }
        $envPrefix = ($parts -join " ") + " "
    }
    $argsList = if ($Args) { ($Args | ForEach-Object { "'$($_.Replace("'", "'\''"))'" }) -join " " } else { "" }
    $remote = "$envPrefix bash -s -- $argsList"
    $sshArgs = @("-o", "BatchMode=yes", "-o", "ConnectTimeout=30", "-o", "StrictHostKeyChecking=no", "-o", "UserKnownHostsFile=NUL", "-o", "IdentitiesOnly=yes", "-i", $keyPath, "-p", $sshPort, "root@$sshHost")
    Write-Host ""
    Write-Host "=== SSH provision: $ScriptPath $argsList ==="
    $script | & ssh @sshArgs $remote
    if ($LASTEXITCODE -ne 0) { throw "SSH script '$ScriptPath' failed (exit $LASTEXITCODE)." }
}

function Invoke-SshCommand {
    param([Parameter(Mandatory = $true)][string]$Command)
    $sshArgs = @("-o", "BatchMode=yes", "-o", "ConnectTimeout=30", "-o", "StrictHostKeyChecking=no", "-o", "UserKnownHostsFile=NUL", "-o", "IdentitiesOnly=yes", "-i", $keyPath, "-p", $sshPort, "root@$sshHost")
    Write-Host "=== SSH command: $Command ==="
    & ssh @sshArgs $Command
    if ($LASTEXITCODE -ne 0) { throw "SSH command failed (exit $LASTEXITCODE): $Command" }
}

# Registry step.env comes from ConvertFrom-Json as a PSCustomObject; convert to a Hashtable.
function ConvertTo-Hashtable {
    param($Obj)
    $h = @{}
    if ($null -ne $Obj) {
        foreach ($prop in $Obj.PSObject.Properties) { $h[[string]$prop.Name] = $prop.Value }
    }
    return $h
}

if (-not $SkipProvision) {
    foreach ($step in $entry.provision) {
        switch ($step.kind) {
            "provision-script" { Invoke-SshScript -ScriptPath ([string]$step.path) -Args @([string[]]$step.args) -Env (ConvertTo-Hashtable $step.env) }
            "start-script"     { Invoke-SshScript -ScriptPath ([string]$step.path) -Args @([string[]]$step.args) -Env (ConvertTo-Hashtable $step.env) }
            "prep-command"     { Invoke-SshCommand -Command ([string]$step.command) }
            "install-model-remote" {
                Write-Host ""
                Write-Host "=== install-model-remote: $($step.modelName) ==="
                & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "install-model-remote.ps1") `
                    -ModelName ([string]$step.modelName) `
                    -SourceUrl ([string]$step.sourceUrl) `
                    -SshHost $sshHost -SshPort $sshPort -SshUser "root"
                if ($LASTEXITCODE -ne 0) { throw "install-model-remote failed (exit $LASTEXITCODE) for $($step.modelName)." }
            }
            default { throw "Unknown provision step kind '$($step.kind)' in registry." }
        }
    }
}
else {
    Write-Host "SkipProvision set; no SSH provisioning performed."
}

if (-not $SkipSmokeTest) {
    Write-Host ""
    Write-Host "=== Smoke test (readiness + identity) over $baseUrl ==="
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
    $ready = $false
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $null = Invoke-RestMethod -Uri "$baseUrl$($entry.smokeTest.readinessPath)" -Method GET -TimeoutSec 20
            $ready = $true
            break
        }
        catch {
            [Threading.Thread]::Sleep(15000)
        }
    }
    if (-not $ready) { throw "Readiness never succeeded at '$baseUrl$($entry.smokeTest.readinessPath)' within ${ReadinessTimeoutSeconds}s." }
    Write-Host "READY: $baseUrl$($entry.smokeTest.readinessPath)"

    $identityUrl = "$baseUrl$($entry.smokeTest.identityProbePath)"
    $identityText = ""
    try {
        $identity = Invoke-RestMethod -Uri $identityUrl -Method GET -TimeoutSec 60
        $identityText = $identity | ConvertTo-Json -Depth 100 -Compress
    }
    catch {
        throw "Identity probe failed at '$identityUrl': $($_.Exception.Message)"
    }
    if ($identityText -notmatch [regex]::Escape([string]$entry.smokeTest.identityExpectedText)) {
        throw "Identity response did not contain '$($entry.smokeTest.identityExpectedText)'. Pod '$podId' may still be downloading models or the service is not fully loaded."
    }
    Write-Host "IDENTITY_OK: $($entry.smokeTest.identityExpectedText)"
}

[ordered]@{
    deploymentKey = [string]$manifest.deploymentKey
    podId         = $podId
    endpoint      = $baseUrl
    provisioned   = (-not $SkipProvision)
    smokeTested   = (-not $SkipSmokeTest)
} | ConvertTo-Json -Depth 6
