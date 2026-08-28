# helpers/test-switch-phase.ps1
# Quick phase switch for testing: DB change + app restart + log capture
# Usage: pwsh helpers/test-switch-phase.ps1 -Phase "Climax" -SessionId "3a74a033"
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("BuildUp","Committed","Approaching","Climax","Reset")]
    [string]$Phase,
    [Parameter(Mandatory=$true)]
    [string]$SessionId
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot/..

Write-Host "=== Switching to $Phase phase ===" -ForegroundColor Cyan

# 1. Update DB
Write-Host "[1] Setting phase to $Phase..." -ForegroundColor DarkGray
$sql = @"
UPDATE RolePlayV2AdaptiveStates SET CurrentPhase = '$Phase' WHERE SessionId = '$SessionId';
"@
$sql | Out-File -Encoding utf8 "artifacts/tmp/dbquery/queries/_temp_switch_phase.sql"
dotnet run --project artifacts/tmp/dbquery -- exec artifacts/tmp/dbquery/queries/_temp_switch_phase.sql 2>&1 | Out-Null
Write-Host "    Done." -ForegroundColor Green

# 2. Kill dotnet processes
Write-Host "[2] Restarting web app..." -ForegroundColor DarkGray
Get-Process -Name dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

# 3. Start web app in background
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run --project DreamGenClone.Web/DreamGenClone.csproj --launch-profile https" -PassThru -NoNewWindow
Write-Host "    Waiting for app to start..." -ForegroundColor DarkGray
Start-Sleep -Seconds 8
Write-Host "    App started (PID $($proc.Id))." -ForegroundColor Green

# 4. Reminder
Write-Host ""
Write-Host "[3] >>> NOW run a continuation in the browser for session $SessionId <<<" -ForegroundColor Yellow
Write-Host "    Press Enter after done to capture logs..."
Read-Host

# 5. Capture logs
Write-Host ""
Write-Host "[4] Latest coordinator entries:" -ForegroundColor Cyan
$today = Get-Date -Format "yyyyMMdd"
$logFile = "DreamGenClone.Web/logs/dreamgenclone-$today.log"
if (Test-Path $logFile) {
    Select-String -Path $logFile -Pattern "Coordinator built prompt.*$SessionId" | Select-Object -Last 5 | ForEach-Object {
        $text = $_.Line
        $p = if ($text -match 'Phase=(\S+)') { $matches[1] } else { '?' }
        $pa = if ($text -match 'Pacing="(\S+)"') { $matches[1] } else { '?' }
        $ts = if ($text -match 'TimeShift="(\S+)"') { $matches[1] } else { '?' }
        $dp = if ($text -match 'Deepening="(\S+)"') { $matches[1] } else { '?' }
        $actor = if ($text -match 'Actor=(\S+)') { $matches[1] } else { '?' }
        Write-Host "  $p | $pa | $ts | $dp | $actor" -ForegroundColor White
    }
} else {
    Write-Warning "Log file not found"
}

Pop-Location
