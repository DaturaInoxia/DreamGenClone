<#
.SYNOPSIS
  Fetches the Qwen Rapid-AIO NSFW v23 checkpoint with 8 parallel HTTP range requests.

.DESCRIPTION
  Hugging Face throttles a single connection to roughly 2 MB/s for this 28.4 GB artifact, so a plain
  `curl` takes hours. Range requests are honoured, so the file is fetched as 8 equal chunks in
  parallel, size-verified per chunk, concatenated in order, and the assembled file is size-verified.

  Idempotent: a chunk whose byte count already matches its range is reused, and a complete assembled
  file is accepted without touching the network.

  Run ON the ComfyUI host (WOOD-GAME-MAIN). PowerShell 5.1 compatible.

.NOTES
  Writes only to the ComfyUI models folder on the host.
#>
[CmdletBinding()]
param(
    [string]$TargetDirectory = 'D:\ComfyUI\models\checkpoints',
    [string]$FileName = 'Qwen-Rapid-AIO-NSFW-v23.safetensors',
    [long]$ExpectedBytes = 28431840023,
    [int]$Chunks = 8,
    [string]$Url = 'https://huggingface.co/Phr00t/Qwen-Image-Edit-Rapid-AIO/resolve/main/v23/Qwen-Rapid-AIO-NSFW-v23.safetensors'
)

$ErrorActionPreference = 'Stop'

$curlCommand = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $curlCommand) { throw 'curl.exe is required and was not found on PATH.' }
$curlPath = $curlCommand.Source

$target = Join-Path $TargetDirectory $FileName
$work = Join-Path $TargetDirectory 'aio-chunks'
if (-not (Test-Path $work)) { New-Item -ItemType Directory -Path $work | Out-Null }

if ((Test-Path $target) -and ((Get-Item $target).Length -eq $ExpectedBytes)) {
    "Already complete: $target ($ExpectedBytes bytes)"
    exit 0
}

$base = [math]::Floor($ExpectedBytes / $Chunks)
$chunkPaths = @()
$pending = @()

for ($i = 0; $i -lt $Chunks; $i++) {
    $start = $i * $base
    if ($i -eq ($Chunks - 1)) {
        $end = $ExpectedBytes - 1
    } else {
        $end = $start + $base - 1
    }
    $expected = $end - $start + 1
    $out = Join-Path $work ("chunk{0:d2}.bin" -f $i)
    $chunkPaths += $out

    if ((Test-Path $out) -and ((Get-Item $out).Length -eq $expected)) {
        "chunk $i complete ($expected bytes)"
        continue
    }

    $arguments = @(
        '--location', '--fail', '--silent', '--show-error',
        '--retry', '20', '--retry-delay', '5',
        '--range', "$start-$end",
        '--output', $out,
        $Url
    )
    "chunk $i starting: $start-$end ($expected bytes)"
    $pending += Start-Process -FilePath $curlPath -ArgumentList $arguments -PassThru -NoNewWindow
}

foreach ($process in $pending) { $process.WaitForExit() }
foreach ($process in $pending) {
    if ($process.ExitCode -ne 0) { throw "A chunk download failed with exit code $($process.ExitCode)." }
}

# Verify every chunk before assembling anything.
$total = 0
for ($i = 0; $i -lt $Chunks; $i++) {
    $start = $i * $base
    if ($i -eq ($Chunks - 1)) { $end = $ExpectedBytes - 1 } else { $end = $start + $base - 1 }
    $expected = $end - $start + 1
    $actual = (Get-Item $chunkPaths[$i]).Length
    if ($actual -ne $expected) { throw "Chunk $i is $actual bytes, expected $expected." }
    $total += $actual
}
"all chunks verified: $total bytes"

if (Test-Path $target) { Remove-Item $target -Force }
$copySource = ($chunkPaths -join '+')
& cmd.exe /c "copy /b $copySource `"$target`"" | Out-Null

$final = (Get-Item $target).Length
if ($final -ne $ExpectedBytes) { throw "Assembled file is $final bytes, expected $ExpectedBytes." }

Remove-Item $work -Recurse -Force
"DOWNLOAD_COMPLETE $final bytes"
"SHA256: $((Get-FileHash $target -Algorithm SHA256).Hash)"
