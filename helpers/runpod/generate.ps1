# generate.ps1 - queue a ComfyUI workflow via /prompt and fetch the resulting image
param(
    [Parameter(Mandatory=$true)][string]$WorkflowPath,  # path to workflow JSON
    [Parameter(Mandatory=$false)][string]$Prompt,       # optional positive prompt to inject
    [Parameter(Mandatory=$false)][string]$Negative,     # optional negative prompt
    [Parameter(Mandatory=$false)][int]$Seed = -1,
    [Parameter(Mandatory=$false)][string]$Output = "./output.png"
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

if (-not (Test-Path $WorkflowPath)) { throw "Workflow not found: $WorkflowPath" }
$wf = Get-Content -Raw $WorkflowPath | ConvertFrom-Json

# Inject common overrides into the default template node ids (6=positive, 7=negative, 3=KSampler seed).
if ($Prompt)      { $wf.PSObject.Properties["6"].Value.inputs.text = $Prompt }
if ($Negative)    { $wf.PSObject.Properties["7"].Value.inputs.text = $Negative }
if ($Seed -ge 0)  { $wf.PSObject.Properties["3"].Value.inputs.seed = $Seed }

Write-Host "Queuing workflow..."
$resp = Invoke-ComfyUi -Kind Prompt -Body $wf
$promptId = $resp.prompt_id
if (-not $promptId) { throw "No prompt_id in response." }
Write-Host "Queued prompt_id=$promptId"

# Poll /history for completion.
$deadline = (Get-Date).AddMinutes(10)
do {
    Start-Sleep -Seconds 2
    $hist = Invoke-ComfyUi -Kind History
    if ($hist.PSObject.Properties.Name -contains $promptId) {
        $entry = $hist.$promptId
        $status = $entry.status
        if ($status.status_str -eq "success") {
            break
        } elseif ($status.status_str -eq "error") {
            throw "Workflow error: $($status | ConvertTo-Json -Depth 10)"
        }
    }
} while ((Get-Date) -lt $deadline)
if (-not ($hist.PSObject.Properties.Name -contains $promptId)) { throw "Timed out waiting for result." }

# Save outputs referenced in history. Prefer first output image.
$entry = $hist.$promptId
$downloaded = 0
foreach ($nodeOut in $entry.outputs.PSObject.Properties) {
    foreach ($img in $nodeOut.Value.images) {
        if ($null -eq $img) { continue }
        $query = "filename=$([uri]::EscapeDataString($img.filename))"
        if ($img.subfolder) { $query += "&subfolder=$([uri]::EscapeDataString($img.subfolder))" }
        if ($img.type)      { $query += "&type=$([uri]::EscapeDataString($img.type))" }
        $url = "$env:COMFYUI_URL/view?$query"
        Get-RunPodEnv
        Invoke-RestMethod -Uri $url -Method GET -OutFile $Output
        Write-Host "Downloaded $($img.filename) -> $Output from $url"
        $downloaded++
    }
}
if ($downloaded -eq 0) { throw "No images found in workflow history output." }