# probe-jupyter.ps1 - try to reach the pod's port-8888 service (likely JupyterLab)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

# 8888 maps to publicPort 60536 on the proxy host
$candidates = @(
    "https://qguv5e029u58lb-8888.proxy.runpod.net/",
    "https://qguv5e029u58lb-60536.proxy.runpod.net/",
    "https://100.65.27.200:60536/",
    "$env:COMFYUI_URL/../"  # relative hop unlikely; leave for completeness
)

foreach ($u in $candidates) {
    try {
        $r = Invoke-WebRequest -Uri $u -Method GET -TimeoutSec 20 -MaximumRedirection 0 -ErrorAction Stop
        Write-Host "[$u] -> $($r.StatusCode) ($($r.StatusDescription))"
        if ($r.Content -match "jupyter|terminals|tree|lab") { Write-Host "    -> looks like JupyterLab" }
    } catch {
        $resp = $_.Exception.Response
        if ($resp) {
            Write-Host "[$u] -> HTTP $([int]$resp.StatusCode)"
        } else {
            Write-Host "[$u] -> ERR $($_.Exception.Message)"
        }
    }
}