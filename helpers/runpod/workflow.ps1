# workflow.ps1 - list / validate / export ComfyUI workflow JSON
param(
    [Parameter(Mandatory=$false)][string]$WorkflowPath,
    [Parameter(Mandatory=$false)][string]$ExportOut,    # optional output file
    [Parameter(Mandatory=$false)][switch]$List
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

$workflowsDir = "ComfyUI/user/default/workflows"   # adapter-relative; adjust for your server layout

if ($List) {
    if (Test-Path $workflowsDir) { Get-ChildItem $workflowsDir -Recurse -Filter *.json | Select-Object FullName }
    else { Write-Host "No local workflows dir at $workflowsDir" }
    exit 0
}

if (-not $WorkflowPath -or -not (Test-Path $WorkflowPath)) {
    throw "Usage: workflow.ps1 -WorkflowPath <file.json> [-ExportOut <out>]"
}

$wf = Get-Content -Raw $WorkflowPath | ConvertFrom-Json
if ($null -eq $wf) { throw "Workflow JSON is empty or invalid." }

# Minimal required node keys for the standard text-to-image graph.
$required = @("4","6","3")   # Load Checkpoint, CLIPTextEncode, KSampler in the default template ids
$found = @($required | Where-Object { $wf.PSObject.Properties.Name -contains $_ })
Write-Host "Workflow nodes present for ids [$($required -join ',')]: $($found -join ',')"
if ($found.Count -lt $required.Count) {
    Write-Warning "Some default ids missing; verify this matches your graph node ids."
}

if ($ExportOut) {
    Copy-Item $WorkflowPath $ExportOut -Force
    Write-Host "Exported workflow to $ExportOut"
}
Write-Host "Workflow OK: $WorkflowPath"