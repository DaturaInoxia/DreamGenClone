# runpod-billing-query.ps1 - query RunPod billing (GET /v2/billing) with custom params.
# Promoted from artifacts/tmp/runpod-billing-query.ps1 (2026-09-02).
# Usage: powershell -File helpers/runpod/runpod-billing-query.ps1 [-StartTime "2026-08-28T00:00:00Z"] [-EndTime "2026-08-30T00:00:00Z"] [-BucketSize day]
param(
    [string]$StartTime = "",
    [string]$EndTime = "",
    [ValidateSet("hour","day","month")]
    [string]$BucketSize = "day"
)
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

$headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
$uri = "https://api.runpod.io/v2/billing"
$params = @{}
if ($StartTime) { $params.startTime = $StartTime }
if ($EndTime)   { $params.endTime = $EndTime }
$params.bucketSize = $BucketSize

$query = ($params.GetEnumerator() | ForEach-Object { "{0}={1}" -f $_.Key, [uri]::EscapeDataString($_.Value) }) -join "&"
$full = $uri + "?" + $query
$resp = Invoke-RestMethod -Uri $full -Method GET -Headers $headers

$m = $resp.metadata
Write-Host ("Bucket: {0}  Range: {1} -> {2}  Records: {3}" -f $m.query.bucketSize, $m.query.startTime, $m.query.endTime, $m.recordCount)
$t = $m.totals
Write-Host ("TOTALS: podGpu={0:N3} podDisk={1:N3} serverlessGpu={2:N3} serverlessFee={3:N3} serverlessDisk={4:N3} storage={5:N3} TOTAL={6:N3}" -f `
    $t.podGpuAmount, $t.podDiskAmount, $t.serverlessGpuAmount, $t.serverlessFeeAmount, $t.serverlessDiskAmount, $t.storageStandardAmount, $t.totalAmount)
Write-Host "--- per-bucket ---"
foreach ($r in $resp.records) {
    Write-Host ("{0}: podGpu={1,7:N3} podDisk={2,6:N3} srvGpu={3,6:N3} srvFee={4,6:N3} srvDisk={5,6:N4} storage={6,6:N3} = {7,8:N3}" -f `
        $r.endTime.Substring(0,10), $r.podGpuAmount, $r.podDiskAmount, $r.serverlessGpuAmount, $r.serverlessFeeAmount, $r.serverlessDiskAmount, $r.storageStandardAmount, $r.totalAmount)
}
