# Fetch a Civitai LoRA for Qwen-Image-2.1 onto the local ComfyUI host (WOOD-GAME-MAIN).
# Idempotent and verifiable: resolves the version metadata from the Civitai API, downloads to a
# git-ignored staging dir on the dev box, verifies the safetensors header AND the SHA-256 the API
# declares, then copies to the host's models\loras and re-checks the byte count there.
#
# The Civitai token is read from the git-ignored env file and NEVER printed.
#
# Registered artifacts (run once per -ModelVersionId):
#   "NSFW LORA | Qwen Image 2.1" (TheseAlpacas)  Civitai model 2958918, version 3357315
#       -> NSFW Qwen by TheseAlpacas V2.safetensors        (v2.0, 2026-09-25; general acts + anatomy)
#   "Penis Qwen Image 2.1 by CoachBate"          Civitai model 2952865, version 3344536
#       -> qwen-image-2.1_penis_coachbate_preview1.safetensors  (2.1-specific male anatomy, free)
#   "qwen 2.1 vagina" (herpderpmerp)             Civitai model 2960871, version 3354330
#       -> qwen21_v2_000002750.safetensors                 (female vulva specialist, v1)
#   "Translucent Penetration" (v5 + Qwen-Image-2.1)  Civitai model 2322841, version 3370853
#       -> translucent_penetration-V5+Qwen-Image-2.1.safetensors  (PENETRATION specialist; author
#          constraints: 1.0-2.5 MP canvas, cfg ~3.0, weight 1.0, pair with a general NSFW LoRA)
#   "Qwen image 2.1 Perfect erect penis"         Civitai model 2955779, version 3348119
#       -> qwen2.1_penisV01_000004956.safetensors          (male specialist, 2.3k dl > CoachBate's 1.1k)
#   "qwen 2.1 阴道|vagina" v2.0                    Civitai model 2976277, version 3377365
#       -> pussyV2.safetensors                             (female specialist v2.0, pub 2026-10-02)
[CmdletBinding()]
param(
    [int]$ModelVersionId = 3357315,
    [string]$ComfyUiUrl = 'http://192.168.0.11:8188',
    [string]$SshTarget = 'wood-game-main\kenac@192.168.0.11',
    [string]$SshKey = "$env:USERPROFILE\.ssh\dgcomfy_ed25519",
    [string]$RemoteLoraDir = 'D:/ComfyUI/models/loras',
    [string]$Stage = 'artifacts/tmp/qwen21-nsfw-lora',
    [switch]$SkipCopy,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot
$stageDir = Join-Path $repoRoot $Stage
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

function Test-Safetensors {
    param([string]$Path)
    $fi = New-Object System.IO.FileInfo $Path
    $fs = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
    try {
        $lenBytes = New-Object byte[] 8
        if ($fs.Read($lenBytes, 0, 8) -ne 8) { throw 'file shorter than 8 bytes' }
        $hlen = [BitConverter]::ToInt64($lenBytes, 0)
        if ($hlen -le 0 -or $hlen -gt 100MB) { throw "implausible header length $hlen" }
        $hBytes = New-Object byte[] $hlen
        if ($fs.Read($hBytes, 0, $hlen) -ne $hlen) { throw 'header truncated' }
        $json = [System.Text.Encoding]::UTF8.GetString($hBytes)
    } finally { $fs.Close() }
    $obj = $json | ConvertFrom-Json
    $names = @($obj.PSObject.Properties.Name | Where-Object { $_ -ne '__metadata__' })
    if ($names.Count -eq 0) { throw 'header JSON has no tensors' }
    $maxEnd = 0
    foreach ($n in $names) {
        $end = [int64]$obj.$n.data_offsets[1]
        if ($end -gt $maxEnd) { $maxEnd = $end }
    }
    $expected = 8 + $hlen + $maxEnd
    if ($fi.Length -ne $expected) { throw "size $($fi.Length) != expected $expected (header $hlen + data $maxEnd)" }
    return "$($names.Count) tensors, $([math]::Round($fi.Length / 1MB, 1)) MB"
}

# ---- resolve the version metadata (no auth needed) --------------------------------------
$ver = Invoke-RestMethod "https://civitai.com/api/v1/model-versions/$ModelVersionId" -TimeoutSec 60
$file = @($ver.files | Where-Object { $_.type -eq 'Model' -and $_.primary }) | Select-Object -First 1
if (-not $file) { $file = @($ver.files | Where-Object { $_.type -eq 'Model' }) | Select-Object -First 1 }
if (-not $file) { throw "version $ModelVersionId exposes no Model file" }
$name = $file.name
$expectSha = $file.hashes.SHA256
$expectBytes = [int64][math]::Round($file.sizeKB * 1024)
Write-Host ("ARTIFACT  {0}" -f $name)
Write-Host ("  version {0} base={1} published={2}" -f $ver.id, $ver.baseModel, $ver.publishedAt)
Write-Host ("  expected {0} bytes  sha256 {1}" -f $expectBytes, $expectSha)

$local = Join-Path $stageDir $name
$needDownload = $true
if ((Test-Path $local) -and -not $Force) {
    if ((Get-Item $local).Length -eq $expectBytes) {
        $have = (Get-FileHash $local -Algorithm SHA256).Hash
        if ($have -eq $expectSha) { Write-Host '  staged copy already verified - skip download'; $needDownload = $false }
        else { Write-Host "  staged copy hash $have != expected - re-downloading" }
    } else { Write-Host "  staged copy size $((Get-Item $local).Length) != expected - re-downloading" }
}

if ($needDownload) {
    $raw = Get-Content (Join-Path $repoRoot 'helpers/runpod/.runpod-env.ps1') -Raw
    if ($raw -match 'CIVITAI_API_TOKEN\s*=\s*"([^"]+)"') { $token = $Matches[1] } else { throw 'CIVITAI_API_TOKEN not found in helpers/runpod/.runpod-env.ps1' }
    $url = "https://civitai.com/api/download/models/$ModelVersionId"
    Write-Host "  GET $url"
    # NOTE: PS 5.1 turns a native command's stderr into a terminating error while the preference is
    # 'Stop' (curl's progress meter goes to stderr). --no-progress-meter + a local Continue keeps it quiet.
    $prevEa = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & curl.exe --no-progress-meter -L --fail --retry 3 --retry-delay 4 --retry-all-errors -H "Authorization: Bearer $token" -o $local $url
    $curlExit = $LASTEXITCODE
    $ErrorActionPreference = $prevEa
    if ($curlExit -ne 0) { throw "download failed (curl $curlExit)" }
}

if (-not (Test-Path $local)) { throw "staged file missing: $local" }
$len = (Get-Item $local).Length
if ($len -ne $expectBytes) { throw "staged size $len != expected $expectBytes" }
$sha = (Get-FileHash $local -Algorithm SHA256).Hash
if ($sha -ne $expectSha) { throw "staged sha256 $sha != expected $expectSha" }
Write-Host ("  header OK: {0}" -f (Test-Safetensors $local))
Write-Host '  sha256 + size + header verified on the dev box'

if ($SkipCopy) { Write-Host '  -SkipCopy: not copied to host'; exit 0 }

# ---- copy to the host ------------------------------------------------------------------
# The host's SSH shell is cmd.exe and it eats nested double quotes, so remote snippets are piped
# in over stdin as a script (the pattern documented in helpers/runpod/README.md and
# helpers/local-comfyui-host/README.md), never passed inline after -Command "...".
$nameWin = $name
$remoteWin = ($RemoteLoraDir -replace '/', '\') + '\' + $nameWin
function Invoke-RemoteScript([string]$Body) {
    $out = $Body | & ssh -i $SshKey -o BatchMode=yes $SshTarget 'powershell -NoProfile -ExecutionPolicy Bypass -Command -'
    if ($LASTEXITCODE -ne 0) { throw "ssh probe failed ($LASTEXITCODE)" }
    return $out
}
$existsRaw = Invoke-RemoteScript "if (Test-Path '$remoteWin') { (Get-Item '$remoteWin').Length } else { 0 }"
$existsLen = 0
if ($existsRaw) { [void][int64]::TryParse(($existsRaw | Select-Object -Last 1).ToString().Trim(), [ref]$existsLen) }

if ($existsLen -eq $expectBytes -and -not $Force) {
    Write-Host "  host copy already present ($existsLen bytes) - skip scp"
} else {
    Write-Host "  scp -> ${SshTarget}:$RemoteLoraDir"
    & scp -i $SshKey -o BatchMode=yes $local "${SshTarget}:$RemoteLoraDir/"
    if ($LASTEXITCODE -ne 0) { throw "scp failed ($LASTEXITCODE)" }
    $afterRaw = Invoke-RemoteScript "if (Test-Path '$remoteWin') { (Get-Item '$remoteWin').Length } else { 0 }"
    $afterLen = 0
    if ($afterRaw) { [void][int64]::TryParse(($afterRaw | Select-Object -Last 1).ToString().Trim(), [ref]$afterLen) }
    if ($afterLen -ne $expectBytes) { throw "host size $afterLen != expected $expectBytes" }
    Write-Host "  host copy verified ($afterLen bytes)"
}

# ---- confirm ComfyUI exposes it --------------------------------------------------------
$l = Invoke-RestMethod "$ComfyUiUrl/object_info/LoraLoaderModelOnly" -TimeoutSec 120
$names = $l.LoraLoaderModelOnly.input.required.lora_name[0]
if ($names -notcontains $name) { throw "ComfyUI does not list '$name' in LoraLoaderModelOnly" }
Write-Host "  ComfyUI lists '$name' (of $($names.Count) LoRAs)"
Write-Host 'DONE'
