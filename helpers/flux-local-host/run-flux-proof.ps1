# run-flux-proof.ps1 - qualification runner for uncensored FLUX.1-dev on a local ComfyUI host.
# Runs the implied/softcore cells in prompts-implied.json through helpers/runpod/generate-one.ps1
# (one submit per cell, no resubmit), producing dated PNGs under a git-ignored artifacts path.
# Visual review of every output is REQUIRED before declaring PASS (see prompts-implied.json rubric).
param(
    [Parameter(Mandatory=$true)][string]$ComfyUiUrl,
    [Parameter(Mandatory=$false)][string]$ModelFile = "flux1-dev-fp8.safetensors",
    [Parameter(Mandatory=$false)][string]$Only,            # comma-separated cell ids, e.g. "-Only kneeling-implied,garden-fours"
    [Parameter(Mandatory=$false)][int]$Seed = -1,          # fixed seed applied to every cell; -1 = keep workflow seed
    [Parameter(Mandatory=$false)][int]$TimeoutSec = 900,   # FLUX fp8 on a 5080 ~1-2 min/img; allow cold-start headroom
    [Parameter(Mandatory=$false)][string]$OutputDir = "artifacts/tmp/images/flux-implied-proof"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$workflowPath = Join-Path $PSScriptRoot "flux-t2i-proof.json"
$promptsPath = Join-Path $PSScriptRoot "prompts-implied.json"
$runnerPath = Join-Path $repoRoot "helpers\runpod\generate-one.ps1"

foreach ($path in $workflowPath, $promptsPath, $runnerPath) {
    if (-not (Test-Path $path -PathType Leaf)) { throw "Required source-controlled file was not found: $path" }
}

$manifest = Get-Content -Raw $promptsPath | ConvertFrom-Json
$cells = @($manifest.cells)
if ($Only) {
    $onlyIds = ($Only -split ',' | ForEach-Object { $_.Trim() })
    $cells = @($cells | Where-Object { $_.id -in $onlyIds })
    if ($cells.Count -eq 0) { throw "No cells matched -Only '$Only'." }
}

Write-Host "FLUX proof: model=$ModelFile  cells=$($cells.Count)  url=$ComfyUiUrl"
$resolvedOut = Join-Path $repoRoot $OutputDir

foreach ($cell in $cells) {
    # Bake the diffusion model filename into the UNETLoader (node 4). generate-one.ps1 only
    # overrides CheckpointLoaderSimple (ckpt_name), so we handle unet_name here.
    $wf = Get-Content -Raw $workflowPath | ConvertFrom-Json
    $wf.PSObject.Properties["4"].Value.inputs.unet_name = $ModelFile

    $tmpWf = Join-Path ([IO.Path]::GetTempPath()) ("flux_" + $cell.id + "_" + [guid]::NewGuid().ToString("N") + ".json")
    [IO.File]::WriteAllText($tmpWf, ($wf | ConvertTo-Json -Depth 20))

    try {
        Write-Host ""
        Write-Host "== cell $($cell.id): $($cell.prompt)"
        $seedArg = if ($Seed -ge 0) { $Seed } else { -1 }
        & $runnerPath `
            -WorkflowPath $tmpWf `
            -ComfyUiUrl $ComfyUiUrl `
            -Seed $seedArg `
            -Prefix ("flux-" + $cell.id) `
            -OutputDir (Join-Path $resolvedOut $cell.id) `
            -TimeoutSec $TimeoutSec
    }
    finally {
        Remove-Item -LiteralPath $tmpWf -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "Proof outputs in: $resolvedOut"
Write-Host "REQUIRED: open every PNG (view_image) and record per-cell PASS/FAIL against prompts-implied.json"
Write-Host "  before calling this model acceptable. Expected: arrangement honored AND implied-but-not-explicit."
