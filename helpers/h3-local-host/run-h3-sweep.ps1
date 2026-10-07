param(
    [int]$Chunk = -1,
    [string[]]$Tag,
    [switch]$List,
    [string]$ConfigFile,
    [string]$Image = 'D:\src\DreamGenClone\DreamGenClone.Web\data\scene-images\8bc36efb-b235-485b-8675-b98ad7754e59\6c813ff4-fe3e-4897-a8bb-a65895643ac6.png',
    [string]$Log
)

# Runs H3 tuning configurations sequentially, one per selected tag.
#
# Configurations are permanent: the whole tested option set lives in
# sweep-config.json and is never pruned, so any configuration can be re-run or
# re-tested later. Select what to run by -Chunk (the batch it was first run in)
# or by -Tag (a specific configuration by name). Select nothing and it lists the
# catalog.
#
#   -List                          print every configuration and exit
#   -Chunk 7                       run the configurations first run as chunk 7
#   -Tag dual-scene-subject        re-run one configuration
#   -Tag v2-real10,dual-subj-s2    re-run several (comma-separated)
#
# A run whose output mp4 already exists is SKIPPED, so re-invoking is safe
# (delete the tag's mp4 first to force a re-run).

$ErrorActionPreference = 'Continue'

# $PSScriptRoot is not populated on every host; fall back to the invoked path.
$ScriptDir = if ($PSScriptRoot) { $PSScriptRoot }
             elseif ($PSCommandPath) { Split-Path $PSCommandPath -Parent }
             else { Split-Path $MyInvocation.MyCommand.Definition -Parent }
if (-not $ScriptDir) { $ScriptDir = Join-Path (Get-Location) 'helpers\h3-local-host' }

$repo = Split-Path (Split-Path $ScriptDir -Parent) -Parent
$harness = Join-Path $ScriptDir 'run-h3-ref2va-proof.py'
if (-not $ConfigFile) { $ConfigFile = Join-Path $ScriptDir 'sweep-config.json' }
if (-not $Log) { $Log = Join-Path $repo 'artifacts\tmp\h3-nsfw-proof\sweep.log' }
$outRoot = Join-Path $repo 'artifacts\tmp\h3-nsfw-proof'

if (-not (Test-Path $ConfigFile)) { throw "Sweep config not found: $ConfigFile" }
# Windows PowerShell's ConvertFrom-Json emits a JSON array as ONE pipeline object, so
# @(...) around it yields a 1-element array. Enumerate it to get real elements.
$all = @((Get-Content $ConfigFile -Raw | ConvertFrom-Json) | ForEach-Object { $_ })

# An empty selection is a request for the catalog, not for "run everything".
if ($List -or (-not $Tag -and $Chunk -lt 0)) {
    Write-Output "H3 configuration catalog ($($all.Count) configurations in $ConfigFile)"
    Write-Output ''
    $all |
        Sort-Object chunk, tag |
        ForEach-Object {
            $state = if ($_.enabled) { 'enabled ' } else { 'disabled' }
            [pscustomobject]@{
                Tag     = $_.tag
                Chunk   = $_.chunk
                State   = $state
                Loras   = ($_.loras -join ' + ')
                Refs    = 1 + [int][bool]$_.image2 + [int][bool]$_.image3
                Steps   = $_.steps
                Frames  = $_.length
                Seed    = $_.seed
                Run     = [bool](Test-Path (Join-Path $outRoot "$($_.tag)\$($_.tag)_00001_.mp4"))
            }
        } |
        Format-Table -AutoSize
    Write-Output "Catalog is the source of truth; configurations are kept whether or not they were the best result."
    return
}

$runs = @($all | Where-Object { $_.enabled })
if ($Tag) {
    $wanted = $Tag | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    $unknown = @($wanted | Where-Object { $_ -notin $all.tag })
    if ($unknown.Count -gt 0) { throw "Unknown tag(s): $($unknown -join ', '). Use -List to see the catalog." }
    $runs = @($runs | Where-Object { $_.tag -in $wanted })
}
elseif ($Chunk -ge 0) {
    $runs = @($runs | Where-Object { $_.chunk -eq $Chunk })
}

if ($runs.Count -eq 0) { Write-Output 'Nothing selected.'; return }

function Write-Log($message) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message
    Write-Output $line
    Add-Content -Path $Log -Value $line
}

if (-not (Test-Path $Image)) { throw "Reference image not found: $Image" }

Write-Log "Selected $($runs.Count) configuration(s): $($runs.tag -join ', ')"

foreach ($run in $runs) {
    $outMp4 = Join-Path $outRoot "$($run.tag)\$($run.tag)_00001_.mp4"
    if (Test-Path $outMp4) { Write-Log "SKIP $($run.tag) (output already exists; delete it to force a re-run)"; continue }

    $runImage = if ($run.image) { Join-Path $repo $run.image } else { $Image }
    $argv = @($harness, '--image', $runImage, '--tag', $run.tag,
              '--width', "$($run.width)", '--height', "$($run.height)",
              '--length', "$($run.length)", '--steps', "$($run.steps)",
              '--seed', "$($run.seed)", '--timeout', "$($run.timeout)")
    if ($run.prompt) { $argv += @('--prompt-file', (Join-Path $repo $run.prompt)) }
    if ($run.refSize) { $argv += @('--ref-size', $run.refSize) }
    if ($run.image2) { $argv += @('--image2', (Join-Path $repo $run.image2)) }
    if ($run.image3) { $argv += @('--image3', (Join-Path $repo $run.image3)) }
    foreach ($lora in $run.loras) { $argv += @('--lora-spec', $lora) }

    # H3's native audio is ~12 dB below normal delivery level, so every run also
    # gets a normalized copy beside the raw render (needs the venv's ffmpeg).
    $venvPython = Join-Path $repo '.venv\Scripts\python.exe'
    $python = if (Test-Path $venvPython) { $venvPython } else { 'python' }
    if (Test-Path $venvPython) { $argv += @('--normalize-lufs', '-16') }

    Write-Log "START $($run.tag)  loras=[$($run.loras -join ', ')] steps=$($run.steps) seed=$($run.seed)"
    Push-Location $repo
    try {
        & $python @argv 2>&1 | Tee-Object -FilePath (Join-Path $outRoot "$($run.tag).log")
    }
    finally { Pop-Location }
    Write-Log "END $($run.tag) exit=$LASTEXITCODE"
}

Write-Log 'Selection finished.'
