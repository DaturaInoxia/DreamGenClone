# queue-stop.ps1 - interrupt current job and clear pending queue (temp)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

Write-Host "=== interrupt current ==="
try { Invoke-RestMethod -Uri "$env:COMFYUI_URL/interrupt" -Method POST -TimeoutSec 15 } catch { Write-Host "interrupt: $($_.Exception.Message)" }

Write-Host "=== clear queue ==="
$payload = @{ clear = $true }
try { Invoke-RestMethod -Uri "$env:COMFYUI_URL/queue" -Method POST -ContentType "application/json" -Body ($payload | ConvertTo-Json) -TimeoutSec 15 } catch { Write-Host "clear: $($_.Exception.Message)" }

Start-Sleep -Seconds 2
$q = Invoke-RestMethod -Uri "$env:COMFYUI_URL/queue" -Method GET -TimeoutSec 15
Write-Host "after clear -> running: $($q.queue_running.Count), pending: $($q.queue_pending.Count)"