# history-last.ps1 - dump the most recent ComfyUI history prompt+negative (temp)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

$h = Invoke-RestMethod -Uri "$env:COMFYUI_URL/history" -Method GET -TimeoutSec 30
$names = @($h.PSObject.Properties.Name)
if ($names.Count -eq 0) { Write-Host "no history"; exit 0 }
$lastId = $names[-1]
$e = $h.$lastId
Write-Host "=== last prompt $lastId status=$($e.status.status_str) ==="
$wf = $e.prompt
if ($wf) {
  $pos = $wf.PSObject.Properties['6'].Value.inputs.text
  Write-Host "--- POSITIVE ---"
  Write-Host $pos
  $neg = $wf.PSObject.Properties['7'].Value.inputs.text
  Write-Host "--- NEGATIVE ---"
  Write-Host $neg
  $ckpt = $wf.PSObject.Properties['4'].Value.inputs.ckpt_name
  Write-Host "--- ckpt: $ckpt"
  $s = $wf.PSObject.Properties['3'].Value.inputs
  Write-Host "--- sampler: $($s.sampler_name) steps=$($s.steps) cfg=$($s.cfg) seed=$($s.seed)"
}