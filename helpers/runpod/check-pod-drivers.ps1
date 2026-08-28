# Check NVIDIA driver version on all RUNNING pods to find CUDA-13-capable hosts (driver >= 580).
$ErrorActionPreference = 'Continue'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv
$keyPath = Join-Path $repoRoot "artifacts\runpod\ssh_ed25519"

$body = @{ query = 'query { myself { pods { id name desiredStatus gpuCount machine { gpuDisplayName } runtime { ports { ip privatePort publicPort type } uptimeInSeconds } } } }' } | ConvertTo-Json -Depth 10
$r = (Invoke-RestMethod -Uri "https://api.runpod.io/graphql" -Method POST -ContentType "application/json" `
    -Headers @{ Authorization = "Bearer $env:RUNPOD_API_KEY" } -Body $body).data.myself.pods

foreach ($pod in $r) {
    if ($pod.desiredStatus -ne "RUNNING") { continue }
    $tcp = @($pod.runtime.ports | Where-Object { $_.type -eq "tcp" })[0]
    if ($null -eq $tcp) { Write-Output "$($pod.id) ($($pod.machine.gpuDisplayName)): no tcp port"; continue }
    $sshArgs = @("-o","BatchMode=yes","-o","ConnectTimeout=20","-o","StrictHostKeyChecking=no","-o","UserKnownHostsFile=NUL","-o","IdentitiesOnly=yes","-i",$keyPath,"-p",$tcp.publicPort,"root@$($tcp.ip)")
    $cmd = "nvidia-smi --query-gpu=driver_version --format=csv,noheader 2>/dev/null | head -1"
    $driver = (& ssh @sshArgs $cmd 2>$null | Out-String).Trim()
    Write-Output ("{0} | {1} | driver={2}" -f $pod.id, $pod.machine.gpuDisplayName, $driver)
}
