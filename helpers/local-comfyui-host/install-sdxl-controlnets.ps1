<#
.SYNOPSIS
  Install the SDXL depth + canny ControlNet weights on the LOCAL ComfyUI host (B-119 T01).

.DESCRIPTION
  Route C1 (B-119) contracts a layout from a reference image with Depth or Canny ControlNet.
  The node classes are already present on the host, but the SDXL ControlNet *weights* were not:
  `ControlNetLoader` listed only `thibaud-openpose-xl2\OpenPoseXL2.safetensors`, so every C1
  graph failed at load time. This script installs the two missing weights.

  Both artifacts are the exact fp16 files already proven on the RunPod identity worker, so the
  same workflow JSON (`controlnet-depth-sdxl-1.0.safetensors` / `controlnet-canny-sdxl-1.0.safetensors`)
  runs unchanged on the local host.

  Idempotent: a file that is already present AND hash-verified is skipped. A file present with the
  wrong bytes is deleted and re-downloaded (forward-only; a partial download is never trusted).
  Any size or hash mismatch throws — there is no "assume it is fine" path.

  This script must run ON the ComfyUI host (Windows PowerShell 5.1 compatible).
  It does NOT restart ComfyUI by default: ComfyUI invalidates its model-folder listing on folder
  mtime change, so newly dropped weights appear without a restart (pass -RestartComfyUi to force).

.PARAMETER ComfyUiDir
  ComfyUI install root on the host. Default D:\ComfyUI.

.PARAMETER ControlNetDir
  ControlNet weight folder. Defaults to <ComfyUiDir>\models\controlnet.

.PARAMETER SkipDepth / .PARAMETER SkipCanny
  Install only the other artifact.

.PARAMETER RestartComfyUi
  Attempt a safe restart (stop scheduled task, wait for the port to free, start it again).
  Off by default; normally unnecessary.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\install-sdxl-controlnets.ps1
#>
[CmdletBinding()]
param(
    [string]$ComfyUiDir = 'D:\ComfyUI',
    [string]$ControlNetDir,
    [switch]$SkipDepth,
    [switch]$SkipCanny,
    [switch]$RestartComfyUi,
    [string]$ComfyUiUrl = 'http://127.0.0.1:8188'
)

$ErrorActionPreference = 'Stop'

function Write-Step($msg)  { Write-Host ""; Write-Host "== $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "   OK   : $msg" -ForegroundColor Green }
function Write-Skip2($msg) { Write-Host "   SKIP : $msg" -ForegroundColor DarkGray }

$artifacts = @()
if (-not $SkipDepth) {
    $artifacts += @{
        Name   = 'controlnet-depth-sdxl-1.0.safetensors'
        Url    = 'https://huggingface.co/diffusers/controlnet-depth-sdxl-1.0/resolve/main/diffusion_pytorch_model.fp16.safetensors'
        Sha256 = '66A6813E6BD7270ECFE68206A59DDD605A011AE85321188376605C66E0A4F303'
        Size   = 2502139134
    }
}
if (-not $SkipCanny) {
    $artifacts += @{
        Name   = 'controlnet-canny-sdxl-1.0.safetensors'
        Url    = 'https://huggingface.co/diffusers/controlnet-canny-sdxl-1.0/resolve/main/diffusion_pytorch_model.fp16.safetensors'
        Sha256 = 'B2E7D3921058A442CC80430D1EC8847F42599C705E2451C95E77CF4DCF8D6C25'
        Size   = 2502139136
    }
}

# ---------------------------------------------------------------- preflight
Write-Step "Preflight"

if (-not (Test-Path $ComfyUiDir)) { throw "ComfyUI directory not found: $ComfyUiDir" }
Write-Ok "ComfyUI dir: $ComfyUiDir"

if (-not $ControlNetDir) { $ControlNetDir = Join-Path $ComfyUiDir 'models\controlnet' }
if (-not (Test-Path $ControlNetDir)) { New-Item -ItemType Directory -Path $ControlNetDir -Force | Out-Null }
Write-Ok "ControlNet dir: $ControlNetDir"

$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $curl) { throw "curl.exe is required (ships with Windows 10/11). Not found on PATH." }
Write-Ok "curl: $($curl.Source)"

$drive = (Get-Item $ControlNetDir).PSDrive.Name
$free = (Get-PSDrive -Name $drive).Free
# NOTE: Measure-Object -Property does not read hashtable keys; enumerate explicitly.
$needed = ($artifacts | ForEach-Object { [int64]$_.Size } | Measure-Object -Sum).Sum
if ($free -lt ($needed + 1GB)) {
    throw "Not enough free space on ${drive}: for the ControlNet weights (need ~$([math]::Round($needed/1GB,1)) GB + 1 GB headroom, have $([math]::Round($free/1GB,1)) GB)."
}
Write-Ok "free space on ${drive}: $([math]::Round($free/1GB,1)) GB (need ~$([math]::Round($needed/1GB,1)) GB)"

# ------------------------------------------------------------- install loop
foreach ($a in $artifacts) {
    Write-Step "$($a.Name)"

    $target = Join-Path $ControlNetDir $a.Name
    $part   = "$target.part"

    # A stale partial from an interrupted run is resumed by curl (-C -); a stray writer would
    # corrupt it, so make sure nothing else is writing this file.
    $strays = Get-Process -Name curl -ErrorAction SilentlyContinue
    if ($strays) { throw "A curl process is already running ($(($strays | Measure-Object).Count) process(es)). Finish or stop it before running this installer (concurrent writers corrupt the .part file)." }

    if (Test-Path $target) {
        $existing = Get-Item $target
        $hash = (Get-FileHash -Path $target -Algorithm SHA256).Hash
        if ($existing.Length -eq $a.Size -and $hash -eq $a.Sha256) {
            Write-Skip2 "already installed and hash-verified ($([math]::Round($existing.Length/1GB,2)) GB)"
            continue
        }
        Write-Host "   WARN : present but does not match the pinned artifact (size=$($existing.Length) sha256=$hash) - replacing" -ForegroundColor Yellow
        Remove-Item $target -Force
    }

    if (Test-Path $part) {
        $partSize = (Get-Item $part).Length
        Write-Host "   resuming partial download ($([math]::Round($partSize/1MB,1)) MB already on disk)" -ForegroundColor Yellow
    }

    Write-Host "   downloading $($a.Url)"
    & $curl.Source -L -C - --fail --retry 5 --retry-delay 5 --no-progress-meter -o $part $a.Url
    if ($LASTEXITCODE -ne 0) { throw "curl failed for $($a.Name) (exit $LASTEXITCODE). The .part file is kept; re-running this script resumes it." }

    if (-not (Test-Path $part)) { throw "curl reported success but $part does not exist." }

    $downloaded = Get-Item $part
    if ($downloaded.Length -ne $a.Size) {
        throw "Size mismatch for $($a.Name): expected $($a.Size) bytes, got $($downloaded.Length). The .part file is kept; re-running resumes it."
    }

    $hash = (Get-FileHash -Path $part -Algorithm SHA256).Hash
    if ($hash -ne $a.Sha256) {
        throw "SHA-256 mismatch for $($a.Name): expected $($a.Sha256), got $hash. Refusing to install an unverified weight. Delete $part and re-run."
    }
    Write-Ok "verified size + sha256"

    Move-Item -Path $part -Destination $target -Force
    Write-Ok "installed $target"
}

# ------------------------------------------------------------- verification
Write-Step "Verify ControlNetLoader sees the weights"
try {
    $oi = Invoke-RestMethod -Uri "$ComfyUiUrl/object_info" -TimeoutSec 120
    $installed = @($oi.ControlNetLoader.input.required.control_net_name[0])
    foreach ($a in $artifacts) {
        $leaf = [System.IO.Path]::GetFileNameWithoutExtension($a.Name)
        $match = $installed | Where-Object { $_ -like "*$leaf*" -or $_ -like "*$($a.Name)*" }
        if ($match) { Write-Ok "ControlNetLoader: $($match -join ', ')" }
        else { Write-Host "   WARN : '$($a.Name)' not yet listed by ControlNetLoader (ComfyUI may need a folder-rescan or restart)" -ForegroundColor Yellow }
    }
    Write-Host "   currently listed:"
    $installed | ForEach-Object { Write-Host "     $_" }
}
catch {
    Write-Host "   WARN : could not query $ComfyUiUrl/object_info ($($_.Exception.Message)). Files are installed; verify the loader list manually." -ForegroundColor Yellow
}

if ($RestartComfyUi) {
    Write-Step "Restart ComfyUI (requested)"
    $task = Get-ScheduledTask -TaskName 'ComfyUI 8188' -ErrorAction SilentlyContinue
    if (-not $task) { throw "Scheduled task 'ComfyUI 8188' not found; restart ComfyUI manually." }
    Stop-ScheduledTask -TaskName 'ComfyUI 8188'
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        $listening = Test-NetConnection -ComputerName '127.0.0.1' -Port 8188 -InformationLevel Quiet -WarningAction SilentlyContinue
        if (-not $listening) { break }
        Start-Sleep -Seconds 2
    }
    Start-ScheduledTask -TaskName 'ComfyUI 8188'
    Write-Ok "restart requested"
}

Write-Step "Done"
Write-Host "Depth and canny ControlNet weights are installed and verified in $ControlNetDir"
