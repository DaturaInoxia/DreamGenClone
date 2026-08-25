# install-model-remote.ps1 - install a checkpoint directly on the remote RunPod pod over SSH.
param(
    [Parameter(Mandatory=$true)][string]$ModelName,
    [Parameter(Mandatory=$true)][string]$SourceUrl,
    [Parameter(Mandatory=$false)][string]$ExpectedSha256,
  [Parameter(Mandatory=$false)][string]$Token,
  [Parameter(Mandatory=$false)][string]$SshUser,
  [Parameter(Mandatory=$false)][string]$SshHost,
  [Parameter(Mandatory=$false)][int]$SshPort
)
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$keyPath = Join-Path $repoRoot "artifacts\runpod\ssh_ed25519"
$sshEnv = Join-Path $repoRoot "artifacts\runpod\.ssh-env.ps1"
$user = "root"
$hostName = ""
$port = "22"
if (Test-Path $sshEnv) { . $sshEnv }
if ($env:RUNPOD_SSH_USER) { $user = $env:RUNPOD_SSH_USER }
if ($env:RUNPOD_SSH_HOST) { $hostName = $env:RUNPOD_SSH_HOST }
if ($env:RUNPOD_SSH_PORT) { $port = $env:RUNPOD_SSH_PORT }
if ($SshUser) { $user = $SshUser }
if ($SshHost) { $hostName = $SshHost }
if ($SshPort) { $port = $SshPort }

$runpodEnv = Join-Path $PSScriptRoot ".runpod-env.ps1"
if (Test-Path $runpodEnv) { . $runpodEnv }
if ([string]::IsNullOrWhiteSpace($Token)) { $Token = $env:CIVITAI_API_TOKEN }

if (-not (Test-Path $keyPath)) { throw "SSH private key not found: $keyPath" }
if (-not $hostName) { throw "RUNPOD_SSH_HOST is not configured in $sshEnv" }

$remoteScript = @'
set -eu
model_name="$1"
source_url="$2"
expected_sha256="$3"
auth_token="$4"
checkpoint_dir="/workspace/comfyui/models/checkpoints"
if [ ! -d "$checkpoint_dir" ]; then
  echo "Required persistent checkpoint directory is missing: $checkpoint_dir" >&2
  exit 2
fi
mkdir -p "$checkpoint_dir"
dest="$checkpoint_dir/$model_name"
echo "Installing $model_name into $checkpoint_dir"
if [ -n "$auth_token" ]; then
  curl -fL --retry 3 -C - -o "$dest" -H "Authorization: Bearer $auth_token" "$source_url"
else
  curl -fL --retry 3 -C - -o "$dest" "$source_url"
fi
if [ -n "$expected_sha256" ]; then
  actual_sha256="$(sha256sum "$dest" | awk '{print $1}')"
  if [ "$actual_sha256" != "$expected_sha256" ]; then
    echo "SHA256 mismatch: expected $expected_sha256 got $actual_sha256" >&2
    exit 3
  fi
fi
ls -lh "$dest"
echo "Installed checkpoint: $dest"
'@

$remoteScript = $remoteScript -replace "`r", ""
$encodedScript = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($remoteScript))

function ConvertTo-BashSingleQuoted([string]$Value) {
  $singleQuote = [string][char]39
  $escapedQuote = $singleQuote + '"' + $singleQuote + '"' + $singleQuote
  return $singleQuote + $Value.Replace($singleQuote, $escapedQuote) + $singleQuote
}

$remoteInvocation = "bash -s -- " + @($ModelName, $SourceUrl, $ExpectedSha256, $Token | ForEach-Object {
  ConvertTo-BashSingleQuoted ([string]$_)
}) -join " "
$remoteCommand = "echo $encodedScript | base64 -d | $remoteInvocation"
$sshArgs = @(
    "-o", "BatchMode=yes", "-o", "ConnectTimeout=20",
    "-o", "StrictHostKeyChecking=no", "-o", "UserKnownHostsFile=NUL",
    "-o", "IdentitiesOnly=yes", "-i", $keyPath, "-p", $port,
  "${user}@${hostName}"
)
& ssh @sshArgs $remoteCommand
if ($LASTEXITCODE -ne 0) { throw "Remote model installation failed (ssh exit $LASTEXITCODE)." }
