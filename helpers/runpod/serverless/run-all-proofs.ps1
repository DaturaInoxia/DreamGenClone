# run-all-proofs.ps1 - run every READY serverless proof and report a per-workload pass/fail summary
#
# B-101. Drives the serverless migration proof suite from the endpoints.json registry. Each endpoint
# entry can carry a "proof" block describing how to smoke-test that workload and its readiness:
#   proof.status       : "ready" | "pending-upload" | "pending-endpoint" | "pending-download" | "blocked"
#   proof.type         : "dwpose" | "t2i" | "identity" | "qwen-vl" | "qwen-edit"
#   proof.workflowJson : (optional) ComfyUI API JSON to send as input.workflow. Omit -> default DWPose workflow.
#   proof.imagesJson   : (optional) [{name,path}] manifest -> input.images (identity refs+masks, qwen-edit source).
#   proof.imagePath    : (optional) single image -> input.images[0] named input_image.png (dwpose).
#   proof.outDir       : where output PNGs are saved (default artifacts/tmp/proofs/<endpointKey>).
#
# Only proof.status == "ready" entries are executed; everything else is reported as SKIPPED with its
# reason. Outputs are saved and the SAVED:<path> lines are printed so each rendered image can be
# visually verified (identity score, pose skeleton, etc.) before the workload is declared done.
#
# Usage:
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/run-all-proofs.ps1
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/run-all-proofs.ps1 -EndpointKey img-juggernaut-serverless
#   powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/serverless/run-all-proofs.ps1 -DryRun   # print the plan, submit nothing
param(
    [string]$EndpointKey,   # optional: run only this endpoint's proof
    [switch]$DryRun         # print what would run per endpoint without submitting any job
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
Set-Location $repoRoot

$registry = Get-Content -Raw -Path (Join-Path $PSScriptRoot "endpoints.json") | ConvertFrom-Json

# Collect every proof block (each endpoint can carry several: proof, identityProof, ...).
$runs = @()
foreach ($ep in @($registry.endpoints)) {
    if ($ep.proof)          { $runs += [PSCustomObject]@{ Endpoint = $ep; Proof = $ep.proof } }
    if ($ep.identityProof)  { $runs += [PSCustomObject]@{ Endpoint = $ep; Proof = $ep.identityProof } }
}
if ($EndpointKey) { $runs = @($runs | Where-Object { $_.Endpoint.endpointKey -eq $EndpointKey }) }
if ($runs.Count -eq 0) {
    if ($EndpointKey) { throw "No endpoint '$EndpointKey' with a proof block in endpoints.json." }
    Write-Host "No ready proofs to run (no endpoint has a proof block)."
    exit 0
}

$results = @()
foreach ($run in $runs) {
    $entry = $run.Endpoint
    $proof = $run.Proof
    $key = $entry.endpointKey
    Write-Host ""
    Write-Host "=== $key ($($entry.function)) - $($proof.type): status=$($proof.status) ==="

    if ($proof.status -ne "ready") {
        Write-Host "  SKIP ($($proof.status)): $($proof.reason)"
        $results += [PSCustomObject]@{ Endpoint = $key; Status = $proof.status; Result = "SKIP" }
        continue
    }

    if ([string]::IsNullOrWhiteSpace($entry.endpointId)) {
        Write-Host "  SKIP (no endpointId yet): create the endpoint first."
        $results += [PSCustomObject]@{ Endpoint = $key; Status = $proof.status; Result = "SKIP" }
        continue
    }

    if ($DryRun) {
        Write-Host "  WOULD-RUN type=$($proof.type) workflow=$($proof.workflowJson) images=$($proof.imagesJson) image=$($proof.imagePath)"
        $results += [PSCustomObject]@{ Endpoint = $key; Status = $proof.status; Result = "WOULD-RUN" }
        continue
    }

    $outDir = $proof.outDir
    if (-not $outDir) { $outDir = "artifacts/tmp/proofs/$key" }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $args = @("-EndpointKey", $key, "-OutDir", $outDir)
    if ($proof.workflowJson) { $args += @("-WorkflowJsonPath", $proof.workflowJson) }
    if ($proof.imagesJson)   { $args += @("-ImagesJsonPath", $proof.imagesJson) }
    if ($proof.imagePath)    { $args += @("-ImagePath", $proof.imagePath) }

    $saved = @()
    try {
        $output = & (Join-Path $PSScriptRoot "smoke-test.ps1") @args 2>&1
        $output | ForEach-Object { Write-Host "  $_" }
        foreach ($line in $output) {
            if ($line -match '^SAVED:(.+)$') { $saved += $Matches[1] }
        }
        $lastLine = ($output | Select-Object -Last 1)
        if ($lastLine -match 'PASS:') {
            $results += [PSCustomObject]@{ Endpoint = $key; Status = $proof.status; Result = "PASS" }
        } else {
            $results += [PSCustomObject]@{ Endpoint = $key; Status = $proof.status; Result = "FAIL" }
        }
    } catch {
        Write-Host "  ERROR running smoke test: $($_.Exception.Message)"
        $results += [PSCustomObject]@{ Endpoint = $key; Status = $proof.status; Result = "FAIL" }
    }
}

Write-Host ""
Write-Host "==================== PROOF SUMMARY ===================="
$results | Format-Table -AutoSize
$failed = @($results | Where-Object { $_.Result -eq "FAIL" })
Write-Host ""
Write-Host "Saved renders to artifacts/tmp/proofs/<endpointKey>/ - visually verify each PNG before declaring the workload done."
if ($failed.Count -gt 0) {
    Write-Host "RESULT: $($failed.Count) FAILED ($(($results | Where-Object {$_.Result -eq 'FAIL'}).Endpoint -join ', '))"
    exit 1
}
Write-Host "RESULT: all executed proofs PASSED."
