# helpers/run-marker-test.ps1
# Execute a single marker test case and capture results.
# Usage: pwsh helpers/run-marker-test.ps1 -TestId "T1.1" -SessionId "adc0acfe" -Marker "[Pacing:slow]"
param(
    [Parameter(Mandatory=$true)]
    [string]$TestId,
    [Parameter(Mandatory=$true)]
    [string]$SessionId,
    [Parameter(Mandatory=$false)]
    [string]$Marker = "",
    [Parameter(Mandatory=$false)]
    [string]$ExpectedPacing = "",
    [Parameter(Mandatory=$false)]
    [string]$ExpectedDeepening = "",
    [Parameter(Mandatory=$false)]
    [string]$ExpectedFiringExtra = "",
    [Parameter(Mandatory=$false)]
    [string]$ExpectedFiringMissing = ""
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot/..

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " TEST: $TestId" -ForegroundColor Cyan
Write-Host " Session: $SessionId" -ForegroundColor Cyan
Write-Host " Marker: $Marker" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Take a snapshot of current logs (line count before test)
$today = Get-Date -Format "yyyyMMdd"
$logFile = "DreamGenClone.Web/logs/dreamgenclone-$today.log"
$logBefore = 0
if (Test-Path $logFile) {
    $logBefore = (Get-Content $logFile | Measure-Object -Line).Lines
}
Write-Host "[1] Log snapshot: $logBefore lines before test" -ForegroundColor DarkGray

# Step 2: Apply marker if specified
if ($Marker) {
    Write-Host "[2] Would apply marker: $Marker" -ForegroundColor Yellow
    Write-Host "    Run manually: UPDATE RPThemePhaseGuidance SET GuidanceText = '$Marker' || char(10) || GuidanceText WHERE ..." -ForegroundColor DarkGray
}

# Step 3: Wait for user to run continuation
Write-Host ""
Write-Host "[3] >>> NOW run a continuation in the browser for session $SessionId <<<" -ForegroundColor Green
Write-Host "    Press Enter after the continuation completes..." -ForegroundColor Green
Read-Host

# Step 4: Capture new log entries
Write-Host ""
Write-Host "[4] Capturing coordinator logs..." -ForegroundColor DarkGray
$logAfter = 0
if (Test-Path $logFile) {
    $logAfter = (Get-Content $logFile | Measure-Object -Line).Lines
}
$newLines = $logAfter - $logBefore
Write-Host "    $newLines new log lines" -ForegroundColor DarkGray

# Get the last coordinator entry for this session
$lastEntry = Select-String -Path $logFile -Pattern "Coordinator built prompt.*$SessionId" | Select-Object -Last 1

if (-not $lastEntry) {
    Write-Host "    ❌ No coordinator log entry found!" -ForegroundColor Red
    exit 1
}

$text = $lastEntry.Line
$phase = if ($text -match 'Phase=(\S+)') { $matches[1] } else { '?' }
$pos = if ($text -match 'PositionInTurn=(\S+)') { $matches[1] } else { 'null' }
$actor = if ($text -match 'Actor=(\S+)') { $matches[1] } else { '?' }
$intent = if ($text -match 'Intent="(\S+)"') { $matches[1] } else { '?' }
$pacing = if ($text -match 'Pacing="(\S+)"') { $matches[1] } else { '?' }
$timeshift = if ($text -match 'TimeShift="(\S+)"') { $matches[1] } else { '?' }
$deepening = if ($text -match 'Deepening="(\S+)"') { $matches[1] } else { '?' }
$theme = if ($text -match 'ActiveThemeId=(\S+)') { $matches[1] } else { '?' }
$firing = if ($text -match 'FiringSequence=(.+)$') { $matches[1] } else { '?' }

Write-Host ""
Write-Host "--- RESULT ---" -ForegroundColor Yellow
Write-Host "  Phase:       $phase"
Write-Host "  Position:    $pos"
Write-Host "  Actor:       $actor"
Write-Host "  Intent:      $intent"
Write-Host "  Theme:       $theme"
Write-Host "  Pacing:      $pacing"
Write-Host "  TimeShift:   $timeshift"
Write-Host "  Deepening:   $deepening"
Write-Host "  Firing:      $firing"

# Step 5: Validate
Write-Host ""
Write-Host "--- VALIDATION ---" -ForegroundColor Yellow
$pass = $true

if ($ExpectedPacing -and $pacing -ne $ExpectedPacing) {
    Write-Host "  ❌ Pacing: expected '$ExpectedPacing', got '$pacing'" -ForegroundColor Red
    $pass = $false
} elseif ($ExpectedPacing) {
    Write-Host "  ✅ Pacing: $ExpectedPacing" -ForegroundColor Green
}

if ($ExpectedDeepening -and $deepening -ne $ExpectedDeepening) {
    Write-Host "  ❌ Deepening: expected '$ExpectedDeepening', got '$deepening'" -ForegroundColor Red
    $pass = $false
} elseif ($ExpectedDeepening) {
    Write-Host "  ✅ Deepening: $ExpectedDeepening" -ForegroundColor Green
}

if ($ExpectedFiringExtra -and $firing -notmatch $ExpectedFiringExtra) {
    Write-Host "  ❌ Missing injector in sequence: $ExpectedFiringExtra" -ForegroundColor Red
    $pass = $false
} elseif ($ExpectedFiringExtra) {
    Write-Host "  ✅ Injector present: $ExpectedFiringExtra" -ForegroundColor Green
}

if ($ExpectedFiringMissing -and $firing -match $ExpectedFiringMissing) {
    Write-Host "  ❌ Unexpected injector in sequence: $ExpectedFiringMissing" -ForegroundColor Red
    $pass = $false
} elseif ($ExpectedFiringMissing) {
    Write-Host "  ✅ Injector absent: $ExpectedFiringMissing" -ForegroundColor Green
}

Write-Host ""
if ($pass) {
    Write-Host "=== TEST $TestId : PASS ===" -ForegroundColor Green
} else {
    Write-Host "=== TEST $TestId : FAIL ===" -ForegroundColor Red
}
