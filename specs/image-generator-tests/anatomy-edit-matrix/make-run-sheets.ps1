<#
.SYNOPSIS
  Builds the standard labelled contact sheets for a completed anatomy-matrix run.

.DESCRIPTION
  Reads the run's manifest.json (the source of truth for which cells exist and which set each belongs
  to) and emits ONE contact sheet per set, pairing the two configs so each row shows the same cell under
  config A and config B. That is the whole point of the run: the same prompt/seed/base under two
  checkpoints must be readable at a glance rather than hunted through 40 files.

  Phrasing cells carry their OWN per-cell checkpoint/LoRA (they exist to compare phrasing, not configs),
  so they are emitted as cfg-<cell> and are not paired.

  Run from the repo root.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RunDir,
    [string]$ConfigA = 'v23lora08',
    [string]$ConfigB = 'remix',
    [string]$Python = '.venv/Scripts/python.exe',
    [int]$Cols = 2
)

$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$sheet = Join-Path $here 'make-contact-sheet.py'

if (-not (Test-Path $RunDir)) { throw "Run folder not found: $RunDir" }
$manifestPath = Join-Path $RunDir 'manifest.json'
if (-not (Test-Path $manifestPath)) { throw "No manifest.json in $RunDir - did the run finish?" }
if (-not (Test-Path $sheet)) { throw "Missing $sheet" }
if (-not (Test-Path $Python)) { throw "Python not found at $Python (run from the repo root)" }

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

# Preserve manifest order per set so the sheets read the same way every run. A cell name can appear more
# than once per set because a full suite is produced by MULTIPLE passes (one per config), each appending
# its own manifest entries — so names MUST be de-duplicated before they are paired, otherwise every cell
# is emitted twice per config.
$bySet = [ordered]@{}
foreach ($cell in $manifest.cells) {
    if (-not $bySet.Contains($cell.set)) { $bySet[$cell.set] = [System.Collections.Generic.List[string]]::new() }
    if (-not $bySet[$cell.set].Contains($cell.cell)) { $bySet[$cell.set].Add($cell.cell) }
}

$built = @()
$missing = @()

foreach ($set in $bySet.Keys) {
    $files = @()
    $paired = 0
    foreach ($name in $bySet[$set]) {
        $cfg = Join-Path $RunDir ("cfg-$name.png")
        if (Test-Path $cfg) {
            $files += $cfg
            continue
        }

        $a = Join-Path $RunDir ("$ConfigA-$name.png")
        $b = Join-Path $RunDir ("$ConfigB-$name.png")
        if (Test-Path $a) { $files += $a } else { $missing += "$ConfigA-$name.png" }
        if (Test-Path $b) { $files += $b } else { $missing += "$ConfigB-$name.png" }
        $paired++
    }

    if ($files.Count -eq 0) { "SKIP set '$set' (no files found)"; continue }

    $out = Join-Path $RunDir ("contact-sheet-$set.png")
    $caption = "$($manifest.runId) | set=$set | A=$ConfigA  B=$ConfigB | $paired paired cells"
    & $Python $sheet $out $Cols $caption @files
    $built += $out
}

''
"=== SHEETS BUILT ($($built.Count)) ==="
$built | ForEach-Object { "  $_" }

if ($missing.Count -gt 0) {
    ''
    "=== WARNING: $($missing.Count) expected image(s) missing ==="
    $missing | ForEach-Object { "  $_" }
}
