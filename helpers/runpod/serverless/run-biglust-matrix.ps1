# run-biglust-matrix.ps1 - generate the whole BigLust test-matrix prompt catalog, one dated run
#
# Drives the img-biglust-serverless endpoint (official worker-comfyui contract) for every cell in
# specs/image-generator-tests/TEST-MATRIX-PROMPTS.json. Each run is stored by DATE so runs can be
# compared:
#   specs/image-generator-tests/biglust/runs/<yyyy-MM-dd_HHmmss>[-<RunLabel>]/
#     ├── images/<cellId>.png
#     ├── prompts/<cellId>.json
#     └── manifest.json            # per-cell id/suite/pose/seed/prompt/jobId/sha256 + runDir
#
# Outputs are SOURCE-CONTROLLED (specs/image-generator-tests/), NOT artifacts/tmp. Deterministic
# per-cell seeds (SeedStart + index) are frozen into the workflows.
#
# Usage:
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/run-biglust-matrix.ps1 -DryRun
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/run-biglust-matrix.ps1
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/run-biglust-matrix.ps1 -RunLabel v2
param(
    [string]$PromptsJson      = "specs/image-generator-tests/TEST-MATRIX-PROMPTS.json",
    [string]$WorkflowTemplate = "helpers/runpod/workflows/biglust-t2i.json",
    [string]$RunDir           = "",    # override; default = biglust/runs/<timestamp>[-RunLabel]
    [string]$RunLabel         = "",    # optional suffix appended to the run folder name
    [int]$SeedStart           = 0,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
Set-Location $repoRoot

. (Join-Path $PSScriptRoot "..\common.ps1")
Get-RunPodEnv

# ---- Run directory (dated, for cross-run comparison) ----
if (-not $RunDir) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
    $runName = if ($RunLabel) { "$stamp-$RunLabel" } else { $stamp }
    $RunDir = "specs/image-generator-tests/biglust/runs/$runName"
}
$ImagesOut  = Join-Path $RunDir "images"
$PromptsOut = Join-Path $RunDir "prompts"
$ManifestOut = Join-Path $RunDir "manifest.json"

$registry = Get-Content -Raw -Path (Join-Path $PSScriptRoot "endpoints.json") | ConvertFrom-Json
$entry = $registry.endpoints | Where-Object { $_.endpointKey -eq "img-biglust-serverless" }
if ($null -eq $entry) { throw "endpoints.json has no 'img-biglust-serverless' entry." }
$endpointId = $entry.endpointId
if ([string]::IsNullOrWhiteSpace($endpointId)) { throw "img-biglust-serverless has no endpointId yet." }
$base = "https://api.runpod.ai/v2/$endpointId"
$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }

$templateRaw = Get-Content -Raw -Path $WorkflowTemplate
$matrix = Get-Content -Raw -Path $PromptsJson | ConvertFrom-Json

if (-not $DryRun) {
    New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot $ImagesOut)  | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot $PromptsOut) | Out-Null
}

$results = @()
if ($SeedStart -eq 0) { $SeedStart = Get-Random -Minimum 1 -Maximum 2147483647 }
$seed = $SeedStart

foreach ($suite in $matrix.suites) {
    foreach ($cell in $suite.cells) {
        $id = $cell.id
        $title = $cell.title
        $pose = $cell.pose
        $prompt = $cell.prompt

        # Fresh parse per cell -> independent workflow object.
        $wf = $templateRaw | ConvertFrom-Json
        $wf."2".inputs.text = $prompt
        $wf."5".inputs.seed = $seed
        $wf."7".inputs.filename_prefix = $id

        if ($DryRun) {
            Write-Host "WOULD-RUN $id  (suite=$($suite.id) seed=$seed -> $RunDir)"
            $seed++
            continue
        }

        $wfJson = $wf | ConvertTo-Json -Depth 20
        $promptPath = Join-Path $repoRoot (Join-Path $PromptsOut "$id.json")
        [IO.File]::WriteAllText($promptPath, $wfJson)

        Write-Host ""
        Write-Host "=== $id : $title (seed $seed) ==="

        $jobInput = @{ workflow = $wf; images = @() }
        $body = @{ input = $jobInput } | ConvertTo-Json -Depth 20

        try {
            $submit = Invoke-RestMethod -Uri "$base/run" -Method POST -Headers $headers `
                -ContentType "application/json" -Body $body -TimeoutSec 60
        } catch {
            Write-Host "  SUBMIT ERROR: $($_.Exception.Message)"
            $results += [PSCustomObject]@{ id = $id; suite = $suite.id; pose = $pose; seed = $seed; result = "FAIL"; jobId = ""; image = ""; sha256 = "" }
            $seed++
            continue
        }

        $jobId = $submit.id
        Write-Host "  submitted job $jobId"

        $st = $null
        for ($i = 1; $i -le 60; $i++) {   # up to ~10 min per job
            Start-Sleep -Seconds 10
            try {
                $st = Invoke-RestMethod -Uri "$base/status/$jobId" -Headers $headers -TimeoutSec 60
            } catch {
                continue
            }
            Write-Host "  poll $i : $($st.status)"
            if ($st.status -in @("COMPLETED", "FAILED", "CANCELLED", "TIMED_OUT")) { break }
        }

        $imgPath = ""
        $sha = ""
        $result = "FAIL"
        if ($st -and $st.status -eq "COMPLETED") {
            $img = $null
            if ($st.output.images -and $st.output.images.Count -gt 0) { $img = $st.output.images[0] }
            if ($img -and $img.data) {
                $raw = $img.data
                if ($raw -match '^data:') { $raw = ($raw -split ',', 2)[1] }
                $bytes = [Convert]::FromBase64String($raw)
                $outFile = Join-Path $repoRoot (Join-Path $ImagesOut "$id.png")
                [IO.File]::WriteAllBytes($outFile, $bytes)
                $imgPath = $outFile.Substring($repoRoot.Length + 1).Replace('\', '/')
                $sha = (Get-FileHash -Algorithm SHA256 -Path $outFile).Hash
                Write-Host "  SAVED: $imgPath"
                $result = "PASS"
            } else {
                Write-Host "  COMPLETED but no output.images[].data"
            }
        } else {
            $finalStatus = if ($st) { $st.status } else { "TIMEOUT" }
            Write-Host "  FAIL (status=$finalStatus)"
        }

        $results += [PSCustomObject]@{
            id = $id; suite = $suite.id; pose = $pose; seed = $seed; prompt = $prompt
            result = $result; jobId = $jobId; image = $imgPath; sha256 = $sha
        }
        $seed++
    }
}

Write-Host ""
Write-Host "==================== BIGLUST MATRIX SUMMARY ($RunDir) ===================="
$results | Format-Table id, suite, seed, result -AutoSize

if (-not $DryRun) {
    $manifest = [ordered]@{
        suite        = "biglust-matrix"
        model        = $matrix.model
        runDir       = $RunDir
        generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
        cells        = @($results)
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText((Join-Path $repoRoot $ManifestOut), $manifestJson)
    Write-Host ""
    Write-Host "Manifest: $ManifestOut"
}

$failed = @($results | Where-Object { $_.result -eq "FAIL" })
if ($failed.Count -gt 0) {
    Write-Host "RESULT: $($failed.Count) FAILED ($($failed.id -join ', '))"
    exit 1
}
Write-Host "RESULT: all cells $($results.Count -gt 0) PASSED."
