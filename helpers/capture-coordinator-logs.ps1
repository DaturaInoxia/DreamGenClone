# helpers/capture-coordinator-logs.ps1
# Capture coordinator logs for a specific session ID.
# Usage: pwsh helpers/capture-coordinator-logs.ps1 -SessionId "adc0acfe" [-Last 20]
param(
    [Parameter(Mandatory=$true)]
    [string]$SessionId,
    [int]$Last = 30
)

$logsDir = Join-Path $PSScriptRoot ".." "DreamGenClone.Web" "logs"
$today = Get-Date -Format "yyyyMMdd"
$logFile = Join-Path $logsDir "dreamgenclone-$today.log"

if (-not (Test-Path $logFile)) {
    Write-Warning "Log file not found: $logFile"
    # Try yesterday
    $yesterday = (Get-Date).AddDays(-1).ToString("yyyyMMdd")
    $logFile = Join-Path $logsDir "dreamgenclone-$yesterday.log"
    if (-not (Test-Path $logFile)) {
        Write-Error "No log file found for today or yesterday"
        exit 1
    }
}

Write-Host "=== Coordinator logs for session $SessionId ===" -ForegroundColor Cyan
Write-Host "Source: $logFile" -ForegroundColor DarkGray
Write-Host ""

$lines = Select-String -Path $logFile -Pattern "Coordinator built prompt.*$SessionId" | Select-Object -Last $Last

if ($lines.Count -eq 0) {
    Write-Warning "No coordinator log entries found for session $SessionId"
    exit 0
}

$idx = 0
foreach ($line in $lines) {
    $idx++
    # Parse the log line
    $text = $line.Line
    
    # Extract key fields
    $phase = if ($text -match 'Phase=(\S+)') { $matches[1] } else { '?' }
    $pos = if ($text -match 'PositionInTurn=(\S+)') { $matches[1] } else { 'null' }
    $actor = if ($text -match 'Actor=(\S+)') { $matches[1] } else { '?' }
    $intent = if ($text -match 'Intent="(\S+)"') { $matches[1] } else { '?' }
    $pacing = if ($text -match 'Pacing="(\S+)"') { $matches[1] } else { '?' }
    $timeshift = if ($text -match 'TimeShift="(\S+)"') { $matches[1] } else { '?' }
    $deepening = if ($text -match 'Deepening="(\S+)"') { $matches[1] } else { '?' }
    $theme = if ($text -match 'ActiveThemeId=(\S+)') { $matches[1] } else { '?' }
    $firing = if ($text -match 'FiringSequence=(.+)$') { $matches[1] } else { '?' }
    
    Write-Host "[$idx]" -ForegroundColor Yellow -NoNewline
    Write-Host " phase=$phase pos=$pos actor=$actor intent=$intent" -ForegroundColor White
    Write-Host "      " -NoNewline
    Write-Host "pacing=$pacing timeshift=$timeshift deepening=$deepening theme=$theme" -ForegroundColor Gray
    Write-Host "      " -NoNewline
    Write-Host "firing=$firing" -ForegroundColor DarkGray
    Write-Host ""
}

Write-Host "=== $($lines.Count) entries ===" -ForegroundColor Cyan
