# probe-checkpoints.ps1 - list checkpoints visible to ComfyUI API (temp)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

$r = Invoke-RestMethod -Uri "$env:COMFYUI_URL/object_info/CheckpointLoaderSimple" -Method GET -TimeoutSec 30
Write-Host "=== Checkpoints visible to ComfyUI ==="
$r.CheckpointLoaderSimple.input.required.ckpt_name[0] | ForEach-Object { Write-Host "  - $_" }