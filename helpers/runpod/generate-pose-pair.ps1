# Generate one clothed image per pose skeleton via the existing OpenPose workflow.
# Usage: powershell -File helpers/runpod/generate-pose-pair.ps1
$ErrorActionPreference = "Stop"
$endpoint = "https://emqmxptqdxu7pp-3000.proxy.runpod.net"
$wfBase = "helpers/runpod/workflows/juggernaut-fellatio-openpose.json"
$outDir = "artifacts/tmp/images/juggernaut-fellatio-openpose"
$prompt = "a photo of a woman, fully clothed, lying on a bed, overhead view, full body"
$negative = "bad anatomy, poorly drawn hands, deformed, blurry, low quality, watermark, text"

Add-Type -AssemblyName System.Net.Http

function Upload-ComfyUiImage([string]$Path) {
    $fileName = "dg_input_$((Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmssfff'))_$([IO.Path]::GetFileName($Path))"
    $fileStream = [IO.File]::OpenRead((Resolve-Path $Path))
    $streamContent = [System.Net.Http.StreamContent]::new($fileStream)
    $streamContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("image/png")
    $multipart = [System.Net.Http.MultipartFormDataContent]::new()
    $multipart.Add($streamContent, 'image', $fileName)
    $httpClient = [System.Net.Http.HttpClient]::new()
    $httpClient.Timeout = [TimeSpan]::FromSeconds(120)
    try {
        $response = $httpClient.PostAsync("$endpoint/upload/image", $multipart).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw "Upload failed: $body" }
        $upload = $body | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($upload.subfolder)) { return $upload.name }
        return "$($upload.subfolder)/$($upload.name)"
    }
    finally {
        $httpClient.Dispose(); $multipart.Dispose(); $fileStream.Dispose()
    }
}

$items = @(
    @{ skel = "$outDir/lying020_skeleton.png"; seed = 1640; prefix = "dg_lying020" }
)

foreach ($item in $items) {
    $name = Upload-ComfyUiImage -Path $item.skel
    Write-Host "Uploaded skeleton as: $name"
    $wf = Get-Content -Raw $wfBase | ConvertFrom-Json
    $wf.PSObject.Properties["10"].Value.inputs.image = $name
    $tmpWf = "$outDir/wf_$($item.prefix).json"
    [System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $outDir).Path + "\wf_$($item.prefix).json", ($wf | ConvertTo-Json -Depth 20))
    Write-Host "=== generate $($item.prefix) seed=$($item.seed) ==="
    & powershell -ExecutionPolicy RemoteSigned -File "helpers/runpod/generate-one.ps1" `
        -WorkflowPath $tmpWf -Prompt $prompt -Negative $negative `
        -Seed $item.seed -Prefix $item.prefix -OutputDir $outDir `
        -ComfyUiUrl $endpoint -TimeoutSec 300
}
