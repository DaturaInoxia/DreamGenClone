# ssh.ps1 - SSH into the RunPod pod using the local (git-ignored) key.
#
# Expects:
#   artifacts/runpod/runpod_ed25519      - your private key (git-ignored via artifacts/)
#   artifacts/runpod/.ssh-env.ps1        - optional small env file with RUNPOD_SSH_USER/SSH_HOST/SSH_PORT
#
# Usage (from repo root):
#   powershell -File helpers/runpod/ssh.ps1                     # open interactive shell
#   powershell -File helpers/runpod/ssh.ps1 -Command 'whoami'   # run one remote command
param(
    [Parameter(Mandatory=$false)][string]$Command = $null
)
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$sshDir   = Join-Path $repoRoot "artifacts\runpod"
$keyPath  = Join-Path $sshDir "ssh_ed25519"

# Optional SSH env overrides
$sshEnv = Join-Path $sshDir ".ssh-env.ps1"
$user = "root"
$hostName = ""
$port = "22"

if (Test-Path $sshEnv) { . $sshEnv }

if ($user -eq "root" -and $env:RUNPOD_SSH_USER) { $user = $env:RUNPOD_SSH_USER }
if (-not $hostName) { $hostName = $env:RUNPOD_SSH_HOST }
if ($port -eq "22" -and $env:RUNPOD_SSH_PORT) { $port = $env:RUNPOD_SSH_PORT }
if (-not $hostName) {
    Write-Host "SSH host not set. Edit artifacts/runpod/.ssh-env.ps1 with your SSH command host/port (shown in RunPod console)."
    exit 1
}

if (-not (Test-Path $keyPath)) {
    Write-Host "SSH key not found at $keyPath. Place your private key there (git-ignored)."
    exit 1
}

$cmdArgs = @("-tt", "-i", $keyPath, "-o", "StrictHostKeyChecking=no",
             "-o", "UserKnownHostsFile=/dev/null", "-o", "IdentitiesOnly=yes",
             "-p", $port, "${user}@${hostName}")

if ($Command) {
    $cmdArgs += $Command
}
& ssh @cmdArgs