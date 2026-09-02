# download-hf.ps1 - resumable download of HuggingFace files to a local staging dir.
#
# Usage:
#   powershell -File helpers/runpod/serverless/download-hf.ps1 `
#     -ManifestPath artifacts/tmp/qwen-vl/manifest.txt `
#     -OutDir artifacts/tmp/qwen-vl `
#     -BaseUrl "https://huggingface.co/huihui-ai/Qwen2.5-VL-7B-Instruct-abliterated/resolve/main"
#
# Manifest format: one relative path per line (comments with # allowed).
# Uses curl.exe -L -C - --retry-all-errors for resumable downloads. Prints each file as it
# completes and a final "DOWNLOAD COMPLETE" marker.
param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [Parameter(Mandatory = $true)][string]$BaseUrl
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$lines = Get-Content $ManifestPath | Where-Object { $_ -and -not $_.TrimStart().StartsWith("#") }
foreach ($rel in $lines) {
    $rel = $rel.Trim()
    if (-not $rel) { continue }
    $target = Join-Path $OutDir $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    $url = "$BaseUrl/$rel"
    Write-Host "downloading $rel -> $target"
    & curl.exe -sSL -C - --retry 10 --retry-delay 5 --retry-all-errors -o $target $url
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED $rel (curl exit $LASTEXITCODE) - check logs; file may be partial (resumable)."
        exit 1
    }
    Write-Host "  done $rel"
}
Write-Host "DOWNLOAD COMPLETE ($($lines.Count) files)"
