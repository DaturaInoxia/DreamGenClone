# run-sdxl-implied-compare.ps1 - B-112 SDXL-vs-FLUX comparison on the 4 implied
# (non-explicit) cells. Runs each cell on Juggernaut XL and BigLust v1.6 through the SDXL
# natural-language recipe (sdxl-t2i-smoke.json), and on Pony V6 XL through the Pony tag recipe
# (pony-t2i-smoke.json). Results let us compare arrangement fidelity vs the stock-FLUX results
# already in artifacts/tmp/images/flux-implied-proof.
# Content: NON-explicit implied/softcore only (matches prompts-implied.json rubric).
param(
    [Parameter(Mandatory=$true)][string]$ComfyUiUrl,
    [Parameter(Mandatory=$false)][int]$Seed = 20260908,
    [Parameter(Mandatory=$false)][int]$TimeoutSec = 600,
    [Parameter(Mandatory=$false)][string]$OutputDir = "artifacts/tmp/images/sdxl-implied-compare",
    [Parameter(Mandatory=$false)][string]$Only          # optional comma-separated cell ids
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$sdxlWf = Join-Path $PSScriptRoot "sdxl-t2i-smoke.json"
$ponyWf = Join-Path $PSScriptRoot "pony-t2i-smoke.json"
$promptsPath = Join-Path $PSScriptRoot "prompts-implied-sdxl.json"
$runnerPath = Join-Path $repoRoot "helpers\runpod\generate-one.ps1"
foreach ($p in $sdxlWf, $ponyWf, $promptsPath, $runnerPath) {
    if (-not (Test-Path $p -PathType Leaf)) { throw "Required file not found: $p" }
}

$manifest = Get-Content -Raw $promptsPath | ConvertFrom-Json
$cells = @($manifest.cells)
if ($Only) {
    $ids = ($Only -split ',' | ForEach-Object { $_.Trim() })
    $cells = @($cells | Where-Object { $_.id -in $ids })
    if ($cells.Count -eq 0) { throw "No cells matched -Only '$Only'." }
}

$runs = @(
    @{ model = "juggernautXL_ragnarok.safetensors"; wf = $sdxlWf; ckptNode = "1"; field = "sdxl" }
    @{ model = "bigLust_v16.safetensors";            wf = $sdxlWf; ckptNode = "1"; field = "sdxl" }
    @{ model = "ponyDiffusionV6XL_v6.safetensors";   wf = $ponyWf; ckptNode = "4"; field = "pony" }
)
$resolvedOut = Join-Path $repoRoot $OutputDir
Write-Host "SDXL implied compare: url=$ComfyUiUrl cells=$($cells.Count) models=$($runs.Count) seed=$Seed"

foreach ($run in $runs) {
    foreach ($cell in $cells) {
        Write-Host ""
        Write-Host "== $($run.model) | cell $($cell.id)"
        $wf = Get-Content -Raw $run.wf | ConvertFrom-Json
        $wf.PSObject.Properties[$run.ckptNode].Value.inputs.ckpt_name = $run.model
        # positive CLIP text node is 6 in both templates; node 7 is the (quality-guard) negative.
        $wf.PSObject.Properties["6"].Value.inputs.text = $cell.($run.field)
        $tmpWf = Join-Path ([IO.Path]::GetTempPath()) ("sdxl_implied_" + [guid]::NewGuid().ToString("N") + ".json")
        [IO.File]::WriteAllText($tmpWf, ($wf | ConvertTo-Json -Depth 20))
        try {
            $modelTag = ($run.model -replace '\.safetensors$', '')
            $outDir = Join-Path $resolvedOut ($cell.id + "_" + $modelTag)
            & $runnerPath `
                -WorkflowPath $tmpWf `
                -ComfyUiUrl $ComfyUiUrl `
                -Seed $Seed `
                -Prefix ("sdxl-implied-" + $cell.id + "-" + $modelTag) `
                -OutputDir $outDir `
                -TimeoutSec $TimeoutSec
        }
        finally { Remove-Item -LiteralPath $tmpWf -Force -ErrorAction SilentlyContinue }
    }
}

Write-Host ""
Write-Host "SDXL implied outputs in: $resolvedOut"
Write-Host "Open each PNG and compare against artifacts/tmp/images/flux-implied-proof per cell."
