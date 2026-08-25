param(
    [Parameter(Mandatory=$true)][string]$BaseComfyUiUrl,
    [Parameter(Mandatory=$true)][string]$QwenComfyUiUrl,
    [string]$OutputDir = "artifacts/tmp/images/qwen-simple-people-replay",
    [int]$TimeoutSec = 600
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$proofRoot = Join-Path $repoRoot "specs\Planning\B-032-scene-image-generator\phase-2-character-identity\qwen-simple-people-proof"
$manifestPath = Join-Path $proofRoot "manifest.json"
$baseWorkflow = Join-Path $repoRoot "helpers\runpod\workflows\qwen-simple-people-base.json"
$editWorkflow = Join-Path $repoRoot "helpers\runpod\workflows\qwen-simple-people-edit.json"
$runner = Join-Path $PSScriptRoot "generate-one.ps1"

foreach ($path in $manifestPath, $baseWorkflow, $editWorkflow, $runner) {
    if (-not (Test-Path $path -PathType Leaf)) { throw "Required proof file was not found: $path" }
}

$manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
$resolvedOutputDir = Join-Path $repoRoot $OutputDir
New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null
$baseOutput = Join-Path $resolvedOutputDir $manifest.base.path

& $runner -WorkflowPath $baseWorkflow -ComfyUiUrl $BaseComfyUiUrl -Seed $manifest.base.seed -Prefix "qwen-simple-people-base" -OutputDir $baseOutput -TimeoutSec $TimeoutSec

foreach ($edit in $manifest.edits) {
    $outputPath = Join-Path $resolvedOutputDir $edit.path
    Write-Host "Replaying edit '$($edit.id)' from the new base image."
    & $runner -WorkflowPath $editWorkflow -ComfyUiUrl $QwenComfyUiUrl -InputImagePath $baseOutput -Prompt $edit.prompt -Seed $edit.seed -Prefix $edit.id -OutputDir $outputPath -TimeoutSec $TimeoutSec
}

Write-Host "Replay complete: $resolvedOutputDir"
Write-Host "Review the outputs visually. The packaged hashes prove the original evidence package only; do not expect byte-identical replay output across changed ComfyUI, CUDA, PyTorch, or model environments."