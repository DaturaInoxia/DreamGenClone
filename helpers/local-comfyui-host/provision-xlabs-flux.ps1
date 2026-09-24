<#
.SYNOPSIS
  Provision the XLabs FLUX runtime on the LOCAL ComfyUI host (WOOD-GAME-MAIN).

.DESCRIPTION
  The dual-base-location proofs use a FLUX.1-dev + XLabs OpenPose ControlNet graph
  (`LoadFluxControlNet` / `ApplyFluxControlNet` / `XlabsSampler`). Those nodes come from
  XLabs-AI/x-flux-comfyui, which is NOT part of ComfyUI core, and the OpenPose controlnet
  checkpoint is not a stock asset. Both were present on the RunPod proof worker image and
  network volume; neither exists on the local host, so the proofs cannot run locally until
  this script has been run ONCE on the host.

  Everything here is idempotent: re-running it is safe and will not re-download or
  re-clone what is already correct.

  Mirrors the pinned revisions documented for the serverless worker:
    - x-flux-comfyui rev 00328556efc9472410d903639dc9e68a8471f7ac
      (helpers/runpod/serverless/flux-structural-worker/Dockerfile, ARG XLABS_FLUX_REV)
    - flux-openpose-controlnet-raulc0399.safetensors
      SHA-256 0401e9fd6a9ae719d5bcaf6825e2fb6a354f84af9341eb7326c11b6027a7a828
      (specs/image-generator-tests/dual-base-location/proofs/flux-openpose-controlnet-manifest.json)

  This script must run ON the ComfyUI host (Windows PowerShell 5.1 compatible).
  It does NOT restart ComfyUI by default; pass -RestartComfyUi to have it try.

.PARAMETER ComfyUiDir
  ComfyUI install root on the host. Default D:\ComfyUI.

.PARAMETER PythonExe
  Python used by the ComfyUI install. Auto-detected from .venv, python_embeded, or PATH.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\provision-xlabs-flux.ps1
#>
[CmdletBinding()]
param(
    [string]$ComfyUiDir = 'D:\ComfyUI',
    [string]$PythonExe,
    [string]$XlabsRev = '00328556efc9472410d903639dc9e68a8471f7ac',
    [string]$ControlNetUrl = 'https://huggingface.co/raulc0399/flux_dev_openpose_controlnet/resolve/main/model.safetensors',
    [string]$ControlNetSha256 = '0401E9FD6A9AE719D5BCAF6825E2FB6A354F84AF9341EB7326C11B6027A7A828',
    [string]$TaskName,
    [switch]$SkipDeps,
    [switch]$RestartComfyUi
)

$ErrorActionPreference = 'Stop'

function Write-Step($msg)  { Write-Host ""; Write-Host "== $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "   OK   : $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "   WARN : $msg" -ForegroundColor Yellow }

# ---------------------------------------------------------------- preflight
Write-Step "Preflight"

if (-not (Test-Path $ComfyUiDir)) { throw "ComfyUI directory not found: $ComfyUiDir" }
Write-Ok "ComfyUI dir: $ComfyUiDir"

if (-not $PythonExe) {
    $candidates = @(
        (Join-Path $ComfyUiDir '.venv\Scripts\python.exe'),
        (Join-Path $ComfyUiDir 'python_embeded\python.exe'),
        (Join-Path $ComfyUiDir 'venv\Scripts\python.exe')
    )
    foreach ($c in $candidates) { if (Test-Path $c) { $PythonExe = $c; break } }
    if (-not $PythonExe) {
        $cmd = Get-Command python -ErrorAction SilentlyContinue
        if ($cmd) { $PythonExe = $cmd.Source }
    }
}
if (-not $PythonExe -or -not (Test-Path $PythonExe)) {
    throw "Could not locate the ComfyUI Python. Pass -PythonExe explicitly (e.g. D:\ComfyUI\.venv\Scripts\python.exe)."
}
Write-Ok "python: $PythonExe"

$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) { throw "git is required to install the XLabs custom node." }
Write-Ok "git: $($git.Source)"

$customNodesDir = Join-Path $ComfyUiDir 'custom_nodes'
if (-not (Test-Path $customNodesDir)) { New-Item -ItemType Directory -Path $customNodesDir -Force | Out-Null }
$xlabsDir = Join-Path $customNodesDir 'x-flux-comfyui'

$xlabsControlNets = Join-Path $ComfyUiDir 'models\xlabs\controlnets'
$xlabsFlux        = Join-Path $ComfyUiDir 'models\xlabs\flux'

# ------------------------------------------------------- check comfyui version
Write-Step "ComfyUI version"
$comfyVersionFile = Join-Path $ComfyUiDir 'comfyui_version.py'
if (Test-Path $comfyVersionFile) { Get-Content $comfyVersionFile | Select-Object -First 1 | ForEach-Object { Write-Ok $_ } }

# ------------------------------------------------------------- custom node
Write-Step "Install x-flux-comfyui @ $XlabsRev"

if (Test-Path (Join-Path $xlabsDir 'nodes.py')) {
    $current = (& $git.Source -C $xlabsDir rev-parse HEAD 2>$null)
    if ($current -eq $XlabsRev) {
        Write-Ok "already at pinned revision $XlabsRev"
    } else {
        Write-Warn2 "present at $current - checking out pinned revision"
        & $git.Source -C $xlabsDir fetch --depth 1 origin $XlabsRev
        if ($LASTEXITCODE -ne 0) { throw "git fetch failed for $XlabsRev" }
        & $git.Source -C $xlabsDir checkout --force FETCH_HEAD
        if ($LASTEXITCODE -ne 0) { throw "git checkout failed for $XlabsRev" }
        Write-Ok "checked out $XlabsRev"
    }
} else {
    if (Test-Path $xlabsDir) { Remove-Item -Recurse -Force $xlabsDir }
    New-Item -ItemType Directory -Path $xlabsDir -Force | Out-Null
    & $git.Source -C $xlabsDir init --quiet
    & $git.Source -C $xlabsDir remote add origin https://github.com/XLabs-AI/x-flux-comfyui.git
    & $git.Source -C $xlabsDir fetch --depth 1 origin $XlabsRev
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed for $XlabsRev" }
    & $git.Source -C $xlabsDir checkout --force FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { throw "git checkout failed for $XlabsRev" }
    Write-Ok "cloned + checked out $XlabsRev"
}

if (-not (Test-Path (Join-Path $xlabsDir 'nodes.py'))) { throw "x-flux-comfyui nodes.py missing after checkout" }
if (-not (Test-Path (Join-Path $xlabsDir 'xflux\src\flux\util.py'))) { throw "x-flux-comfyui xflux sources missing after checkout" }
Write-Ok "nodes.py + xflux sources present"

# ------------------------------------------------------------------ deps
$logDir = Join-Path $ComfyUiDir 'xlabs-install-log'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
$freezeBefore = Join-Path $logDir ("pip-freeze-before-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

Write-Step "Python dependencies"
& $PythonExe -m pip freeze | Out-File -FilePath $freezeBefore -Encoding ASCII
Write-Ok "rollback snapshot written: $freezeBefore"

if ($SkipDeps) {
    Write-Warn2 "-SkipDeps set: requirements NOT installed. LoadFluxControlNet will fail on import if deps are missing."
} else {
    $req = Join-Path $xlabsDir 'requirements.txt'
    if (-not (Test-Path $req)) { throw "requirements.txt missing at $req" }
    $reqList = @(Get-Content $req | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { $_.Trim() })
    Write-Host ("   installing: " + ($reqList -join ', '))
    & $PythonExe -m pip install --no-cache-dir -r $req
    if ($LASTEXITCODE -ne 0) { throw "pip install failed (exit $LASTEXITCODE). Rollback snapshot: $freezeBefore" }
    Write-Ok "requirements installed"
}

# ------------------------------------------------------------- controlnet
Write-Step "FLUX OpenPose controlnet"

if (-not (Test-Path $xlabsControlNets)) { New-Item -ItemType Directory -Path $xlabsControlNets -Force | Out-Null }
if (-not (Test-Path $xlabsFlux))        { New-Item -ItemType Directory -Path $xlabsFlux -Force | Out-Null }

$controlNetPath = Join-Path $xlabsControlNets 'flux-openpose-controlnet-raulc0399.safetensors'
$needDownload = $true
if (Test-Path $controlNetPath) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $controlNetPath).Hash
    if ($hash -eq $ControlNetSha256) {
        Write-Ok "already present + hash verified ($hash)"
        $needDownload = $false
    } else {
        Write-Warn2 "present but hash mismatch ($hash) - re-downloading"
    }
}

if ($needDownload) {
    Write-Host "   downloading ~2.8 GB from HF (resumable; re-run this script if it drops)"
    & curl.exe -L --fail -C - -o $controlNetPath $ControlNetUrl
    if ($LASTEXITCODE -ne 0) {
        Write-Warn2 "curl exited $LASTEXITCODE - verifying whatever landed on disk"
    }
    if (-not (Test-Path $controlNetPath)) { throw "controlnet download did not produce $controlNetPath" }
    $hash = (Get-FileHash -Algorithm SHA256 -Path $controlNetPath).Hash
    if ($hash -ne $ControlNetSha256) {
        throw "controlnet SHA-256 mismatch.`n  expected: $ControlNetSha256`n  actual  : $hash`nThe file is incomplete or wrong; re-run this script to resume."
    }
    Write-Ok "downloaded + hash verified ($hash)"
}

# ------------------------------------------------- local FLUX checkpoints
Write-Step "Local FLUX base assets (informational)"
$dm = Join-Path $ComfyUiDir 'models\diffusion_models'
foreach ($f in 'flux1-dev-fp8.safetensors') {
    $p = Join-Path $dm $f
    if (Test-Path $p) { Write-Ok "diffusion_models\$f present" } else { Write-Warn2 "missing: $p" }
}
foreach ($f in @('t5xxl_fp8_e4m3fn.safetensors', 'clip_l.safetensors')) {
    $p = Join-Path (Join-Path $ComfyUiDir 'models\text_encoders') $f
    if (Test-Path $p) { Write-Ok "text_encoders\$f present" } else { Write-Warn2 "missing: $p" }
}
$p = Join-Path (Join-Path $ComfyUiDir 'models\vae') 'ae.safetensors'
if (Test-Path $p) { Write-Ok "vae\ae.safetensors present" } else { Write-Warn2 "missing: $p" }

# ---------------------------------------------------------------- restart
# On WOOD-GAME-MAIN ComfyUI is launched by the scheduled task "ComfyUI 8188", so the reliable
# restart is Stop/Start of THAT task (it also survives the SSH session closing - a bare
# Start-Process child is killed with the session). Falls back to process kill + relaunch.
Write-Step "Restart ComfyUI"
if ($RestartComfyUi) {
    $task = $null
    if ($TaskName) {
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        if (-not $task) { Write-Warn2 "scheduled task '$TaskName' not found" }
    } else {
        $task = Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -match 'comfy' } | Select-Object -First 1
    }

    if ($task) {
        Write-Host "   restarting scheduled task: $($task.TaskName)"
        Stop-ScheduledTask -TaskName $task.TaskName -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 5
        Start-ScheduledTask -TaskName $task.TaskName
        Start-Sleep -Seconds 10
        $state = (Get-ScheduledTask -TaskName $task.TaskName).State
        Write-Ok "task '$($task.TaskName)' restarted (state: $state)"
        Write-Warn2 "ComfyUI needs ~30-90s to load custom nodes; verify /object_info before running proofs."
    } else {
        Write-Warn2 "no ComfyUI scheduled task found - falling back to process restart"
        $procs = Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" |
            Where-Object { $_.CommandLine -and $_.CommandLine -match 'main\.py' }
        if (-not $procs) {
            Write-Warn2 "no running 'python main.py' process found; start ComfyUI normally."
        } else {
            foreach ($p in $procs) {
                Write-Host "   stopping PID $($p.ProcessId)"
                Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
            }
            Start-Sleep -Seconds 3
            $mainPy = Join-Path $ComfyUiDir 'main.py'
            $log = Join-Path $ComfyUiDir 'comfyui-launch.log'
            # Detached from the SSH session via the Task Scheduler shell, so it outlives us.
            $action = "cd /d `"$ComfyUiDir`" && `"$PythonExe`" main.py --listen 0.0.0.0 --port 8188 --disable-auto-launch >> `"$log`" 2>&1"
            $tmpTask = "DG-ComfyUI-Start"
            & schtasks.exe /create /tn $tmpTask /tr "cmd /c $action" /sc once /st 00:00 /f | Out-Null
            & schtasks.exe /run /tn $tmpTask | Out-Null
            Start-Sleep -Seconds 5
            & schtasks.exe /delete /tn $tmpTask /f | Out-Null
            Write-Ok "relaunched via a one-shot scheduled task (log: $log)"
        }
    }
} else {
    Write-Warn2 "not restarted (default). Restart ComfyUI yourself, or re-run with -RestartComfyUi."
    Write-Warn2 "custom nodes are only discovered at ComfyUI startup."
}

Write-Host ""
Write-Host "DONE." -ForegroundColor Green
Write-Host "Verify from the dev box with:" -ForegroundColor Green
Write-Host "  Invoke-RestMethod https://comfy.kenacwood.net/object_info/LoadFluxControlNet   # non-empty = node loaded"
Write-Host "  Invoke-RestMethod https://comfy.kenacwood.net/object_info/ApplyFluxControlNet"
Write-Host "  Invoke-RestMethod https://comfy.kenacwood.net/object_info/XlabsSampler"
Write-Host ""
Write-Host "Rollback snapshot (before deps): $freezeBefore" -ForegroundColor Yellow
