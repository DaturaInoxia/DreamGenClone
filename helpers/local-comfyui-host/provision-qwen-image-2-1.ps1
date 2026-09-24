<#
.SYNOPSIS
  Provision Qwen-Image-2.1 (unified 7B text-to-image + image-editing model) on the LOCAL ComfyUI host.

.DESCRIPTION
  Run ON the ComfyUI host (WOOD-GAME-MAIN, RTX 5080 16 GB, D:\ComfyUI). Idempotent; two independent phases.

  Phase 1 - runtime build.
    Qwen-Image-2.1 needs the `TextEncodeQwenImage21` node. Verified 2026-09-22: the installed build
    (0.34.0, commit f5ed117b, 2026-09-07) does NOT have it, and neither does any released tag -
    v0.35.2 (2026-09-14) predates the model. ComfyUI shipped Qwen-Image-2.1 support Day-0
    (2026-09-20, model release); the first *release* branch carrying it is `release/v0.37`.
    This phase pins that exact commit. It only acts when the checkout is not already at the pin:
    stop the `ComfyUI 8188` scheduled task, wait for the port to free, check out the pin, refresh
    the venv requirements, start the task again, then verify the node is live over HTTP.

  Phase 2 - weights.
    ComfyUI-native `int8_convrot` DiT + text encoder + RGBA VAE from Comfy-Org/Qwen-Image-2.1.
    Every artifact is verified by exact byte count AND SHA-256. A partial download lives in a
    `.part` file, is resumed with `curl -C -`, and is never trusted: a wrong-size or wrong-hash
    artifact is refused (there is no "assume it is fine" path).

  Why int8_convrot and not bf16: the host is a 16 GB card. bf16 DiT (13.25 GB) + bf16 text encoder
  (16.33 GB) can never be co-resident; the int8 pair (6.76 GB + 8.71 GB) runs with ComfyUI's normal
  staging - the encoder encodes once, then frees for the DiT. The 8B text encoder is Qwen3-VL,
  NOT the Qwen2.5-VL used by Qwen-Image-Edit-2511, so no 2511/2512 LoRA or merge is compatible.

.PARAMETER ComfyUiDir
  ComfyUI install root on the host. Default D:\ComfyUI.

.PARAMETER PinnedCommit
  ComfyUI commit to run. Default = release/v0.37 @ 3f767e7f67bc587e88d6de6668eb424f725d4649 (2026-09-22).

.PARAMETER SkipUpdate
  Skip phase 1 (runtime build). Use when the build is already known good.

.PARAMETER SkipWeights
  Skip phase 2 (weight download).

.PARAMETER ComfyUiUrl
  ComfyUI base URL used for the queue guard and the post-phase verification. Default http://127.0.0.1:8188.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\provision-qwen-image-2-1.ps1 -SkipWeights
  Pin the runtime only (fast; no 17 GB download).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\provision-qwen-image-2-1.ps1 -SkipUpdate
  Download/verify the weights only.

.NOTES
  Writes only into the ComfyUI install on the host. No source-control artifacts.
  Total weight payload: 17,285,881,112 bytes (~16.1 GiB).
#>
[CmdletBinding()]
param(
    [string]$ComfyUiDir = 'D:\ComfyUI',
    [string]$PinnedCommit = '3f767e7f67bc587e88d6de6668eb424f725d4649',
    [switch]$SkipUpdate,
    [switch]$SkipWeights,
    [string]$ComfyUiUrl = 'http://127.0.0.1:8188'
)

$ErrorActionPreference = 'Stop'

function Write-Step($msg)  { Write-Host ""; Write-Host "== $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "   OK   : $msg" -ForegroundColor Green }
function Write-Skip2($msg) { Write-Host "   SKIP : $msg" -ForegroundColor DarkGray }
function Write-Warn2($msg) { Write-Host "   WARN : $msg" -ForegroundColor Yellow }

# Sourced from https://huggingface.co/api/models/Comfy-Org/Qwen-Image-2.1?blobs=true (2026-09-22).
# The companion prompt-rewriter encoders (qwen3.5_9b_*_pe_t2i/i2i.int8_convrot, 9.47 GB each) are
# deliberately NOT installed: the app compiles its own prompts, and the repo forbids calling a
# prompt-polishing service at runtime.
$artifacts = @(
    @{
        Name   = 'qwen_image_2.1_int8_convrot.safetensors'
        Folder = 'diffusion_models'
        Url    = 'https://huggingface.co/Comfy-Org/Qwen-Image-2.1/resolve/main/diffusion_models/qwen_image_2.1_int8_convrot.safetensors'
        Sha256 = 'CB74113CB03FAECD79611B01FD7FD642F0AA60D6F0B95086ABEE214D75EAA57D'
        Size   = 7256783064
    },
    @{
        Name   = 'qwen3vl_8b_int8_convrot.safetensors'
        Folder = 'text_encoders'
        Url    = 'https://huggingface.co/Comfy-Org/Qwen-Image-2.1/resolve/main/text_encoders/qwen3vl_8b_int8_convrot.safetensors'
        Sha256 = '8BFD0F6E12ABF2D2D697ECC888E5E90B0D6741D6708F05799F53AFA560452E8F'
        Size   = 9350798360
    },
    @{
        Name   = 'qwen_image_2.1_vae_bf16.safetensors'
        Folder = 'vae'
        Url    = 'https://huggingface.co/Comfy-Org/Qwen-Image-2.1/resolve/main/vae/qwen_image_2.1_vae_bf16.safetensors'
        Sha256 = 'BB21F7473051E1AC368515DD3F2E15CD44D7A11748EE8823E1DDCA3E4876B7C9'
        Size   = 675509688
    }
)

function Get-QueueState {
    try {
        $q = Invoke-RestMethod -Uri "$ComfyUiUrl/queue" -TimeoutSec 20
        return @{ Running = @($q.queue_running).Count; Pending = @($q.queue_pending).Count }
    }
    catch {
        return $null   # ComfyUI not up: nothing can be queued
    }
}

function Wait-ForPort([bool]$Listening, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $now = Test-NetConnection -ComputerName '127.0.0.1' -Port 8188 -InformationLevel Quiet -WarningAction SilentlyContinue
        if ($now -eq $Listening) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Wait-ForApiReady([int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            Invoke-RestMethod -Uri "$ComfyUiUrl/system_stats" -TimeoutSec 10 | Out-Null
            return $true
        }
        catch { Start-Sleep -Seconds 3 }
    }
    return $false
}

function Get-LiveVersion {
    try { return (Invoke-RestMethod -Uri "$ComfyUiUrl/system_stats" -TimeoutSec 20).system.comfyui_version }
    catch { return $null }
}

# ---------------------------------------------------------------- preflight
Write-Step "Preflight"

if (-not (Test-Path $ComfyUiDir)) { throw "ComfyUI directory not found: $ComfyUiDir" }
Write-Ok "ComfyUI dir: $ComfyUiDir"

$python = Join-Path $ComfyUiDir '.venv\Scripts\python.exe'
if (-not (Test-Path $python)) { throw "ComfyUI venv python not found: $python" }
Write-Ok "venv python: $python"

if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { throw "git.exe not found on PATH." }
Write-Ok "git: $((Get-Command git.exe).Source)"

if (-not $SkipWeights) {
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if (-not $curl) { throw "curl.exe is required for resumable downloads and was not found on PATH." }
    Write-Ok "curl: $($curl.Source)"

    $drive = (Get-Item $ComfyUiDir).PSDrive.Name
    $free = (Get-PSDrive -Name $drive).Free
    # NOTE: Measure-Object -Property does not read hashtable keys; enumerate explicitly.
    $needed = ($artifacts | ForEach-Object { [int64]$_.Size } | Measure-Object -Sum).Sum
    if ($free -lt ($needed + 2GB)) {
        throw "Not enough free space on ${drive}: need ~$([math]::Round($needed/1GB,2)) GB + 2 GB headroom, have $([math]::Round($free/1GB,2)) GB."
    }
    Write-Ok "free space on ${drive}: $([math]::Round($free/1GB,2)) GB (need ~$([math]::Round($needed/1GB,2)) GB)"
}

# -------------------------------------------------------- phase 1: runtime
if (-not $SkipUpdate) {
    Write-Step "Phase 1 - ComfyUI runtime build"

    $head = (& git.exe -C $ComfyUiDir rev-parse HEAD).Trim()
    Write-Host "   current commit: $head"
    Write-Host "   pinned commit : $PinnedCommit"

    if ($head -eq $PinnedCommit) {
        Write-Skip2 "already at the pinned commit (release/v0.37, ships Qwen-Image-2.1 nodes)"
    }
    else {
        $queue = Get-QueueState
        if ($queue -and (($queue.Running + $queue.Pending) -gt 0)) {
            throw "ComfyUI has work queued (running=$($queue.Running) pending=$($queue.Pending)). Never restart it mid-job - wait for the queue to drain and re-run."
        }

        $task = Get-ScheduledTask -TaskName 'ComfyUI 8188' -ErrorAction SilentlyContinue
        if (-not $task) { throw "Scheduled task 'ComfyUI 8188' not found; ComfyUI is not started the supported way." }

        Write-Host "   stopping scheduled task 'ComfyUI 8188'"
        Stop-ScheduledTask -TaskName 'ComfyUI 8188'
        if (-not (Wait-ForPort -Listening $false -TimeoutSeconds 90)) {
            throw "Port 8188 is still listening 90 s after stopping the task; refusing to update a live install."
        }
        Write-Ok "port 8188 released"

        Write-Host "   checking out $PinnedCommit (detached)"
        & git.exe -C $ComfyUiDir -c advice.detachedHead=false checkout --detach $PinnedCommit
        if ($LASTEXITCODE -ne 0) { throw "git checkout --detach $PinnedCommit failed (exit $LASTEXITCODE). Resolve the working-tree conflict and re-run; do not force it." }

        $newHead = (& git.exe -C $ComfyUiDir rev-parse HEAD).Trim()
        if ($newHead -ne $PinnedCommit) { throw "Checkout reported success but HEAD is $newHead, expected $PinnedCommit." }
        Write-Ok "now at $newHead"

        Write-Host "   refreshing venv requirements (frontend/template packages move with the build)"
        & $python -m pip install -r (Join-Path $ComfyUiDir 'requirements.txt') --upgrade-strategy only-if-needed
        if ($LASTEXITCODE -ne 0) { throw "pip install -r requirements.txt failed (exit $LASTEXITCODE)." }
        Write-Ok "requirements installed"

        Write-Host "   starting scheduled task 'ComfyUI 8188'"
        Start-ScheduledTask -TaskName 'ComfyUI 8188'
    }

    if (-not (Wait-ForApiReady -TimeoutSeconds 240)) { throw "ComfyUI did not answer $ComfyUiUrl/system_stats within 240 s of the update." }
    Write-Ok "ComfyUI API ready (reported version $((Get-LiveVersion)))"

    $oi = Invoke-RestMethod -Uri "$ComfyUiUrl/object_info" -TimeoutSec 120
    $names = @($oi.PSObject.Properties.Name)
    if ($names -contains 'TextEncodeQwenImage21') {
        Write-Ok "TextEncodeQwenImage21 present (Qwen-Image-2.1 supported)"
    }
    else {
        throw "TextEncodeQwenImage21 is STILL absent after the update ($($names.Count) node classes). The build does not support Qwen-Image-2.1 - do not proceed."
    }

    # Blast-radius guards: the same host serves the SDXL/BigLust, FLUX/XLabs and Qwen-Edit-2511 paths.
    Write-Host "   blast-radius check (other families this host serves):"
    foreach ($cls in @('TextEncodeQwenImageEditPlus', 'CheckpointLoaderSimple', 'KSampler', 'LoadFluxControlNet', 'ApplyFluxControlNet', 'XlabsSampler')) {
        if ($names -contains $cls) { Write-Host "     present : $cls" } else { Write-Warn2 "MISSING : $cls" }
    }
}

# --------------------------------------------------------- phase 2: weights
if (-not $SkipWeights) {
    Write-Step "Phase 2 - Qwen-Image-2.1 weights (int8_convrot pair + RGBA VAE)"

    $strays = Get-Process -Name curl -ErrorAction SilentlyContinue
    if ($strays) {
        throw "A curl process is already running ($(($strays | Measure-Object).Count) process(es)). Concurrent writers corrupt a .part file; wait for it to finish."
    }

    foreach ($a in $artifacts) {
        Write-Step "$($a.Name)  ->  models\$($a.Folder)"

        $dir = Join-Path $ComfyUiDir "models\$($a.Folder)"
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

        $target = Join-Path $dir $a.Name
        $part = "$target.part"

        if (Test-Path $target) {
            $existing = Get-Item $target
            $hash = (Get-FileHash -Path $target -Algorithm SHA256).Hash
            if ($existing.Length -eq $a.Size -and $hash -eq $a.Sha256) {
                Write-Skip2 "already installed and hash-verified ($([math]::Round($existing.Length/1GB,2)) GB)"
                continue
            }
            Write-Warn2 "present but does not match the pinned artifact (size=$($existing.Length) sha256=$hash) - replacing"
            Remove-Item $target -Force
        }

        if (Test-Path $part) {
            Write-Warn2 "resuming partial download ($([math]::Round((Get-Item $part).Length/1MB,1)) MB already on disk)"
        }

        Write-Host "   downloading $($a.Url)"
        & $curl.Source -L -C - --fail --retry 5 --retry-delay 5 --no-progress-meter -o $part $a.Url
        if ($LASTEXITCODE -ne 0) { throw "curl failed for $($a.Name) (exit $LASTEXITCODE). The .part file is kept; re-running resumes it." }
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

    Write-Step "Verify the loaders see the new weights"
    try {
        $oi = Invoke-RestMethod -Uri "$ComfyUiUrl/object_info" -TimeoutSec 120
        $unets = @($oi.UNETLoader.input.required.unet_name[0])
        $clips = @($oi.CLIPLoader.input.required.clip_name[0])
        $vaes = @($oi.VAELoader.input.required.vae_name[0])
        $checks = @(
            @{ List = $unets; Name = 'qwen_image_2.1_int8_convrot.safetensors'; Loader = 'UNETLoader' },
            @{ List = $clips; Name = 'qwen3vl_8b_int8_convrot.safetensors'; Loader = 'CLIPLoader' },
            @{ List = $vaes; Name = 'qwen_image_2.1_vae_bf16.safetensors'; Loader = 'VAELoader' }
        )
        foreach ($c in $checks) {
            if ($c.List -contains $c.Name) { Write-Ok "$($c.Loader): $($c.Name)" }
            else { Write-Warn2 "$($c.Loader) does not list '$($c.Name)' yet (ComfyUI rescans on folder mtime change; a restart also picks it up)" }
        }
    }
    catch {
        Write-Warn2 "could not query $ComfyUiUrl/object_info ($($_.Exception.Message)). Files are installed; verify the loader list manually."
    }
}

Write-Step "Done"
Write-Host "Qwen-Image-2.1 runtime pin: $PinnedCommit"
Write-Host "Graph: UNETLoader(qwen_image_2.1_int8_convrot) + CLIPLoader(qwen3vl_8b_int8_convrot, type=qwen_image) + VAELoader(qwen_image_2.1_vae_bf16)"
Write-Host "Runner: helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 (from the dev box)"
