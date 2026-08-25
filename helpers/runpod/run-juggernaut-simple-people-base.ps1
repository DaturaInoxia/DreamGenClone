# Runs the source-controlled Juggernaut base-image proof once.
# The exact prompt, negative prompt, sampler settings, checkpoint, and seed live in the workflow.
param(
    [Parameter(Mandatory=$true)][string]$ComfyUiUrl,
    [string]$OutputDir = "artifacts/tmp/images/juggernaut-simple-people-replay",
    [int]$TimeoutSec = 600
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$workflowPath = Join-Path $repoRoot "helpers\runpod\workflows\qwen-simple-people-base.json"
$runnerPath = Join-Path $PSScriptRoot "generate-one.ps1"
$proofManifestPath = Join-Path $repoRoot "specs\Planning\B-032-scene-image-generator\phase-2-character-identity\qwen-simple-people-proof\manifest.json"

foreach ($path in $workflowPath, $runnerPath, $proofManifestPath) {
    if (-not (Test-Path $path -PathType Leaf)) { throw "Required source-controlled file was not found: $path" }
}

$proofManifest = Get-Content -Raw $proofManifestPath | ConvertFrom-Json
$resolvedOutputDir = Join-Path $repoRoot $OutputDir

& $runnerPath `
    -WorkflowPath $workflowPath `
    -ComfyUiUrl $ComfyUiUrl `
    -Seed $proofManifest.base.seed `
    -Prefix "juggernaut-simple-people-base" `
    -OutputDir $resolvedOutputDir `
    -TimeoutSec $TimeoutSec

Write-Host "Generated a fresh Juggernaut base image in: $resolvedOutputDir"
Write-Host "Compare it visually with: $($proofManifest.base.path)"
Write-Host "The packaged base hash documents the original evidence only; changed ComfyUI, CUDA, PyTorch, or model environments need not reproduce identical bytes."