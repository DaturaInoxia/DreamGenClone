<#
.SYNOPSIS
  Downloads the Qwen-Image-Edit Rapid-AIO NSFW v23 merged checkpoint onto the local ComfyUI host.

.DESCRIPTION
  Run ON the ComfyUI host (WOOD-GAME-MAIN). Idempotent + resumable:
    * a partially-downloaded file is resumed with `curl -C -`
    * a complete file (exact expected byte count) is accepted without re-downloading
  The checkpoint is the same renderer the RunPod serverless endpoint uses
  (`img-qwen-edit-serverless`, model `Qwen-Rapid-AIO-NSFW-v23.safetensors`), so the local
  editor and the serverless editor run identical weights. See
  specs/Planning/B-101-serverless-migration/plan.md (MODEL DECISION 2026-08-28).

  B-101 MODEL DECISION settings: ~4-8 steps, CFG 1, euler_ancestral/beta. It is a Lightning
  accelerator merge - do NOT run it at 40 steps / CFG 4.

.NOTES
  Writes only to the ComfyUI models folder on the host. No source-control artifacts.
#>
[CmdletBinding()]
param(
    [string]$TargetDirectory = 'D:\ComfyUI\models\checkpoints',
    [string]$FileName = 'Qwen-Rapid-AIO-NSFW-v23.safetensors',
    [long]$ExpectedBytes = 28431840023,
    [string]$Url = 'https://huggingface.co/Phr00t/Qwen-Image-Edit-Rapid-AIO/resolve/main/v23/Qwen-Rapid-AIO-NSFW-v23.safetensors'
)

$ErrorActionPreference = 'Stop'
$target = Join-Path $TargetDirectory $FileName
$logPath = Join-Path $TargetDirectory ($FileName -replace '\.safetensors$', '.download.log')

$curlCommand = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $curlCommand) { throw 'curl.exe is required for a resumable download and was not found on PATH.' }
$curl = $curlCommand.Source
if (-not (Test-Path $TargetDirectory)) { throw "Target directory '$TargetDirectory' does not exist." }

if (Test-Path $target) {
    $existing = (Get-Item $target).Length
    if ($existing -eq $ExpectedBytes) {
        "Already complete: $target ($existing bytes)"
        exit 0
    }
    "Resuming: $target has $existing of $ExpectedBytes bytes"
} else {
    "Starting download: $Url"
}

"Log: $logPath"
& $curl --location --continue-at - --retry 20 --retry-delay 10 --fail --show-error --silent --output $target $Url 2>&1 |
    Tee-Object -FilePath $logPath -Append

$final = (Get-Item $target).Length
if ($final -ne $ExpectedBytes) {
    throw "Download incomplete: $final of $ExpectedBytes bytes."
}

"DOWNLOAD_COMPLETE $final bytes"
"SHA256: $((Get-FileHash $target -Algorithm SHA256).Hash)"
