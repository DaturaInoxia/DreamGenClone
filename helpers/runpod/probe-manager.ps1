# probe-manager.ps1 - check whether the pod has ComfyUI Manager installed and
# whether its model-download HTTP API is reachable
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

function Try-Probe($name, $url) {
    try {
        $r = Invoke-RestMethod -Uri $url -Method GET -TimeoutSec 20
        Write-Host "[$name] OK"
        ($r | ConvertTo-Json -Depth 5) -split "`n" | Select-Object -First 25 | ForEach-Object { Write-Host "    $_" }
    } catch {
        Write-Host "[$name] FAIL: $($_.Exception.Message)"
    }
}

Try-Probe "manager/models"        "$env:COMFYUI_URL/api/manager/models"
Try-Probe "object_info/KSampler"  "$env:COMFYUI_URL/object_info/KSampler"
Try-Probe "system_stats"          "$env:COMFYUI_URL/system_stats"