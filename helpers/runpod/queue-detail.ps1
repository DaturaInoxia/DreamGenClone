# queue-detail.ps1 - dump the current running/pending queue nodes (temp)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
Get-RunPodEnv

$q = Invoke-RestMethod -Uri "$env:COMFYUI_URL/queue" -Method GET -TimeoutSec 30
Write-Host "=== running ==="
foreach ($item in $q.queue_running) {
    if ($item -is [System.Array]) {
        Write-Host "id=$($item[0])"
        $wf = $item[2]
        if ($wf) {
            Write-Host "  nodes: $((@($wf.PSObject.Properties.Name)) -join ',')"
            foreach ($n in @($wf.PSObject.Properties.Name)) {
                $node = $wf.$n
                Write-Host "    $n : $($node.class_type)"
            }
        }
    }
}