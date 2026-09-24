<#
.SYNOPSIS
  Apply the identity face pass to a scene ONE CHARACTER AT A TIME, chaining each result into the next.

.DESCRIPTION
  Verified 2026-09-21: putting two identity references in ONE Qwen edit does not transfer both faces - the
  model composites the second reference as an extra figure in the frame (a third face). This is the failure
  mode recorded in specs/001-rp-prompt-redesign/debug/043 ("a second identity reference does NOT transfer
  identity - it adds a person"), and it reproduced here on a full-body two-person base.

  Splitting the work into one single-reference edit per character fixes it: each pass sends exactly one
  reference (Picture 2) plus that character's visible locator, and the next pass takes the previous pass's
  output as its source. Dean's applied identity survived Becky's subsequent pass, so no re-application was
  needed - but the order is recorded in the output names if that changes.

  Each pass is delegated to run-local-aio-edit-proof.ps1, which keeps that runner's one-edit-per-invocation
  contract and its app-parity instruction/wiring intact.

.PARAMETER IdentityBindingsPath
  The same bindings JSON the app persists (ordinal, characterName, targetKey, visibleLocator,
  fileRelativePath, sha256). Passes run in ordinal order.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-sequential-identity.ps1 `
    -SourceImage "<run>/06-harmonized-bedroom-a/img-local-bedroom-a_0.png" `
    -IdentityBindingsPath "<run>/07-identity/bindings-A.json" `
    -OutDir "<run>/07-identity/bedroom-a"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceImage,
    [Parameter(Mandatory = $true)][string]$IdentityBindingsPath,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    [string]$IdentityStorageRoot = 'DreamGenClone.Web/data/scene-images',
    [int]$Seed = 73191,
    [int]$TimeoutSeconds = 1500
)

$ErrorActionPreference = 'Stop'

$runner = Join-Path $PSScriptRoot 'run-local-aio-edit-proof.ps1'
if (-not (Test-Path $runner)) { throw "Runner not found: $runner" }
if (-not (Test-Path $SourceImage)) { throw "Source image not found: $SourceImage" }
if (-not (Test-Path $IdentityBindingsPath)) { throw "Bindings not found: $IdentityBindingsPath" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# Note: do NOT wrap ConvertFrom-Json in @() on PowerShell 5.1 - it nests the array as one element.
$parsed = Get-Content -Raw $IdentityBindingsPath | ConvertFrom-Json
$bindings = @()
foreach ($binding in $parsed) { $bindings += $binding }
if ($bindings.Count -eq 0) { throw "Bindings file '$IdentityBindingsPath' contains no bindings." }
$ordered = @($bindings | Sort-Object { [int]$_.ordinal })

$current = (Resolve-Path $SourceImage).Path
$manifest = [ordered]@{
    sourceImage         = $SourceImage
    identityBindingsPath = $IdentityBindingsPath
    comfyUiUrl          = $ComfyUiUrl
    seed                = $Seed
    mode                = 'sequential-single-reference'
    steps               = @()
}

for ($i = 0; $i -lt $ordered.Count; $i++) {
    $binding = $ordered[$i]
    $stepName = "step$($i + 1)-$($binding.characterName)"
    $stepDir = Join-Path $OutDir $stepName
    Write-Host ""
    Write-Host "=== pass $($i + 1)/$($ordered.Count): $($binding.characterName) ($($binding.faceView)) at '$($binding.visibleLocator)'"

    # One binding per pass - this is the whole point of the script.
    $single = @([ordered]@{
        ordinal          = 1
        characterId      = $binding.characterId
        characterName    = $binding.characterName
        targetKey        = $binding.targetKey
        visibleLocator   = $binding.visibleLocator
        identityPackId   = $binding.identityPackId
        identityPackVersion = $binding.identityPackVersion
        faceView         = $binding.faceView
        fileRelativePath = $binding.fileRelativePath
        sha256           = $binding.sha256
    })
    $singlePath = Join-Path $OutDir "$stepName.bindings.json"
    [IO.File]::WriteAllText($singlePath, (ConvertTo-Json -InputObject $single -Depth 8))

    $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $runner `
        -ComfyUiUrl $ComfyUiUrl `
        -SourceImage $current `
        -IdentityBindingsPath $singlePath `
        -IdentityStorageRoot $IdentityStorageRoot `
        -OutDir $stepDir `
        -Seed $Seed `
        -TimeoutSeconds $TimeoutSeconds 2>&1
    $output | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "identity pass for $($binding.characterName) failed (exit $LASTEXITCODE)" }

    $outLine = $output | Where-Object { $_ -match '^OUTPUT:\s*(.+)$' } | Select-Object -Last 1
    if (-not $outLine) { throw "Identity pass for $($binding.characterName) produced no OUTPUT line." }
    $current = $Matches[1].Trim()
    if (-not (Test-Path $current)) { throw "Identity pass reported '$current' but the file is missing." }

    $manifest.steps += [ordered]@{
        pass             = $i + 1
        character        = $binding.characterName
        faceView         = $binding.faceView
        visibleLocator   = $binding.visibleLocator
        reference        = $binding.fileRelativePath
        output           = $current.Replace((Resolve-Path $OutDir).Path + '\', '')
    }
}

$finalPath = Join-Path $OutDir 'final.png'
Copy-Item $current $finalPath -Force
$manifest.final = 'final.png'
$manifest.finishedAt = (Get-Date).ToString('o')
[IO.File]::WriteAllText((Join-Path $OutDir 'identity-manifest.json'), ($manifest | ConvertTo-Json -Depth 10))

Write-Host ""
Write-Host "FINAL: $finalPath" -ForegroundColor Green
