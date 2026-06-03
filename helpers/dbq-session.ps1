# helpers/dbq-session.ps1
# Full RP session analysis — runs all standard queries for a given session ID.
# Trusted script: VS Code does not prompt for confirmation when called by the agent.
#
# Usage: powershell -ExecutionPolicy RemoteSigned -File helpers/dbq-session.ps1 -SessionId <guid>
# Example: powershell -ExecutionPolicy RemoteSigned -File helpers/dbq-session.ps1 -SessionId a03014bb-7049-4c36-b755-25dff9612fd5
#
# Sections:
#   SESSION OVERVIEW, TURN + INTERACTION COUNTS, TURNS,
#   PHASE SUMMARY (BuildUp/Committed/Approaching/Climax/Reset counters),
#   PHASE TRANSITIONS, PHASE TRANSITION BLOCKERS,
#   INTENSITY PROFILE LADDER,
#   THEME OBSERVER STATE, THEME SCORES,
#   CHARACTER SNAPSHOTS, STAT DELTA BREAKDOWNS,
#   CANDIDATE EVALUATIONS, GATE EVALUATIONS,
#   SEMANTIC ANALYSIS STATE, SEMANTIC EVIDENCE APPLIED,
#   DEBUG EVENT TIMELINE, PROMPT HARD CONSTRAINTS

param(
    [Parameter(Mandatory=$true)]
    [string]$SessionId
)

Set-Location $PSScriptRoot/..

$q   = 'artifacts/tmp/dbquery/queries'
$dbq = 'artifacts/tmp/dbquery'

# Ladder label lookup — matches IntensityLadder.cs
$LadderLabels = @{
    0 = 'Atmospheric (Intro)'
    1 = 'Emotional'
    2 = 'Suggestive'
    3 = 'Sensual'
    4 = 'Erotic (Explicit)'
    5 = 'Hardcore'
}

function Get-LadderLabel([int]$level) {
    $clamped = [Math]::Max(0, [Math]::Min(5, $level))
    return $LadderLabels[$clamped]
}

function Parse-IntLevel([string]$name) {
    switch ($name.ToLower()) {
        'intro'          { return 0 }
        'atmospheric'    { return 0 }
        'emotional'      { return 1 }
        'suggestivepg12' { return 2 }
        'suggestive'     { return 2 }
        'sensualmature'  { return 3 }
        'sensual'        { return 3 }
        'explicit'       { return 4 }
        'erotic'         { return 4 }
        'hardcore'       { return 5 }
        default          { return 0 }
    }
}

function Run-Query([string]$label, [string]$sqlFile) {
    Write-Host ""
    Write-Host "=== $label ===" -ForegroundColor Cyan
    dotnet run --project $dbq -- sql $sqlFile $SessionId
}

function Show-IntensityLadder {
    Write-Host ""
    Write-Host "=== INTENSITY PROFILE LADDER ===" -ForegroundColor Cyan

    $raw = dotnet run --project $dbq -- sql "$q/intensity-profile.sql" $SessionId 2>&1
    if (-not $raw) {
        Write-Host "(no intensity profile data)"
        return
    }

    # Pipe-delimited columns from dbquery: each row uses ' | ' as separator
    # Use -split with regex to avoid PS5 .Split() char-per-char behaviour
    $cols = ($raw -join "`n") -split ' \| '
    if ($cols.Count -lt 8) {
        Write-Host $raw
        return
    }

    $profileName   = $cols[1].Trim()
    $baseStr       = $cols[2].Trim()
    $buildUpOff    = [int]($cols[3].Trim())
    $committedOff  = [int]($cols[4].Trim())
    $approachOff   = [int]($cols[5].Trim())
    $climaxOff     = [int]($cols[6].Trim())
    $resetOff      = [int]($cols[7].Trim())
    $sceneDir      = if ($cols.Count -gt 8) { $cols[8].Trim() } else { '' }
    $pinned        = if ($cols.Count -gt 9) { $cols[9].Trim() } else { '0' }
    $ceiling       = if ($cols.Count -gt 10) { $cols[10].Trim() } else { '' }
    $floor         = if ($cols.Count -gt 11) { $cols[11].Trim() } else { '' }
    $lastLabel     = if ($cols.Count -gt 12) { $cols[12].Trim() } else { '' }
    $lastReason    = if ($cols.Count -gt 13) { $cols[13].Trim() } else { '' }
    $transitions   = if ($cols.Count -gt 17) { $cols[17].Trim() } else { '[]' }

    $baseLevel = Parse-IntLevel $baseStr

    Write-Host "  Profile   : $profileName (base: $baseStr = $(Get-LadderLabel $baseLevel))"
    Write-Host "  Pinned    : $($pinned -eq '1')"
    if ($ceiling) { Write-Host "  Ceiling   : $ceiling" }
    if ($floor)   { Write-Host "  Floor     : $floor" }
    if ($lastLabel)  { Write-Host "  Last resolved : $lastLabel -- $lastReason" }
    if ($sceneDir)   { Write-Host "  Scene directive: $sceneDir" }
    Write-Host ""
    Write-Host "  Phase Ladder (base + offset -> effective intensity):"
    Write-Host "    BuildUp    : base($baseLevel) + $buildUpOff   = $(Get-LadderLabel ($baseLevel + $buildUpOff))"
    Write-Host "    Committed  : base($baseLevel) + $committedOff  = $(Get-LadderLabel ($baseLevel + $committedOff))"
    Write-Host "    Approaching: base($baseLevel) + $approachOff   = $(Get-LadderLabel ($baseLevel + $approachOff))"
    Write-Host "    Climax     : base($baseLevel) + $climaxOff     = $(Get-LadderLabel ($baseLevel + $climaxOff))"
    Write-Host "    Reset      : base($baseLevel) + $resetOff      = $(Get-LadderLabel ($baseLevel + $resetOff))"

    if ($transitions -and $transitions -ne '[]') {
        Write-Host ""
        Write-Host "  Adaptive Profile Transitions: $transitions"
    }
}

# ── Main output ──────────────────────────────────────────────────────────────

Run-Query "SESSION OVERVIEW"              "$q/session-overview.sql"
Run-Query "TURN & INTERACTION COUNTS"     "$q/turn-interaction-counts.sql"
Run-Query "TURNS"                         "$q/turns.sql"
Run-Query "PHASE SUMMARY"                 "$q/phase-summary.sql"
Run-Query "PHASE TRANSITIONS"             "$q/phase-transitions.sql"
Run-Query "PHASE TRANSITION BLOCKERS"     "$q/phase-blockers.sql"
Show-IntensityLadder
Run-Query "THEME OBSERVER STATE"          "$q/theme-observer.sql"
Run-Query "THEME SCORES"                  "$q/theme-scores.sql"
Run-Query "THEME TRACKER META"            "$q/theme-tracker.sql"
Run-Query "CANDIDATE EVALUATIONS"         "$q/evals.sql"
Run-Query "GATE EVALUATIONS"              "$q/gates.sql"
Run-Query "CHARACTER SNAPSHOTS"           "$q/char-snapshots.sql"
Run-Query "STAT DELTA BREAKDOWNS"         "$q/stat-deltas.sql"
Run-Query "SEMANTIC ANALYSIS STATE"       "$q/semantic-analysis.sql"
Run-Query "SEMANTIC EVIDENCE APPLIED"     "$q/semantic-applied.sql"
Run-Query "DEBUG EVENT TIMELINE"          "$q/debug-events.sql"
Run-Query "PROMPT HARD CONSTRAINTS"       "$q/prompt-hard-constraints.sql"
