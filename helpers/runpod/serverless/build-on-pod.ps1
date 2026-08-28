# build-on-pod.ps1 - FALLBACK builder: build + push a Serverless worker image on a TEMP RunPod pod
#
# B-101. Default builder is the GitHub Actions workflow (.github/workflows/build-serverless-worker.yml)
# -> GHCR (repo is public). This script is only a fallback if Actions/GHCR are unavailable.
# Docker is NOT installed on the dev host, so the build would happen on a short-lived RunPod pod
# (cheap GPU), then the pod is terminated. No container tooling is installed on this Windows host.
#
# Mechanism (P0/P1 GATE - validate live before first real run):
#   1. create a cheap temp pod via the RunPod GraphQL API
#   2. copy the worker build context (Dockerfile + handler) to the pod over SSH
#   3. on the pod: install buildah (daemonless, works in a container), then
#        buildah bud -t <registry>/<image> <ctx>
#        buildah push --tls-verify=false <image> docker://<registry>/<image>
#   4. terminate the temp pod
# Requires a container registry + credentials (Docker Hub / GHCR). Set via:
#   $env:IMAGE_REGISTRY, $env:REGISTRY_USER, $env:REGISTRY_TOKEN  (or in .runpod-env.ps1)
#
# Usage:
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/build-on-pod.ps1 `
#     -Worker dwpose -ImageTag 0.1.0 -DryRun
param(
    [Parameter(Mandatory = $true)][ValidateSet("dwpose")][string]$Worker,
    [string]$ImageTag = "0.1.0",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
Set-Location $repoRoot

. (Join-Path $PSScriptRoot "..\common.ps1")
Get-RunPodEnv

$registry = Get-Content -Raw -Path (Join-Path $PSScriptRoot "endpoints.json") | ConvertFrom-Json
$ctx = Join-Path $PSScriptRoot "$Worker-worker"
if (-not (Test-Path (Join-Path $ctx "Dockerfile"))) { throw "No Dockerfile in $ctx" }

$image = "$($registry.containerRegistry)/dreamgen-$Worker-worker:$ImageTag"
Write-Host "============================================================"
Write-Host "Build + push worker image: $image"
Write-Host "  context  : $ctx"
Write-Host "  builder  : temp RunPod pod (buildah, daemonless)"
Write-Host "============================================================"

if ($DryRun) {
    Write-Host "`nDRY RUN. Steps (P0/P1: validate live API + buildah-on-pod mechanics):"
    Write-Host "  1) create temp pod (cheap GPU, SECURE)"
    Write-Host "  2) scp '$ctx' -> pod:/workspace/build"
    Write-Host "  3) on pod: apt-get install -y buildah; buildah bud -t '$image' /workspace/build; buildah push '$image' docker://$image"
    Write-Host "  4) terminate temp pod"
    exit 0
}

throw "Not implemented past DryRun - validate the temp-pod/buildah mechanics at P1 before enabling."
