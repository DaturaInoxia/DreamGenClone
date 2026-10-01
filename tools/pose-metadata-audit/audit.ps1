<#
.SYNOPSIS
    Runs the pose metadata audit over the packs that ship, and prints its report.

.DESCRIPTION
    The audit itself lives in DreamGenClone.Tests/RolePlay/PoseMetadataAuditTests.cs, on purpose: the classification
    it audits (stance, facing, camera, rating, prompt) is C#, and a second implementation of it in a script would be
    a second answer to the same question. This script is the documented entry point - it runs that single test and
    prints the report it writes.

    The report covers every pose in every pack under pose-packs/:
      * completeness - rating, stance and prompt present (a gap is a defect)
      * the disagreements between the keypoint measurement and the pack's declaration, named with the numbers
      * the prompt each pose renders with, verbatim

    Exit code follows the test: 0 = every pose is complete and the disagreements match the reviewed set.

    ASCII only on purpose: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so a non-ASCII dash inside a string
    literal turns into mojibake and the script stops parsing.

.PARAMETER Prompts
    Include the per-pose prompt list in the printed output (omitted by default: 579 lines).

.EXAMPLE
    powershell -ExecutionPolicy RemoteSigned -File tools/pose-metadata-audit/audit.ps1

.EXAMPLE
    powershell -ExecutionPolicy RemoteSigned -File tools/pose-metadata-audit/audit.ps1 -Prompts
#>
[CmdletBinding()]
param(
    [switch]$Prompts
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$report = Join-Path $repoRoot 'artifacts\tmp\pose-metadata-audit\report.md'
$outDir = Join-Path $repoRoot 'artifacts\tmp\pose-metadata-audit\bin'

Push-Location $repoRoot
try {
    Write-Host "Auditing pose metadata over '$repoRoot\pose-packs' ..." -ForegroundColor Cyan

    & dotnet test DreamGenClone.Tests\DreamGenClone.Tests.csproj `
        -p:OutDir="$outDir\" `
        --nologo `
        --filter 'FullyQualifiedName~PoseMetadataAuditTests' `
        --results-directory (Join-Path $repoRoot 'artifacts\tmp\testout')

    $exit = $LASTEXITCODE
}
finally {
    Pop-Location
}

if (-not (Test-Path $report)) {
    throw "The audit produced no report at '$report', so the test never reached the writer. Run it directly to see why."
}

$text = Get-Content $report -Raw

# The prompt list is 579 lines and is already in the file; it is only printed on request.
$cut = $text.IndexOf("## Prompts", [StringComparison]::Ordinal)
if (-not $Prompts -and $cut -gt 0) {
    $text = $text.Substring(0, $cut) + "## Prompts`n`n(omitted - pass -Prompts, or read '$report')`n"
}

Write-Host $text

if ($exit -ne 0) {
    throw "Pose metadata audit FAILED (dotnet exit $exit). Read the table and disagreement list above: either a pose " +
        "is missing metadata, or the disagreements no longer match the reviewed set in PoseMetadataAuditTests."
}

Write-Host "Audit passed. Full report: $report" -ForegroundColor Green
