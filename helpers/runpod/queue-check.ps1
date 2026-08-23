# queue-check.ps1 - check ComfyUI queue state (temp)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

Write-Host "=== /queue ==="
$q = Invoke-RestMethod -Uri "$env:COMFYUI_URL/queue" -Method GET -TimeoutSec 30
Write-Host "running count: $($q.queue_running.Count)"
Write-Host "pending count: $($q.queue_pending.Count)"
if ($q.queue_running.Count) { Write-Host "running IDs:"; $q.queue_running | ForEach-Object { Write-Host "  $_" } }
if ($q.queue_pending.Count)  { Write-Host "pending IDs:"; $q.queue_pending | ForEach-Object { Write-Host "  $_" } }