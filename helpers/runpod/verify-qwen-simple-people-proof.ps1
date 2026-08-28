param(
    [string]$ProofRoot = (Join-Path $PSScriptRoot "..\..\specs\image-generator-tests\qwen")
)

$ErrorActionPreference = "Stop"
$manifestPath = Join-Path $ProofRoot "manifest.json"
if (-not (Test-Path $manifestPath -PathType Leaf)) { throw "Proof manifest was not found: $manifestPath" }

$manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
$entries = @($manifest.base) + @($manifest.edits) + @($manifest.exploratory.images) + @($manifest.adultFellatio.stages | Where-Object { $_.path })
foreach ($entry in $entries) {
    $path = Join-Path $ProofRoot $entry.path
    if (-not (Test-Path $path -PathType Leaf)) { throw "Missing proof image: $($entry.path)" }
    $bytes = (Get-Item $path).Length
    if ($bytes -ne [long]$entry.bytes) { throw "Byte count mismatch for $($entry.path): expected $($entry.bytes), got $bytes" }
    $hash = (Get-FileHash -Algorithm SHA256 -Path $path).Hash.ToLowerInvariant()
    if ($hash -ne $entry.sha256.ToLowerInvariant()) { throw "SHA-256 mismatch for $($entry.path): expected $($entry.sha256), got $hash" }
    Write-Host "Verified $($entry.path)"
}

if ($manifest.acceptance.passed -ne 6 -or $manifest.acceptance.total -ne 6) {
    throw "The packaged proof manifest does not contain the expected 6/6 covered-scenario result."
}
if ($manifest.coverage.adultContentEditing -ne 'tested-exploratory-unscored') {
    throw "The packaged proof manifest has an unexpected adult-content coverage value."
}

Write-Host "Qwen proof package verified: 6/6 covered non-explicit edits."
Write-Host "Adult-content editing (exploratory, unscored) images were integrity-checked but are NOT scored capability evidence."
Write-Host "Four exploratory interaction images were integrity-checked but remain unscored and not replayable."