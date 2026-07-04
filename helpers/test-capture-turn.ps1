# helpers/test-capture-turn.ps1
# Capture coordinator injection + interaction output for latest turn.
# Usage: pwsh helpers/test-capture-turn.ps1 -SessionId "3a74a033" -TurnLabel "BuildUp-T1"
param(
    [Parameter(Mandatory=$true)][string]$SessionId,
    [Parameter(Mandatory=$true)][string]$TurnLabel
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot/..

$today = Get-Date -Format "yyyyMMdd"
$logFile = "DreamGenClone.Web/logs/dreamgenclone-$today.log"
$resultsFile = "specs/debug/scene-direction-marker-tests/results-$SessionId.md"

Write-Host "=== TURN: $TurnLabel ===" -ForegroundColor Cyan

# 1. Coordinator
$coordEntries = Select-String -Path $logFile -Pattern "Coordinator built prompt.*$SessionId" | Select-Object -Last 5
if ($coordEntries.Count -eq 0) {
    Write-Warning "No coordinator entries. Run a continuation first."
    Pop-Location; return
}

Write-Host "--- COORDINATOR INJECTION ---" -ForegroundColor Yellow
$idx = 0; $firstPhase = "?"; $firstPacing = "?"; $firstTs = "?"; $firstDeep = "?"
foreach ($entry in $coordEntries) {
    $idx++; $text = $entry.Line
    if ($text -match 'Phase=(\S+)') { $phase = $matches[1] } else { $phase = '?' }
    if ($text -match 'PositionInTurn=(\S+)') { $pos = $matches[1] } else { $pos = 'null' }
    if ($text -match 'Actor=(\S+)') { $actor = $matches[1] } else { $actor = '?' }
    if ($text -match 'Intent="([^"]+)"') { $intent = $matches[1] } else { $intent = '?' }
    if ($text -match 'Pacing="([^"]+)"') { $pacing = $matches[1] } else { $pacing = '?' }
    if ($text -match 'TimeShift="([^"]+)"') { $ts = $matches[1] } else { $ts = '?' }
    if ($text -match 'Deepening="([^"]+)"') { $deep = $matches[1] } else { $deep = '?' }
    if ($text -match 'FiringSequence=(.+)$') { $firing = $matches[1] } else { $firing = '?' }
    if ($idx -eq 1) { $firstPhase = $phase; $firstPacing = $pacing; $firstTs = $ts; $firstDeep = $deep }
    Write-Host "  [$idx] $phase pos=$pos $actor ($intent) | $pacing | $ts | $deep"
}
Write-Host ""

# 2. Interaction output
Write-Host "--- LATEST INTERACTION OUTPUT ---" -ForegroundColor Yellow
python helpers/capture-interactions.py $SessionId 3

# 3. Results file
$marker = "## $TurnLabel`n`n**Phase**: $firstPhase | **Pacing**: $firstPacing | **TimeShift**: $firstTs | **Deepening**: $firstDeep`n`n"
if (-not (Test-Path $resultsFile)) {
    "# Session $SessionId - Per-Turn Results`n`n" | Out-File -Encoding utf8 $resultsFile
}
$marker | Out-File -Encoding utf8 -Append $resultsFile
Write-Host "Saved: $resultsFile" -ForegroundColor DarkGray

Pop-Location
