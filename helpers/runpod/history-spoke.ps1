# history-detail.ps1 - dump workflows from recent ComfyUI history (temp)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

$h = Invoke-RestMethod -Uri "$env:COMFYUI_URL/history" -Method GET -TimeoutSec 30
$names = @($h.PSObject.Properties.Name) | Select-Object -Last 3
foreach ($id in $names) {
    $e = $h.$id
    Write-Host "=== prompt $id status=$($e.status.status_str) ==="
    $wf = $e.prompt
    if ($wf) {
        Write-Host "  node4 ckpt: $($wf.PSObject.Properties['4'].Value.inputs.ckpt_name)"
        Write-Host "  node10 cls: $($wf.PSObject.Properties['10'].Value.class_type)"
        $s = $wf.PSObject.Properties['3'].Value.inputs
        if ($s) { Write-Host "  sampler: $($s.sampler_name) steps=$($s.steps) cfg=$($s.cfg) seed=$($s.seed)" }
        $pos = $wf.PSObject.Properties['6'].Value.inputs.text
        if ($pos) { Write-Host "  positive[0..180]: $($pos.Substring(0, [Math]::Min(180,$pos.Length)))" }
        $neg = $wf.PSObject.Properties['7'].Value.inputs.text
        if ($neg) { Write-Host "  negative[0..120]: $($neg.Substring(0, [Math]::Min(120,$neg.Length)))" }
    }
}