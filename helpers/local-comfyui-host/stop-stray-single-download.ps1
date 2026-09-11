<#
.SYNOPSIS
  Stops the stray single-connection checkpoint download on the ComfyUI host.

.DESCRIPTION
  An earlier `Start-Process`-launched download survived the SSH disconnect and is competing with the
  8-way parallel fetch for bandwidth. The parallel chunk fetches carry `--range`; the stray one writes
  the assembled file name with no range, so it can be identified and stopped without touching the
  parallel workers.

  Read-only until it finds a match, then it stops only that process. Prints every curl process it sees.
#>
[CmdletBinding()]
param(
    [string]$StrayOutputFragment = 'models\checkpoints\Qwen-Rapid-AIO-NSFW-v23.safetensors'
)

$ErrorActionPreference = 'Stop'

$curls = Get-CimInstance Win32_Process -Filter "Name='curl.exe'"
if (-not $curls) { 'no curl processes found'; exit 0 }

$stray = @()
foreach ($process in $curls) {
    $commandLine = [string]$process.CommandLine
    $isRange = $commandLine.Contains('--range')
    $matchesTarget = $commandLine.Contains($StrayOutputFragment)
    $kind = 'parallel-range'
    if (-not $isRange) { $kind = 'SINGLE-CONNECTION' }
    "pid $($process.ProcessId) [$kind] $commandLine"
    if ((-not $isRange) -and $matchesTarget) { $stray += $process.ProcessId }
}

if ($stray.Count -eq 0) { 'no stray single-connection download found'; exit 0 }

foreach ($processId in $stray) {
    "stopping stray pid $processId"
    Stop-Process -Id $processId -Force
}
"stopped $($stray.Count) stray process(es)"
