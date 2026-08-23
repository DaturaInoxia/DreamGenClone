# install-model-remote.ps1 - install a checkpoint directly on the remote RunPod pod over SSH.
param(
    [Parameter(Mandatory=$true)][string]$ModelName,
    [Parameter(Mandatory=$true)][string]$SourceUrl,
    [Parameter(Mandatory=$false)][string]$ExpectedSha256
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

if (-not (Test-Path $keyPath)) { throw "SSH private key not found: $keyPath" }
if (-not $hostName) { throw "RUNPOD_SSH_HOST is not configured in $sshEnv" }

$remoteScript = @'
set -eu
model_name="$1"
source_url="$2"
expected_sha256="$3"
checkpoint_dir="$(find / -type d -path '*/ComfyUI/models/checkpoints' 2>/dev/null | head -n 1)"
if [ -z "$checkpoint_dir" ]; then
  echo "Could not find ComfyUI/models/checkpoints" >&2
  exit 2
fi
mkdir -p "$checkpoint_dir"
dest="$checkpoint_dir/$model_name"
echo "Installing $model_name into $checkpoint_dir"
wget -c -O "$dest" "$source_url"
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

$encodedScript = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($remoteScript))
$remoteCommand = "echo $encodedScript | base64 -d | bash -s -- '$ModelName' '$SourceUrl' '$ExpectedSha256'"
$sshArgs = @(
    "-tt", "-o", "BatchMode=yes", "-o", "ConnectTimeout=20",
    "-o", "StrictHostKeyChecking=no", "-o", "UserKnownHostsFile=NUL",
    "-o", "IdentitiesOnly=yes", "-i", $keyPath, "-p", $port,
    "${user}@${hostName}", $remoteCommand
)
& ssh @sshArgs
if ($LASTEXITCODE -ne 0) { throw "Remote model installation failed (ssh exit $LASTEXITCODE)." }
