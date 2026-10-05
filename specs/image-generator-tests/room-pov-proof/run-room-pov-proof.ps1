<#
.SYNOPSIS
  Room POV-consistency proof: can Qwen-Image-2.1 (native edit + reference images) produce
  geometrically consistent views of one room?

.DESCRIPTION
  Uses the synthetic colour-coded room from `room.py` as ground truth, so every expected answer
  is computed, not judged. Walls: north=RED+shelf, east=BLUE+table, south=GREEN+picture,
  west=YELLOW+window, centre=a wooden crate.

  Each arm edits from a source view and is scored against the exact ground-truth render of the
  view it was asked for, by `score.py`.

  Reference sets NEVER contain the target view - otherwise the arm would be copying, not inferring.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File specs/image-generator-tests/room-pov-proof/run-room-pov-proof.ps1 -Arms A
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'http://192.168.0.11:8188',
    [string]$GroundTruth = 'artifacts/tmp/room-proof/gt',
    [string]$OutDir = 'artifacts/tmp/room-proof/runs',
    [string[]]$Arms = @('A', 'B', 'C', 'D'),
    [long]$Seed = 20261004,
    [int]$Resolution = 1024
)

$ErrorActionPreference = 'Stop'

# `powershell -File` cannot bind an ARRAY parameter: it stringifies it, so `-Arms A,C` arrives as the
# single value "A,C" and every arm is filtered out. Splitting here makes the runner usable both ways.
if ($Arms.Count -eq 1 -and $Arms[0] -match ',') {
    $Arms = $Arms[0].Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
}

$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$gt = Join-Path $repo $GroundTruth
$out = Join-Path $repo $OutDir
$edit = Join-Path $repo 'helpers/local-comfyui-host/run-qwen21-native-edit.ps1'
$t2i = Join-Path $repo 'helpers/local-comfyui-host/run-qwen21-native-t2i.ps1'

if (-not (Test-Path $gt)) { throw "Ground truth not found at $gt - run room.py first." }

# name, source view, reference views (never the target), target view, instruction
# NOTE: this must NOT be called $arms - PowerShell variable names are case-insensitive, so a local
# `$arms` silently overwrites the `$Arms` parameter and every arm is then filtered out.
$armDefs = [ordered]@{
    'A' = @{
        Title = 'ONE reference -> the OPPOSITE view'
        Source = 'S_to_N'; Refs = @(); Target = 'N_to_S'
        Instruction = 'The camera has moved to the opposite end of the room and now looks back the way it came. Show the same room from that end: the far wall straight ahead, the wooden crate in the near foreground.'
    }
    'B' = @{
        Title = 'THREE wall views -> the view from the far end'
        Source = 'S_to_N'; Refs = @('S_to_E', 'S_to_W'); Target = 'N_to_S'
        Instruction = 'The camera is at the far end of the room looking back toward the near end. Using the other wall views provided, show the room from there: the far wall straight ahead, the crate in the near foreground.'
    }
    'C' = @{
        Title = 'CONTROL - full prose description of the room, no references'
        Source = $null; Refs = @(); Target = 'N_to_S'
        Instruction = 'A flat matte colour-blocked room interior, no people. Seen from one end looking along the room: the far wall is a solid RED panel with a dark brown wall shelf mounted on it; the left wall is solid BLUE with a brown table against it; the right wall is solid YELLOW with a tall pale window; the wall behind the camera is solid GREEN with a framed picture. A plain light ceiling, a plain grey floor, and a wooden crate sitting in the middle of the floor.'
    }
    'D' = @{
        Title = 'ALL FOUR wall views -> a corner view (everything needed is in the references)'
        Source = 'S_to_N'; Refs = @('N_to_S', 'S_to_E', 'S_to_W'); Target = 'C_SW'
        Instruction = 'The camera is a little way back from the centre of the room, turned to face the corner where the two walls behind it meet. Show that corner view: two walls meeting at a vertical corner, the wooden crate in the near foreground.'
    }
}

foreach ($key in $armDefs.Keys) {
    if ($Arms -notcontains $key) { continue }
    $arm = $armDefs[$key]

    Write-Host ""
    Write-Host ("=" * 78)
    Write-Host "ARM $key - $($arm.Title)"
    Write-Host "  source=$($arm.Source)  refs=[$($arm.Refs -join ', ')]  target=$($arm.Target)"
    Write-Host ("=" * 78)

    $armOut = Join-Path $out $key
    New-Item -ItemType Directory -Force -Path $armOut | Out-Null

    $runArgs = @{
        ComfyUiUrl  = $ComfyUiUrl
        Seed        = $Seed
        Resolution  = $Resolution
        OutDir      = $armOut
    }
    if ($arm.Source) {
        # edit arm: source is the canvas-companion reference (image_1)
        $runArgs.Instruction = $arm.Instruction
        $runArgs.Denoise = 1.0
        $runArgs.SourceImage = (Join-Path $gt "$($arm.Source).png")
        if ($arm.Refs.Count -gt 0) {
            $runArgs.References = @($arm.Refs | ForEach-Object { Join-Path $gt "$_.png" })
        }
        & $edit @runArgs
    } else {
        # control arm: text-to-image, so the instruction is the ONLY information.
        # It must not be handed the target view, which would leak the answer.
        $runArgs.Positive = $arm.Instruction
        $runArgs.Width = $Resolution
        $runArgs.Height = $Resolution
        & $t2i @runArgs
    }

    # newest png under the arm dir is this run's output
    $produced = Get-ChildItem $armOut -Recurse -File -Filter *.png |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $produced) { Write-Warning "ARM $key produced no image"; continue }

    Write-Host ""
    Write-Host "--- scoring arm $key against ground truth '$($arm.Target)' ---"
    $python = Join-Path $repo '.venv/Scripts/python.exe'
    & $python (Join-Path $PSScriptRoot 'score.py') $gt $arm.Target $produced.FullName |
        ForEach-Object { if ($_.Trim()) { Write-Host $_ } }
}

Write-Host ""
Write-Host "Runs are under $out"
