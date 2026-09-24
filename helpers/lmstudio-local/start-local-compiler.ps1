<#
.SYNOPSIS
  Starts (or verifies) the LOCAL LM Studio Qwen2.5-VL scene-image compiler on this host.

.DESCRIPTION
  The scene-image edit compiler + validator (functions RolePlaySceneImageEditPromptCompiler and
  RolePlaySceneImageValidator) are served by LM Studio on the APP host (WOODGame, RTX 4060 Ti 8 GB)
  on the host's LAN IP (default http://192.168.0.192:1234) - provider row
  'Local LM Studio (WOODGame 4060 Ti)'.

  The model must live in LM Studio's models folder (D:\LMStudio\Models) and be registered there;
  files placed outside it (e.g. ~\.lmstudio\models) are only transiently visible and disappear from
  the index on the next LM Studio restart. Register with (stage the file on D: first - import cannot
  cross volumes, and it prompts Y/n even with -y, so never pipe its output):
    lms import "<weights.gguf>" --user-repo mradermacher/Qwen2.5-VL-7B-Instruct-abliterated-local-GGUF -y

  Run this after a reboot (LM Studio must be running; the model itself JIT-loads on first request).
  Idempotent: starting an already-running server and loading an already-loaded model are both safe.

  Two flags here are NOT optional:
    * --gpu max        - fits in 8 GB (5.62 GiB weights) but leaves only ~250 MiB spare.
    * --parallel 1     - LM Studio defaults to 4 slots and SPLITS the context across them, so one
                         request would only get 1/4 of the context. A single slot also avoids the
                         ':<n>' identifier suffix that a duplicate load produces, which would break
                         the app's response `model` echo check.

  Details and rollback: docs/local-qwen-vl-compiler-setup.md ("2026-09-21 - moved to LM Studio on the
  APP host").
#>
[CmdletBinding()]
param(
    [string]$ModelKey = 'qwen2.5-vl-7b-instruct-abliterated-local',
    [int]$Port = 1234,
    [int]$ContextLength = 8192,
    [string]$BaseUrl = 'http://192.168.0.192:1234',
    [string]$Bind = '0.0.0.0',
    [switch]$SkipLoad
)

$ErrorActionPreference = 'Continue'

$lms = Join-Path $env:USERPROFILE '.lmstudio\bin\lms.exe'
if (-not (Test-Path $lms)) { throw "LM Studio CLI not found at '$lms'. Install/launch LM Studio first." }

$modelDir = Join-Path 'D:\LMStudio\Models' 'mradermacher\Qwen2.5-VL-7B-Instruct-abliterated-local-GGUF'
$required = @(
    (Join-Path $modelDir 'Qwen2.5-VL-7B-Instruct-abliterated-local.Q4_K_M.gguf'),
    (Join-Path $modelDir 'Qwen2.5-VL-7B-Instruct-abliterated-local.mmproj-f16.gguf')
)
foreach ($file in $required) {
    if (-not (Test-Path $file)) {
        throw "Missing model file '$file'. Download it first - see docs/local-qwen-vl-compiler-setup.md."
    }
}

# 1. Server -------------------------------------------------------------------
$serverStatus = (& $lms server status 2>&1 | Out-String)
if ($serverStatus -match "running on port $Port") {
    Write-Host "OK   LM Studio server already running on port $Port."
} else {
    Write-Host "..   Starting LM Studio server on port $Port (bind $Bind)"
    $start = (& $lms server start --port $Port --bind $Bind 2>&1 | Out-String)
    if ($start -notmatch 'Server is now running') { throw "Failed to start the LM Studio server: $start" }
    Write-Host "OK   LM Studio server started on port $Port (bind $Bind)."
}

# 2. Model ----------------------------------------------------------------
if (-not $SkipLoad) {
    $loaded = (& $lms ps 2>&1 | Out-String)
    if ($loaded -match [regex]::Escape($ModelKey)) {
        Write-Host "OK   Model '$ModelKey' already loaded."
    } else {
        Write-Host "..   Loading '$ModelKey' (gpu max, ctx $ContextLength, parallel 1) - takes ~10-20 s"
        $load = (& $lms load $ModelKey --gpu max -c $ContextLength --parallel 1 -y 2>&1 | Out-String)
        if ($load -notmatch 'Model loaded successfully') { throw "Failed to load '$ModelKey'`: $load" }
        Write-Host "OK   Model '$ModelKey' loaded."
    }
}

# 3. Verify the served id (the app rejects a response whose `model` differs) --
#    The provider row in the dev DB pins this BaseUrl, which uses the host's LAN IP (DHCP - it can
#    change). Warn early if the address the app will call is no longer this machine's.
$expectedHost = ([System.Uri]$BaseUrl).Host
$lanIps = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.InterfaceAlias -notlike 'vEthernet*' } |
    Select-Object -ExpandProperty IPAddress)
if ($lanIps -notcontains $expectedHost) {
    Write-Warning "BaseUrl host '$expectedHost' is not a current LAN IP of this host ($($lanIps -join ', ')). " +
        "The provider row in Model Manager will fail to resolve until the BaseUrl is updated or the DHCP lease is reserved."
}

try {
    $models = Invoke-RestMethod -Uri "$BaseUrl/v1/models" -TimeoutSec 20
} catch {
    throw "Endpoint $BaseUrl/v1/models is unreachable: $($_.Exception.Message)"
}
if ($models.data.id -notcontains $ModelKey) {
    throw "Endpoint $BaseUrl does not serve '$ModelKey' (readiness contract would fail). If it is missing " +
        "from the index, register the files with: lms import <weights.gguf> --user-repo mradermacher/Qwen2.5-VL-7B-Instruct-abliterated-local-GGUF -y"
}
Write-Host "OK   $BaseUrl serves '$ModelKey'."

& $lms ps 2>&1 | Out-String | Write-Host
nvidia-smi --query-gpu=memory.used,memory.free --format=csv,noheader
