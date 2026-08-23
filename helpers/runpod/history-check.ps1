# history-check.ps1 - count recent ComfyUI history entries (temp)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

$h = Invoke-RestMethod -Uri "$env:COMFYUI_URL/history" -Method GET -TimeoutSec 30
$names = @($h.PSObject.Properties.Name)
Write-Host "history entries: $($names.Count)"
# Show last 5 entries with status and output count
$last = $names | Select-Object -Last 5
foreach ($id in $last) {
    $e = $h.$id
    $status = $e.status.status_str
    $outs = @($e.outputs.PSObject.Properties)
    Write-Host "  $id  status=$status  outputs=$($outs.Count)"
}