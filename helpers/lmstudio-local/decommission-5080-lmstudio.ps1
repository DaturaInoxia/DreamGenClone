<#
.SYNOPSIS
  Decommission LM Studio on the ComfyUI host (WOOD-GAME-MAIN, RTX 5080, 192.168.0.11).

.DESCRIPTION
  WHY: the Qwen2.5-VL scene-image compiler was moved OFF this host on 2026-09-21 to the app
  host (WOODGame, RTX 4060 Ti, http://192.168.0.192:1234, served id
  `qwen2.5-vl-7b-instruct-abliterated-local`) to free ~6 GB of VRAM for ComfyUI. The 5080 kept
  an LM Studio install that (a) auto-started at login via an `--run-as-service` startup entry,
  (b) bound 0.0.0.0:1234 with justInTimeModelLoading, and (c) still had the OLD, non-suffixed
  `Qwen2.5-VL-7B-Instruct-abliterated` GGUF on disk. The app's periodic health checks reached it
  through the public front `https://qwen.kenacwood.net` (legacy provider row "Local", now disabled)
  and JIT-loaded the 7B compiler — plus a 7B mistral and a 14B — back onto the 5080, silently
  recreating the exact VRAM pressure the move was meant to remove.

  This script is FORWARD-ONLY and IDEMPOTENT. It does NOT uninstall LM Studio: re-launching it is
  then a deliberate manual act, and with these settings it serves and loads nothing. It does NOT
  touch any other model in the library.

  Run ON the 5080 host:
      powershell -ExecutionPolicy RemoteSigned -File decommission-5080-lmstudio.ps1
  or from the dev box:
      scp -i ~/.ssh/dgcomfy_ed25519 helpers/lmstudio-local/decommission-5080-lmstudio.ps1 \
          "wood-game-main\kenac@192.168.0.11:C:/Users/kenac/"
      ssh -i ~/.ssh/dgcomfy_ed25519 "wood-game-main\kenac@192.168.0.11" \
          "powershell -ExecutionPolicy Bypass -File C:\Users\kenac\decommission-5080-lmstudio.ps1"

.PARAMETER SkipWeightArchive
  Leave the duplicate VL GGUF weights in place (only stop/disable LM Studio).

.NOTES
  Reversal: re-add the HKCU Run value logged below, restore
  `~/.lmstudio/.internal/http-server-config.json.pre-decommission-*.bak`, and set
  autoStartOnLaunch/justInTimeModelLoading back to true. The DB rows are re-enabled with
  artifacts/tmp/dbquery/queries/rollback_legacy_local_provider.sql — do that ONLY if you
  deliberately want the compiler running on the 5080 again.
#>
param(
    [switch]$SkipWeightArchive
)

$ErrorActionPreference = 'Stop'
$log = New-Object System.Collections.Generic.List[string]
function Say([string]$m) { $log.Add($m); Write-Output $m }

# PS 5.1 turns a native command's stderr into a TERMINATING NativeCommandError when
# $ErrorActionPreference = 'Stop' and 2>&1 is used. lms.exe prints its normal status
# messages to stderr, so every lms call goes through this wrapper.
function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments)
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        # via cmd.exe so PowerShell never converts the child's stderr into an ErrorRecord
        $line = '"' + $Exe + '" ' + ($Arguments -join ' ') + ' 2>&1'
        $out = & cmd.exe /c $line | Out-String
        return $out.Trim()
    } finally {
        $ErrorActionPreference = $saved
    }
}

$lm  = Join-Path $env:USERPROFILE '.lmstudio'
$cfg = Join-Path $lm '.internal\http-server-config.json'
$lms = Join-Path $lm 'bin\lms.exe'
$stamp = '20260926'

Say "== LM Studio decommission on $env:COMPUTERNAME : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') =="

# --- 1. report what is loaded, then stop -------------------------------------------
# NOTE: `lms <anything>` STARTS the LM Studio app ("Waking up LM Studio service...") when it
# is not already running. That is how this host kept coming back: any lms invocation, including
# a harmless `lms ps`, boots the app again. So only ever call lms when the service is ALREADY up,
# and never rely on `lms unload` to free the GPU - killing the process tree does that definitively.
$listening = @(Get-NetTCPConnection -LocalPort 1234 -State Listen -ErrorAction SilentlyContinue)
if ($listening.Count -gt 0 -and (Test-Path $lms)) {
    Say ("   loaded models before stop: " + (Invoke-Native -Exe $lms -Arguments @('ps')))
} else {
    Say "   LM Studio service not listening on :1234 - skipping 'lms ps' so the app is not woken"
}

# --- 2. stop the app and its llama-server backends --------------------------------
$app = @(Get-Process -Name 'LM Studio' -ErrorAction SilentlyContinue)
if ($app.Count -gt 0) {
    Say "   stopping $($app.Count) 'LM Studio' process(es): $($app.Id -join ', ')"
    $app | Stop-Process -Force
    Start-Sleep -Seconds 3
} else {
    Say "   'LM Studio' was not running"
}
$back = @(Get-Process -Name 'llama-server' -ErrorAction SilentlyContinue)
if ($back.Count -gt 0) {
    Say "   stopping $($back.Count) llama-server backend(s): $($back.Id -join ', ')"
    $back | Stop-Process -Force
} else {
    Say "   no llama-server backend running"
}

# --- 3. remove every login-autostart hook ----------------------------------------
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$removed = 0
if (Test-Path $runKey) {
    $props = (Get-Item $runKey).Property | Where-Object { $_ -match 'LM Studio|lmstudio' }
    foreach ($p in $props) {
        $val = (Get-ItemProperty $runKey -Name $p).$p
        Say "   REMOVE startup (HKCU Run) '$p' = $val   <-- restore this value to reverse"
        Remove-ItemProperty -Path $runKey -Name $p
        $removed++
    }
}
$startupDir = [Environment]::GetFolderPath('Startup')
foreach ($lnk in @(Get-ChildItem $startupDir -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'LM Studio|lmstudio' })) {
    Say "   REMOVE startup shortcut $($lnk.Name)"
    Remove-Item $lnk.FullName -Force
    $removed++
}
foreach ($task in @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -match 'LM Studio|lmstudio' })) {
    Say "   DISABLE scheduled task $($task.TaskName)"
    Disable-ScheduledTask -TaskName $task.TaskName -ErrorAction SilentlyContinue | Out-Null
    $removed++
}
if ($removed -eq 0) { Say "   no LM Studio login-autostart hook found (already clean)" }

# --- 4. config: no autostart, no JIT loading, loopback only ----------------------
if (Test-Path $cfg) {
    $backup = "$cfg.pre-decommission-$stamp.bak"
    if (-not (Test-Path $backup)) { Copy-Item $cfg $backup -Force }
    $j = Get-Content $cfg -Raw | ConvertFrom-Json
    Say ("   config before: autoStartOnLaunch=$($j.autoStartOnLaunch) justInTimeModelLoading=$($j.justInTimeModelLoading) networkInterface=$($j.networkInterface) port=$($j.port)")
    $j.autoStartOnLaunch        = $false
    $j.justInTimeModelLoading   = $false
    $j.networkInterface         = '127.0.0.1'
    $json = $j | ConvertTo-Json -Depth 8
    # WriteAllText => no BOM (LM Studio's JSON reader rejects a BOM-prefixed file)
    [System.IO.File]::WriteAllText($cfg, $json)
    $k = Get-Content $cfg -Raw | ConvertFrom-Json
    Say ("   config after:  autoStartOnLaunch=$($k.autoStartOnLaunch) justInTimeModelLoading=$($k.justInTimeModelLoading) networkInterface=$($k.networkInterface)")
    Say "   config backup: $backup"
} else {
    Say "   WARN: config not found at $cfg"
}

# --- 5. archive the duplicate VL weights -----------------------------------------
if (-not $SkipWeightArchive) {
    $src = 'D:\LMStudio\Models\mradermacher\Qwen2.5-VL-7B-Instruct-abliterated-GGUF'
    $dst = 'D:\LMStudio\_superseded\Qwen2.5-VL-7B-Instruct-abliterated-GGUF (moved to WOODGame 2026-09-26)'
    if (Test-Path $src) {
        New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
        Move-Item -Path $src -Destination $dst -Force
        Say "   archived duplicate VL weights -> $dst"
    } else {
        Say "   duplicate VL weights already archived or absent: $src"
    }
}

# --- 6. verify -------------------------------------------------------------------
Start-Sleep -Seconds 2
Say ("   verify: LM Studio processes = " + (@(Get-Process -Name 'LM Studio' -ErrorAction SilentlyContinue).Count))
Say ("   verify: llama-server processes = " + (@(Get-Process -Name 'llama-server' -ErrorAction SilentlyContinue).Count))
$listener = @(Get-NetTCPConnection -LocalPort 1234 -State Listen -ErrorAction SilentlyContinue)
Say ("   verify: listeners on :1234 = " + $listener.Count)

Say "== done =="
$logPath = Join-Path $env:USERPROFILE "lmstudio-decommission-$stamp.log"
$log | Set-Content -Path $logPath -Encoding utf8
Say "   log: $logPath"
