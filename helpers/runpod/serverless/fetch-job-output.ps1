# fetch-job-output.ps1 - Fetch a COMPLETED RunPod job's output and save the pose/render PNG.
# Usage: fetch-job-output.ps1 -EndpointKey <key> -JobId <id> [-OutDir <dir>]
param(
    [Parameter(Mandatory=$true)][string]$EndpointKey,
    [Parameter(Mandatory=$true)][string]$JobId,
    [Parameter(Mandatory=$false)][string]$OutDir = "artifacts/tmp/dwpose-smoke"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "..\.runpod-env.ps1")

$reg = Get-Content (Join-Path $scriptDir "endpoints.json") -Raw | ConvertFrom-Json
$ep = $reg.endpoints | Where-Object { $_.endpointKey -eq $EndpointKey }
if (-not $ep) { throw "Endpoint '$EndpointKey' not found in endpoints.json" }
$epId = $ep.endpointId

$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
$st = Invoke-RestMethod -Uri "https://api.runpod.ai/v2/$epId/status/$JobId" -Headers $headers

Write-Host "status: $($st.status)  exec: $($st.executionTime)ms"
if ($st.status -ne "COMPLETED") { Write-Host "Job not COMPLETED - nothing to save"; exit 1 }
if (-not $st.output) { Write-Host "No output object"; exit 1 }

$outDirAbs = Join-Path (Get-Location) $OutDir
New-Item -ItemType Directory -Force -Path $outDirAbs | Out-Null

# Save image_b64 (our handler contract) if present
if ($st.output.image_b64) {
    $b64 = $st.output.image_b64
    if ($b64.StartsWith("data:")) { $b64 = $b64.Split(",", 2)[1] }
    $png = Join-Path $outDirAbs "dwpose_smoke_${JobId}.png"
    [IO.File]::WriteAllBytes($png, [Convert]::FromBase64String($b64))
    Write-Host "saved: $png"
}

# Also save any official-worker-style output.images
if ($st.output.images) {
    $i = 0
    foreach ($img in $st.output.images) {
        $i++
        if ($img.data -and $img.type -eq "base64") {
            $b64 = $img.data
            if ($b64.StartsWith("data:")) { $b64 = $b64.Split(",", 2)[1] }
            $png = Join-Path $outDirAbs "output_${i}_${JobId}.png"
            [IO.File]::WriteAllBytes($png, [Convert]::FromBase64String($b64))
            Write-Host "saved: $png"
        }
    }
}

if ($st.output.keypoints) {
    $kp = Join-Path $outDirAbs "keypoints_${JobId}.json"
    $st.output.keypoints | ConvertTo-Json -Depth 10 | Set-Content -Path $kp
    Write-Host "saved keypoints: $kp"
} else {
    Write-Host "keypoints: (none in output)"
}
