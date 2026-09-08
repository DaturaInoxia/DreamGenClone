# run-sdxl-smoke.ps1 - one t2i render per SDXL-class checkpoint to prove the local
# ComfyUI host runs FLUX AND SDXL checkpoints side by side.
# Uses helpers/flux-local-host/sdxl-t2i-smoke.json (CheckpointLoaderSimple + SDXL settings)
# via helpers/runpod/generate-one.ps1, overriding the checkpoint per model.
param(
    [Parameter(Mandatory=$true)][string]$ComfyUiUrl,
    [Parameter(Mandatory=$false)][int]$Seed = 20260908,
    [Parameter(Mandatory=$false)][int]$TimeoutSec = 600,
    [Parameter(Mandatory=$false)][string]$OutputDir = "artifacts/tmp/images/sdxl-coexist-smoke"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$workflowPath = Join-Path $PSScriptRoot "sdxl-t2i-smoke.json"
$runnerPath = Join-Path $repoRoot "helpers\runpod\generate-one.ps1"
foreach ($path in $workflowPath, $runnerPath) {
    if (-not (Test-Path $path -PathType Leaf)) { throw "Required file not found: $path" }
}

$models = @(
    "juggernautXL_ragnarok.safetensors",
    "bigLust_v16.safetensors",
    "ponyDiffusionV6XL_v6.safetensors"
)
$resolvedOut = Join-Path $repoRoot $OutputDir
Write-Host "SDXL coexist smoke: url=$ComfyUiUrl models=$($models.Count) out=$resolvedOut"

foreach ($model in $models) {
    Write-Host ""
    Write-Host "== model: $model"

    # Bake the checkpoint filename into node 1 (CheckpointLoaderSimple).
    $wf = Get-Content -Raw $workflowPath | ConvertFrom-Json
    $wf.PSObject.Properties["1"].Value.inputs.ckpt_name = $model
    $tmpWf = Join-Path ([IO.Path]::GetTempPath()) ("sdxl_smoke_" + [guid]::NewGuid().ToString("N") + ".json")
    [IO.File]::WriteAllText($tmpWf, ($wf | ConvertTo-Json -Depth 20))

    try {
        $outDir = Join-Path $resolvedOut ($model -replace '\.safetensors$', '')
        & $runnerPath `
            -WorkflowPath $tmpWf `
            -ComfyUiUrl $ComfyUiUrl `
            -Seed $Seed `
            -Prefix ("sdxl-" + ($model -replace '\.safetensors$', '')) `
            -OutputDir $outDir `
            -TimeoutSec $TimeoutSec
    }
    finally {
        Remove-Item -LiteralPath $tmpWf -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "SDXL smoke outputs in: $resolvedOut"
Write-Host "Open each PNG to confirm a valid render was produced for every model."
