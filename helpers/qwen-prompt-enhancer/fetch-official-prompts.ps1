<#
B-135 B135-039 - vendor the official Qwen-Image-2.1 Prompt Enhancer system prompts.

WHY A SCRIPT AND NOT A HAND COPY
  These are the vendor's own system prompts, and the answer contract they produce is part of what the fine-tuned PE
  checkpoints were trained against. A re-typed, re-wrapped or "tidied" copy is a PARAPHRASE: it reviews as identical
  and quietly changes what the model emits. So the files are downloaded at a PINNED COMMIT and verified against the
  hashes recorded in prompts/manifest.json.

  This matters in practice: a web page or a text-extraction tool MAY re-wrap the text, and that alone is enough to
  break fidelity while looking correct. Take the bytes.

  The two prompts are NOT interchangeable, and there is no merged prompt.

USAGE
  # fetch (only replaces a file when the downloaded bytes match the recorded hash)
  powershell -ExecutionPolicy RemoteSigned -File helpers/qwen-prompt-enhancer/fetch-official-prompts.ps1

  # check the files already on disk, no network
  powershell -ExecutionPolicy RemoteSigned -File helpers/qwen-prompt-enhancer/fetch-official-prompts.ps1 -VerifyOnly
#>
param(
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'

$assetDir = Join-Path $PSScriptRoot 'prompts'
$manifestPath = Join-Path $assetDir 'manifest.json'

if (-not (Test-Path $manifestPath)) {
    Write-Error "Missing manifest: $manifestPath"
    exit 1
}

$manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
$commit = $manifest.source.commit
$template = $manifest.source.rawUrlTemplate

function Get-Sha256([string]$path) {
    (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$failures = @()

foreach ($entry in $manifest.files) {

    $target = Join-Path $assetDir $entry.file
    $expected = $entry.sha256.ToLowerInvariant()

    if ($VerifyOnly) {
        if (-not (Test-Path $target)) {
            $failures += "$($entry.file): missing"
            continue
        }

        $actual = Get-Sha256 $target
        if ($actual -ne $expected) {
            $failures += "$($entry.file): hash mismatch (expected $expected, got $actual)"
        }
        else {
            Write-Host "OK   $($entry.file)  $actual"
        }

        continue
    }

    $url = $template.Replace('{commit}', $commit).Replace('{file}', $entry.file)
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ("b135-pe-" + [Guid]::NewGuid().ToString('N') + ".txt")

    try {
        Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing

        $actual = Get-Sha256 $temp

        if ($actual -ne $expected) {
            # Never replace a verified asset with unverified bytes.
            $failures += "$($entry.file): downloaded bytes do not match the recorded hash (expected $expected, got $actual) from $url"
            continue
        }

        Copy-Item -Path $temp -Destination $target -Force
        Write-Host "OK   $($entry.file)  $actual"
    }
    finally {
        if (Test-Path $temp) { Remove-Item -Path $temp -Force }
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Prompt asset verification FAILED:`n  " + ($failures -join "`n  "))
    exit 1
}

Write-Host "All Prompt Enhancer assets verified against commit $commit."
