# run-deep-probe.ps1 - render the deep implied-pose probe cells (deep-probe-cells.json) on stock
# FLUX fp8 through the local ComfyUI host, staging PNGs under git-ignored artifacts/tmp.
# Visual review is REQUIRED before moving any output into a persisted runs/<ts>-<label>/ folder.
param(
    [Parameter(Mandatory=$true)][string]$ComfyUiUrl,
    [Parameter(Mandatory=$false)][string]$CellsPath = "",
    [Parameter(Mandatory=$false)][int]$Seed = 20260907,
    [Parameter(Mandatory=$false)][string]$OutBase = "artifacts/tmp/images/flux-implied-proof-deep",
    [Parameter(Mandatory=$false)][int]$TimeoutSec = 300
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $here "..\..\..\..")).Path
if (-not $CellsPath) { $CellsPath = Join-Path $here "..\deep-probe-cells.json" }
$workflowPath = Join-Path $repoRoot "helpers\flux-local-host\flux-t2i-proof.json"
$runnerPath   = Join-Path $repoRoot "helpers\runpod\generate-one.ps1"
foreach ($p in $CellsPath, $workflowPath, $runnerPath) {
    if (-not (Test-Path $p -PathType Leaf)) { throw "Required file not found: $p" }
}
$cells = @((Get-Content -Raw $CellsPath | ConvertFrom-Json).cells)
$resolvedOut = Join-Path $repoRoot $OutBase
Write-Host "Deep probe: cells=$($cells.Count) seed=$Seed url=$ComfyUiUrl"
foreach ($cell in $cells) {
    Write-Host ""
    Write-Host "== $($cell.id): $($cell.prompt)"
    & $runnerPath -WorkflowPath $workflowPath -ComfyUiUrl $ComfyUiUrl -Seed $Seed `
        -Prefix ("flux-" + $cell.id) -OutputDir (Join-Path $resolvedOut $cell.id) `
        -Prompt $cell.prompt -TimeoutSec $TimeoutSec
}
Write-Host ""
Write-Host "Staged under $resolvedOut"
Write-Host "REQUIRED: visually review every PNG (view_image) and record PASS/FAIL before persisting a run."
