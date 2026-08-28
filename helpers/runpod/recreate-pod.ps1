# recreate-pod.ps1 - full lifecycle to re-create one DreamGenClone RunPod pod on an alternate GPU.
#
#   create  -> provision (from scratch over SSH) -> smoke test -> Model Manager endpoint update
#
# This is the canonical command for the runpod-pod-creation skill. It is used when a pod cannot be
# started and RunPod manual migration fails because no GPU is available.
#
# IMPORTANT: a fresh pod starts with an EMPTY local volume, so provisioning re-downloads every
# model (Juggernaut ~7 GB, Qwen Edit ~30 GB, Qwen VL ~16 GB, DWPose ~1 GB). This is expected and
# can take significant time. It does NOT carry over any previous pod data.
#
# Usage (example: re-create Juggernaut):
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/recreate-pod.ps1 `
#     -ManifestPath helpers/runpod/deployments/image-gen-juggernaut/deployment.json
#
# With explicit GPU candidates (ordered, cheapest-first; first available is rented):
#   ... -GpuTypeIds "NVIDIA A40","NVIDIA RTX A5000","NVIDIA GeForce RTX 3090 Ti"
#
# Model Manager sync (CAS-guarded). Read the CURRENT BaseUrl from the DB first via:
#   powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/runpod-provider-endpoints.sql
# then pass it as -ExpectedCurrentBaseUrl along with the provider id:
#   ... -ProviderId <GUID> -ExpectedCurrentBaseUrl <current-https-url> -UpdateModelManager
#
# Flags:
#   -SkipCreate           pod already created (manifest.podId set); skip create-pod.ps1
#   -SkipProvision        skip SSH provisioning (e.g. already provisioned)
#   -SkipSmokeTest        skip readiness/identity smoke test
#   -UpdateModelManager   run the CAS provider-endpoint-update (needs -ProviderId + -ExpectedCurrentBaseUrl)
param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [string[]]$GpuTypeIds,
    [ValidateSet("SECURE", "COMMUNITY")][string]$CloudType = "SECURE",
    [switch]$SkipCreate,
    [switch]$SkipProvision,
    [switch]$SkipSmokeTest,
    [switch]$UpdateModelManager,
    [string]$ProviderId,
    [string]$ExpectedCurrentBaseUrl
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
Set-Location $repoRoot

$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
$registry = Get-Content -Raw -Path (Join-Path $repoRoot "helpers/runpod/pod-registry.json") | ConvertFrom-Json
$entry = $registry.pods | Where-Object { [string]$_.deploymentKey -eq [string]$manifest.deploymentKey }
if ($null -eq $entry) { throw "Registry has no entry for deploymentKey '$($manifest.deploymentKey)'." }

Write-Host "============================================================"
Write-Host "Re-create pod: $($entry.function) [$($manifest.deploymentKey)]"
Write-Host "============================================================"

# --- Step 1: create ---
if (-not $SkipCreate) {
    Write-Host ""
    Write-Host "--- [1/3] create pod on candidate GPU list ---"
    $createArgs = @("-ManifestPath", $ManifestPath, "-CloudType", $CloudType)
    # Pass candidates as a CSV string: an array argument is not bound reliably across
    # a nested powershell -File invocation. Omitted -> create-pod.ps1 uses the registry.
    if ($GpuTypeIds -and $GpuTypeIds.Count -gt 0) {
        $createArgs += "-GpuTypeIdsCsv"
        $createArgs += ($GpuTypeIds -join ',')
    }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scriptDir "create-pod.ps1") @createArgs
    if ($LASTEXITCODE -ne 0) { throw "create-pod.ps1 failed (exit $LASTEXITCODE)." }
}
else {
    Write-Host "--- [1/3] SkipCreate set; using manifest podId '$($manifest.podId)' ---"
}

# --- Step 2: provision + smoke ---
$provisionArgs = @("-ManifestPath", $ManifestPath)
if ($SkipProvision)  { $provisionArgs += "-SkipProvision" }
if ($SkipSmokeTest)  { $provisionArgs += "-SkipSmokeTest" }
Write-Host ""
Write-Host "--- [2/3] provision from scratch + smoke test ---"
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scriptDir "provision-pod.ps1") @provisionArgs
if ($LASTEXITCODE -ne 0) { throw "provision-pod.ps1 failed (exit $LASTEXITCODE)." }

# --- Step 3: Model Manager sync ---
$newUrl = "https://$($manifest.podId)-$($manifest.inferencePort).proxy.runpod.net"
if ($UpdateModelManager) {
    if ([string]::IsNullOrWhiteSpace($ProviderId)) { throw "-UpdateModelManager requires -ProviderId." }
    if ([string]::IsNullOrWhiteSpace($ExpectedCurrentBaseUrl)) { throw "-UpdateModelManager requires -ExpectedCurrentBaseUrl (the CURRENT BaseUrl from the DB)." }
    Write-Host ""
    Write-Host "--- [3/3] Model Manager CAS endpoint update ---"
    & dotnet run --project DreamGenClone.DbQuery -- `
        provider-endpoint-update $ProviderId $ExpectedCurrentBaseUrl $newUrl
    if ($LASTEXITCODE -ne 0) { throw "provider-endpoint-update failed (exit $LASTEXITCODE)." }
}
else {
    Write-Host ""
    Write-Host "--- [3/3] Model Manager not updated (no -UpdateModelManager). New endpoint would be: $newUrl ---"
    if ([bool]$entry.modelManager.updateEndpoint) {
        Write-Host "NOTE: registry says this pod feeds Model Manager provider '$($entry.modelManager.providerName)' ($($entry.modelManager.providerId))."
        Write-Host "      Run with -UpdateModelManager -ProviderId <id> -ExpectedCurrentBaseUrl <current-url> once the smoke test passes."
    }
}

Write-Host ""
Write-Host "DONE: $($entry.function)"
Write-Host "  pod     : $($manifest.podId)"
Write-Host "  endpoint: $newUrl"
