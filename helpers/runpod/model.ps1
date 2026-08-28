# model.ps1 - download/install and validate a ComfyUI checkpoint into models/checkpoints
param(
    [Parameter(Mandatory=$false)][string]$ModelName,   # e.g. target.safetensors
    [Parameter(Mandatory=$false)][string]$SourceUrl,  # direct download URL
    [Parameter(Mandatory=$false)][string]$ExpectedSha256,
    [Parameter(Mandatory=$false)][switch]$List
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

$checkpointsDir = "ComfyUI/models/checkpoints"   # relative to the repo; adapt if remote path differs

if ($List) {
    if (-not (Test-Path $checkpointsDir)) { throw "Checkpoints dir not present locally: $checkpointsDir" }
    Get-ChildItem $checkpointsDir | Select-Object Name, Length | Format-Table
    exit 0
}

if (-not $ModelName -or -not $SourceUrl) {
    throw "Usage: model.ps1 -ModelName <name.safetensors> -SourceUrl <url> [-ExpectedSha256 <hash>]"
}

if (-not (Test-Path $checkpointsDir)) { New-Item -ItemType Directory -Path $checkpointsDir -Force | Out-Null }
$dest = Join-Path $checkpointsDir $ModelName

# Download with resume; print progress
curl.exe -L -C - -o $dest $SourceUrl
if ($LASTEXITCODE -ne 0) { throw "Download failed (curl exit $LASTEXITCODE)" }

if ($ExpectedSha256) {
    $h = (Get-FileHash -Algorithm SHA256 -Path $dest).Hash.ToLowerInvariant()
    if ($h -ne $ExpectedSha256.ToLowerInvariant()) { throw "Hash mismatch: expected $ExpectedSha256 got $h" }
}

Write-Host "Installed checkpoint: $ModelName -> $dest"
Write-Host "Refresh ComfyUI; in Load Checkpoint select: $ModelName"