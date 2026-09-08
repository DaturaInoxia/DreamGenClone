# run-deep-probe.ps1 - render implied-pose probe cells (default deep-probe-cells.json) through the
# local ComfyUI host on FLUX fp8, staging PNGs under git-ignored artifacts/tmp.
# Pass -LoraFile to also apply the NSFW "unlock" LoRA via a LoraLoaderModelOnly node (node 12),
# exactly like helpers/flux-local-host/run-flux-proof.ps1.
# Visual review is REQUIRED before moving any output into a persisted runs/<ts>-<label>/ folder.
param(
    [Parameter(Mandatory=$true)][string]$ComfyUiUrl,
    [Parameter(Mandatory=$false)][string]$CellsPath = "",
    [Parameter(Mandatory=$false)][int]$Seed = 20260907,
    [Parameter(Mandatory=$false)][string]$OutBase = "artifacts/tmp/images/flux-implied-proof-deep",
    [Parameter(Mandatory=$false)][string]$LoraFile = "",   # e.g. "aidmaNSFWunlock-FLUX-V0.2.safetensors"
    [Parameter(Mandatory=$false)][double]$LoraStrength = 0.7,
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
$loraTxt = if ($LoraFile) { "  lora=$LoraFile x $LoraStrength" } else { "  (no lora)" }
Write-Host "Probe: cells=$($cells.Count) seed=$Seed url=$ComfyUiUrl$loraTxt"
foreach ($cell in $cells) {
    Write-Host ""
    Write-Host "== $($cell.id): $($cell.prompt)"
    $wfPath = $workflowPath
    if ($LoraFile) {
        # Bake the LoRA into a temp copy (wire LoraLoaderModelOnly node 12 -> KSampler.model node 3).
        $wf = Get-Content -Raw $workflowPath | ConvertFrom-Json
        $wf.PSObject.Properties["12"].Value.inputs.lora_name = $LoraFile
        $wf.PSObject.Properties["12"].Value.inputs.strength_model = $LoraStrength
        $wf.PSObject.Properties["3"].Value.inputs.model = @("12", 0)
        $wfPath = Join-Path ([IO.Path]::GetTempPath()) ("flux_lora_" + $cell.id + "_" + [guid]::NewGuid().ToString("N") + ".json")
        [IO.File]::WriteAllText($wfPath, ($wf | ConvertTo-Json -Depth 20))
    }
    try {
        & $runnerPath -WorkflowPath $wfPath -ComfyUiUrl $ComfyUiUrl -Seed $Seed `
            -Prefix ("flux-" + $cell.id) -OutputDir (Join-Path $resolvedOut $cell.id) `
            -Prompt $cell.prompt -TimeoutSec $TimeoutSec
    }
    finally {
        if ($wfPath -ne $workflowPath) { Remove-Item -LiteralPath $wfPath -Force -ErrorAction SilentlyContinue }
    }
}
Write-Host ""
Write-Host "Staged under $resolvedOut"
Write-Host "REQUIRED: visually review every PNG (view_image) and record PASS/FAIL before persisting a run."
