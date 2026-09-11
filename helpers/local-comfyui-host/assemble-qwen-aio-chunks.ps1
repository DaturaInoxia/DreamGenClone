<#
.SYNOPSIS
  Assembles and verifies the Qwen Rapid-AIO NSFW v23 checkpoint from parallel chunks.

.DESCRIPTION
  Companion to `fetch-qwen-aio-parallel.ps1`. Verifies every chunk against its expected byte range,
  concatenates them in order, verifies the assembled byte count, prints the SHA-256, and only then
  removes the chunk folder. Idempotent: a complete assembled file is accepted as-is.

  Run ON the ComfyUI host (WOOD-GAME-MAIN). PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [string]$TargetDirectory = 'D:\ComfyUI\models\checkpoints',
    [string]$FileName = 'Qwen-Rapid-AIO-NSFW-v23.safetensors',
    [long]$ExpectedBytes = 28431840023,
    [int]$Chunks = 8,
    [string]$ChunkFolder = 'aio-chunks'
)

$ErrorActionPreference = 'Stop'

$target = Join-Path $TargetDirectory $FileName
$work = Join-Path $TargetDirectory $ChunkFolder

if (-not (Test-Path $work)) { throw "Chunk folder '$work' was not found." }
if ((Test-Path $target) -and ((Get-Item $target).Length -eq $ExpectedBytes)) {
    "Already complete: $target ($ExpectedBytes bytes)"
    "SHA256: $((Get-FileHash $target -Algorithm SHA256).Hash)"
    exit 0
}

$base = [math]::Floor($ExpectedBytes / $Chunks)
$chunkPaths = @()
$total = 0

for ($i = 0; $i -lt $Chunks; $i++) {
    $start = $i * $base
    if ($i -eq ($Chunks - 1)) { $end = $ExpectedBytes - 1 } else { $end = $start + $base - 1 }
    $expected = $end - $start + 1
    $path = Join-Path $work ("chunk{0:d2}.bin" -f $i)
    if (-not (Test-Path $path)) { throw "Chunk $i is missing: $path" }
    $actual = (Get-Item $path).Length
    if ($actual -ne $expected) { throw "Chunk $i is $actual bytes, expected $expected." }
    $chunkPaths += $path
    $total += $actual
}
"all $Chunks chunks verified: $total bytes"

if (Test-Path $target) {
    "removing incomplete file: $target ($((Get-Item $target).Length) bytes)"
    Remove-Item $target -Force
}

$copySource = ($chunkPaths -join '+')
& cmd.exe /c "copy /b $copySource `"$target`"" | Out-Null

$final = (Get-Item $target).Length
if ($final -ne $ExpectedBytes) { throw "Assembled file is $final bytes, expected $ExpectedBytes." }

Remove-Item $work -Recurse -Force
"ASSEMBLED $final bytes"
"SHA256: $((Get-FileHash $target -Algorithm SHA256).Hash)"
