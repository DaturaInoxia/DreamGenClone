# run-biglust-keyframes.ps1 - render implied-adult KEYFRAME stills with BigLust v1.6
# on the local ComfyUI host (SDXL natural-language recipe, sdxl-t2i-smoke.json).
#
# Keyframes are the START frames for the uncensored-14B video proof cells: they are
# rendered IN THE TARGET SCENE so I2V only adds motion (never scene-replaces). Portrait
# 832x1216. Implied/softcore register only (see helpers/wan-local-host/README.md).
param(
    [Parameter(Mandatory = $true)][string]$ComfyUiUrl,
    [Parameter(Mandatory = $false)][int]$Seed = 20260908,
    [Parameter(Mandatory = $false)][int]$TimeoutSec = 600,
    [Parameter(Mandatory = $false)][string]$OutDir = "artifacts/tmp/wan-proof/keyframes",
    [Parameter(Mandatory = $false)][string]$Only
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $here "..\..")).Path
$sdxlWf = Join-Path $here "..\flux-local-host\sdxl-t2i-smoke.json"
$promptsPath = Join-Path $here "prompts-keyframes.json"
$runnerPath = Join-Path $repoRoot "helpers\runpod\generate-one.ps1"
foreach ($p in $sdxlWf, $promptsPath, $runnerPath) {
    if (-not (Test-Path $p -PathType Leaf)) { throw "Required file not found: $p" }
}

$manifest = Get-Content -Raw $promptsPath | ConvertFrom-Json
$cells = @($manifest.cells)
if ($Only) {
    $ids = ($Only -split ',' | ForEach-Object { $_.Trim() })
    $cells = @($cells | Where-Object { $_.id -in $ids })
    if ($cells.Count -eq 0) { throw "No cells matched -Only '$Only'." }
}

$resolvedOut = Join-Path $repoRoot $OutDir
Write-Host "BigLust keyframes: url=$ComfyUiUrl cells=$($cells.Count) seed=$Seed out=$resolvedOut"

foreach ($cell in $cells) {
    Write-Host ""
    Write-Host "== bigLust | keyframe $($cell.id)"
    $wf = Get-Content -Raw $sdxlWf | ConvertFrom-Json
    $wf.PSObject.Properties["1"].Value.inputs.ckpt_name = "bigLust_v16.safetensors"
    $wf.PSObject.Properties["6"].Value.inputs.text = $cell.sdxl
    $tmpWf = Join-Path ([IO.Path]::GetTempPath()) ("biglust_kf_" + [guid]::NewGuid().ToString("N") + ".json")
    [IO.File]::WriteAllText($tmpWf, ($wf | ConvertTo-Json -Depth 20))
    try {
        $outFile = Join-Path $resolvedOut "$($cell.id).png"
        & $runnerPath `
            -WorkflowPath $tmpWf `
            -ComfyUiUrl $ComfyUiUrl `
            -Seed $Seed `
            -Prefix ("biglust-kf-" + $cell.id) `
            -OutputDir $outFile `
            -TimeoutSec $TimeoutSec
        if (Test-Path $outFile) {
            Write-Host "KEYFRAME -> $outFile ($([math]::Round((Get-Item $outFile).Length / 1KB)) KB)"
        } else {
            Write-Host "WARN: no file at $outFile"
        }
    }
    finally { Remove-Item -LiteralPath $tmpWf -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host "BigLust keyframes in: $resolvedOut"
Write-Host "Review each PNG (implied/softcore register), then feed as start frames to run-wan-14b-proof.py."
