# generate-one.ps1 - reliably generate EXACTLY ONE image per invocation.
# Submits the given workflow once, polls until that exact prompt_id completes,
# then downloads the single produced image. No resubmit, no loop.
param(
    [Parameter(Mandatory=$true)][string]$WorkflowPath,
    [Parameter(Mandatory=$false)][string]$Prompt,          # optional override of positive prompt
    [Parameter(Mandatory=$false)][string]$Negative,        # optional override of negative prompt
    [Parameter(Mandatory=$false)][string]$Checkpoint,      # optional checkpoint override
    [Parameter(Mandatory=$false)][int]$Seed = -1,          # optional fixed seed
    [Parameter(Mandatory=$false)][string]$Prefix = "img",  # output filename prefix
    [Parameter(Mandatory=$false)][string]$OutputDir = "./out",
    [Parameter(Mandatory=$false)][int]$TimeoutSec = 600,
    [Parameter(Mandatory=$false)][string]$ComfyUiUrl,
    [Parameter(Mandatory=$false)][string]$InputImage,
    [Parameter(Mandatory=$false)][string]$InputImagePath
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
if ([string]::IsNullOrWhiteSpace($ComfyUiUrl)) {
    Get-RunPodEnv
    $endpoint = $env:COMFYUI_URL
}
else {
    $endpoint = $ComfyUiUrl.TrimEnd('/')
}

if (-not (Test-Path $WorkflowPath)) { throw "Workflow not found: $WorkflowPath" }
if ($InputImage -and $InputImagePath) { throw "Specify either InputImage (an existing ComfyUI image name) or InputImagePath (a local file), not both." }
if ($InputImagePath -and -not (Test-Path $InputImagePath -PathType Leaf)) { throw "Input image file was not found: $InputImagePath" }
$wf = Get-Content -Raw $WorkflowPath | ConvertFrom-Json

function Upload-ComfyUiImage {
    param(
        [Parameter(Mandatory=$true)][string]$Endpoint,
        [Parameter(Mandatory=$true)][string]$Path
    )

    $fileStream = $null
    $multipart = $null
    $httpClient = $null
    try {
        $fileName = "dg_input_$((Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmssfff'))_$([IO.Path]::GetFileName($Path))"
        $fileStream = [IO.File]::OpenRead((Resolve-Path $Path))
        $streamContent = [System.Net.Http.StreamContent]::new($fileStream)
        $extension = [IO.Path]::GetExtension($Path).ToLowerInvariant()
        $contentType = if ($extension -in '.jpg', '.jpeg') { 'image/jpeg' } else { 'image/png' }
        $streamContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($contentType)
        $multipart = [System.Net.Http.MultipartFormDataContent]::new()
        $multipart.Add($streamContent, 'image', $fileName)
        $httpClient = [System.Net.Http.HttpClient]::new()
        $httpClient.Timeout = [TimeSpan]::FromSeconds(120)
        $response = $httpClient.PostAsync("$Endpoint/upload/image", $multipart).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw "ComfyUI image upload failed ($([int]$response.StatusCode)): $body" }
        $upload = $body | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($upload.name)) { throw "ComfyUI image upload returned no image name: $body" }
        if ([string]::IsNullOrWhiteSpace($upload.subfolder)) { return $upload.name }
        return "$($upload.subfolder)/$($upload.name)"
    }
    finally {
        if ($httpClient) { $httpClient.Dispose() }
        if ($multipart) { $multipart.Dispose() }
        if ($fileStream) { $fileStream.Dispose() }
    }
}

# Deterministic unique prefix so every run is a distinct output file on the pod.
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$prefix = "dg_${prefix}_${stamp}"

# Standard node ids used by our workflow: 6=positive CLIP, 7=negative CLIP, 3=KSampler seed,
# 9=SaveImage filename_prefix. (Optional-ish; only override if present.)
if ($Prompt -and $wf.PSObject.Properties["6"]) {
    $positiveInputs = $wf.PSObject.Properties["6"].Value.inputs
    if ($positiveInputs.PSObject.Properties["text"]) { $positiveInputs.text = $Prompt }
    elseif ($positiveInputs.PSObject.Properties["prompt"]) { $positiveInputs.prompt = $Prompt }
}
if ($Negative -and $wf.PSObject.Properties["7"]) {
    $negativeInputs = $wf.PSObject.Properties["7"].Value.inputs
    if ($negativeInputs.PSObject.Properties["text"]) { $negativeInputs.text = $Negative }
    elseif ($negativeInputs.PSObject.Properties["prompt"]) { $negativeInputs.prompt = $Negative }
}
if ($Seed -ge 0 -and $wf.PSObject.Properties["3"]) { $wf.PSObject.Properties["3"].Value.inputs.seed = $Seed }
if ($Checkpoint -and $wf.PSObject.Properties["4"]) { $wf.PSObject.Properties["4"].Value.inputs.ckpt_name = $Checkpoint }
if ($InputImagePath) {
    $InputImage = Upload-ComfyUiImage -Endpoint $endpoint -Path $InputImagePath
    Write-Host "Uploaded input image as: $InputImage"
}
if ($InputImage -and $wf.PSObject.Properties["1"] -and $wf.PSObject.Properties["1"].Value.inputs.PSObject.Properties["image"]) {
    $wf.PSObject.Properties["1"].Value.inputs.image = $InputImage
}
if ($wf.PSObject.Properties["9"] -and $wf.PSObject.Properties["9"].Value.inputs.PSObject.Properties["filename_prefix"]) {
    $wf.PSObject.Properties["9"].Value.inputs.filename_prefix = $prefix
}

# 1) Submit exactly once.
Write-Host "Submitting workflow once..."
$payload = @{ prompt = $wf; client_id = "dreamgen-one" }
$resp = Invoke-RestMethod -Uri "$endpoint/prompt" -Method POST -ContentType "application/json" -Body ($payload | ConvertTo-Json -Depth 20) -TimeoutSec 60
$promptId = $resp.prompt_id
if (-not $promptId) { throw "ComfyUI returned no prompt_id: $($resp | ConvertTo-Json -Depth 5)" }
Write-Host "Queued prompt_id=$promptId  output_prefix=$prefix"

# 2) Poll /history ONLY for this prompt_id until success/error or timeout.
$deadline = (Get-Date).AddSeconds($TimeoutSec)
$entry = $null
do {
    Start-Sleep -Seconds 2
    $hist = Invoke-RestMethod -Uri "$endpoint/history" -Method GET -TimeoutSec 30
    if ($hist.PSObject.Properties.Name -contains $promptId) {
        $entry = $hist.$promptId
        break
    }
} while ((Get-Date) -lt $deadline)

if ($null -eq $entry) { throw "Timed out waiting for prompt_id $promptId after ${TimeoutSec}s." }
$status = $entry.status
if ($status.status_str -eq "error") { throw "Workflow error: $($status | ConvertTo-Json -Depth 8)" }
if ($status.status_str -ne "success") { throw "Unexpected status: $($status.status_str)" }

# 3) Fetch the one output image.
$images = @()
foreach ($nodeOut in $entry.outputs.PSObject.Properties) {
    foreach ($img in $nodeOut.Value.images) {
        if ($null -eq $img) { continue }
        $images += ,$img
    }
}
if ($images.Count -lt 1) { throw "No images produced for prompt $promptId." }

$img = $images[0]
$query = "filename=$([uri]::EscapeDataString($img.filename))"
if ($img.subfolder) { $query += "&subfolder=$([uri]::EscapeDataString($img.subfolder))" }
if ($img.type)      { $query += "&type=$([uri]::EscapeDataString($img.type))" }
$url = "$endpoint/view?$query"

# $OutputDir may be a directory path OR a target file. If it ends in .png treat as file, else directory.
if ([IO.Path]::GetExtension($OutputDir) -eq ".png") {
    $outFile = $OutputDir
    $parentDir = Split-Path -Parent $outFile
    if ($parentDir -and -not (Test-Path $parentDir)) {
        New-Item -ItemType Directory -Path $parentDir -Force | Out-Null
    }
} else {
    if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
    $outFile = Join-Path $OutputDir "$prefix.png"
}
Invoke-RestMethod -Uri $url -Method GET -OutFile $outFile -TimeoutSec 120
Write-Host "Generated 1 image: $outFile"
Write-Host "  source filename on pod: $($img.filename)"