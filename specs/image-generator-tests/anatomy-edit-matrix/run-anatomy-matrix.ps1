<#
.SYNOPSIS
  Runs the anatomy edit matrix and writes every result into ONE flat folder for side-by-side review.

.DESCRIPTION
  Cells are grouped into sets so a set can be re-run without redoing the others:
    phrasing - the ORIGINAL 6-way comparison: {v23, v23+LoRA@0.8, v23+LoRA@1.0, Remix} x {add, clothing}
               phrasing. Each cell pins its OWN checkpoint/LoRA, so this set spans configs.
    core     - male/female standing, {soft,erect} x {far,close}
    mast     - female lying with legs spread (clothed), {unaroused,aroused} x {far,close}
    closeup  - close-up actions on the lying base: spread lips / insert finger / rub clit
    rubretry - rephrased rub-clit variants (the first attempt produced a fluid artifact on the fingers)

  Everything else is pinned: same seed, same backend graph, same sampler settings.

  RUN-SCOPED OUTPUT: every invocation writes to its OWN folder, `<harness>/runs/<RunId>/` where RunId
  defaults to a timestamp. Runs never overwrite each other, so two runs can be DIFFED to see what a change
  actually did. The base images are copied into the run folder, making each run self-contained, and a
  `manifest.json` records the seed, git commit, and every cell's config + instruction.

  Run from the repo root. PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [ValidateSet('phrasing', 'core', 'mast', 'closeup', 'rubretry', 'all')]
    [string]$Set = 'all',
    [string]$ComfyUiUrl = 'http://192.168.0.16:8188',
    # Each run writes to its own folder so runs can be compared instead of overwriting each other.
    [string]$RunId = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    [string]$OutDir = '',
    # Where the base images live (build-bases.ps1 output).
    [string]$BasesDir = '',
    [long]$Seed = 6601,
    [string]$Checkpoint = 'Qwen-Rapid-AIO-NSFW-v23.safetensors',
    [string]$LoraName = 'QwenEdit2511_AllIncludedGay_v2.safetensors',
    [double]$LoraStrength = 0.8,
    [string]$Prefix = 'v23lora08',
    [string]$RunnerPath = 'helpers/local-comfyui-host/run-local-aio-edit-proof.ps1'
)

$ErrorActionPreference = 'Stop'

$harnessRoot = $PSScriptRoot
if (-not $BasesDir) { $BasesDir = Join-Path $harnessRoot 'images' }
if (-not $OutDir) { $OutDir = Join-Path $harnessRoot ('runs/' + $RunId) }

if (-not (Test-Path $RunnerPath)) { throw "Runner not found: $RunnerPath (run from the repo root)" }
if (-not (Test-Path $BasesDir)) { throw "Base image folder not found: $BasesDir - run build-bases.ps1 first" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# Copy the bases in so the run folder is self-contained and reviewable on its own.
foreach ($baseFile in (Get-ChildItem $BasesDir -Filter 'base-*.png' -File)) {
    Copy-Item $baseFile.FullName (Join-Path $OutDir $baseFile.Name) -Force
}

# --- instructions --------------------------------------------------------------------------------
$maleSoft = 'unzip his jeans and pull the front open so his soft flaccid penis is fully visible, correct male anatomy, natural integration with his body'
$maleErect = 'unzip his jeans and pull the front open so his erect penis is fully visible, correct male anatomy, natural integration with his body'
$femaleUnaroused = 'unbutton her jeans and pull the front open so her vulva is visible, relaxed and unaroused, correct female anatomy, natural integration with her body'
$femaleAroused = 'unbutton her jeans and pull the front open so her aroused vulva is visible, swollen, wet and glistening, correct female anatomy, natural integration with her body'
$lyingUnaroused = 'pull her jeans and underwear completely off so her bare vulva is fully visible, relaxed and unaroused, correct female anatomy, natural integration with her body'
$lyingAroused = 'pull her jeans and underwear completely off so her bare aroused vulva is fully visible, swollen, wet and glistening, correct female anatomy, natural integration with her body'
$spreadLips = 'her vulva is fully exposed with the outer lips and inner lips spread wide apart, correct female anatomy, natural integration with her body'
$insertFinger = 'she inserts her middle finger into her vagina, correct female anatomy, natural integration with her body'
$rubClit = 'she rubs her clitoris with two fingertips, aroused and wet, correct female anatomy, natural integration with her body'
# rub-clit RETRIES. The first attempt rendered a "white substance on the fingers", so v2 drops the
# "aroused and wet" wording (the most likely cause of a fluid being invented) and v3 removes any fluid
# implication entirely and pins the gesture. Both are ADDED, never substituted, so the original cell
# stays reproducible for comparison.
$rubClitV2 = 'she rubs her clitoris with two fingertips, correct female anatomy, natural integration with her body'
$rubClitV3 = 'her hand rests between her legs with two fingers circling her clitoris, no other objects present, correct female anatomy, natural integration with her body'

# The ORIGINAL "add" phrasing. Kept deliberately: the operator's best-accepted result (v23 + LoRA @0.8)
# used THIS form, which contradicts the blanket "always use the clothing-action form" rule. Running both
# forms on the same base is what settles which phrasing the app's prompt compiler should emit.
$maleAddPhrasing = 'add a male erect penis to the man standing using correct anatomy'
$lora = 'QwenEdit2511_AllIncludedGay_v2.safetensors'

# --- cells --------------------------------------------------------------------------------------
$cells = @(
    @{ Set = 'core';    Name = 'male-far-soft';          Base = 'base-male-far.png';         Instruction = $maleSoft },
    @{ Set = 'core';    Name = 'male-far-erect';         Base = 'base-male-far.png';         Instruction = $maleErect },
    @{ Set = 'core';    Name = 'male-close-soft';        Base = 'base-male-close.png';       Instruction = $maleSoft },
    @{ Set = 'core';    Name = 'male-close-erect';       Base = 'base-male-close.png';       Instruction = $maleErect },
    @{ Set = 'core';    Name = 'female-far-unaroused';   Base = 'base-female-far.png';       Instruction = $femaleUnaroused },
    @{ Set = 'core';    Name = 'female-far-aroused';     Base = 'base-female-far.png';       Instruction = $femaleAroused },
    @{ Set = 'core';    Name = 'female-close-unaroused'; Base = 'base-female-close.png';     Instruction = $femaleUnaroused },
    @{ Set = 'core';    Name = 'female-close-aroused';   Base = 'base-female-close.png';     Instruction = $femaleAroused },

    @{ Set = 'mast';    Name = 'female-lying-far-unaroused';   Base = 'base-female-lying-far.png';   Instruction = $lyingUnaroused },
    @{ Set = 'mast';    Name = 'female-lying-far-aroused';     Base = 'base-female-lying-far.png';   Instruction = $lyingAroused },
    @{ Set = 'mast';    Name = 'female-lying-close-unaroused'; Base = 'base-female-lying-close.png'; Instruction = $lyingUnaroused },
    @{ Set = 'mast';    Name = 'female-lying-close-aroused';   Base = 'base-female-lying-close.png'; Instruction = $lyingAroused },

    @{ Set = 'closeup'; Name = 'female-close-spread-lips';     Base = 'base-female-lying-close.png'; Instruction = $spreadLips },
    @{ Set = 'closeup'; Name = 'female-close-insert-finger';   Base = 'base-female-lying-close.png'; Instruction = $insertFinger },
    @{ Set = 'closeup'; Name = 'female-close-rub-clit';        Base = 'base-female-lying-close.png'; Instruction = $rubClit },

    @{ Set = 'rubretry'; Name = 'female-close-rub-clit-v2';    Base = 'base-female-lying-close.png'; Instruction = $rubClitV2 },
    @{ Set = 'rubretry'; Name = 'female-close-rub-clit-v3';    Base = 'base-female-lying-close.png'; Instruction = $rubClitV3 },

    # Config comparison. Each cell pins its own checkpoint+LoRA, so -Set phrasing ignores the pass-level
    # -Checkpoint/-LoraName. No-LoRA cells were DROPPED (operator: only LoRA configs are worth comparing),
    # and Remix+LoRA was ADDED so the two checkpoints can be judged head-to-head WITH the anatomy prior.
    # Both genders are included: the checkpoint choice must hold for male and female cells alike.
    @{ Set = 'phrasing'; Name = 'v23-lora08-add';        Base = 'base-male-far.png';   Instruction = $maleAddPhrasing; Checkpoint = 'Qwen-Rapid-AIO-NSFW-v23.safetensors'; LoraName = $lora; LoraStrength = 0.8 },
    @{ Set = 'phrasing'; Name = 'v23-lora10-add';        Base = 'base-male-far.png';   Instruction = $maleAddPhrasing; Checkpoint = 'Qwen-Rapid-AIO-NSFW-v23.safetensors'; LoraName = $lora; LoraStrength = 1.0 },
    @{ Set = 'phrasing'; Name = 'v23-lora08-clothing';   Base = 'base-male-far.png';   Instruction = $maleErect;       Checkpoint = 'Qwen-Rapid-AIO-NSFW-v23.safetensors'; LoraName = $lora; LoraStrength = 0.8 },
    @{ Set = 'phrasing'; Name = 'remix-lora08-add';      Base = 'base-male-far.png';   Instruction = $maleAddPhrasing; Checkpoint = 'qwenImageEditRemix_aioV20.safetensors'; LoraName = $lora; LoraStrength = 0.8 },
    @{ Set = 'phrasing'; Name = 'remix-lora08-clothing'; Base = 'base-male-far.png';   Instruction = $maleErect;       Checkpoint = 'qwenImageEditRemix_aioV20.safetensors'; LoraName = $lora; LoraStrength = 0.8 },
    @{ Set = 'phrasing'; Name = 'v23-lora08-female';     Base = 'base-female-far.png'; Instruction = $femaleAroused;  Checkpoint = 'Qwen-Rapid-AIO-NSFW-v23.safetensors'; LoraName = $lora; LoraStrength = 0.8 },
    @{ Set = 'phrasing'; Name = 'remix-lora08-female';   Base = 'base-female-far.png'; Instruction = $femaleAroused;  Checkpoint = 'qwenImageEditRemix_aioV20.safetensors'; LoraName = $lora; LoraStrength = 0.8 }
)

$selected = @($cells | Where-Object { $Set -eq 'all' -or $_.Set -eq $Set })
if ($selected.Count -eq 0) { throw "No cells matched -Set $Set" }

$manifest = @()
$index = 0
foreach ($cell in $selected) {
    $index++
    $source = Join-Path $BasesDir $cell.Base
    if (-not (Test-Path $source)) { throw "Missing base image: $source - run build-bases.ps1 first" }

    # A cell may pin its own config (the `phrasing` set spans configs); otherwise use the invocation's.
    $cellCheckpoint = $Checkpoint
    if ($cell.ContainsKey('Checkpoint')) { $cellCheckpoint = $cell.Checkpoint }
    $cellLora = $LoraName
    if ($cell.ContainsKey('LoraName')) { $cellLora = $cell.LoraName }
    $cellLoraStrength = $LoraStrength
    if ($cell.ContainsKey('LoraStrength')) { $cellLoraStrength = $cell.LoraStrength }
    $cellPrefix = $Prefix
    if ($cell.ContainsKey('Checkpoint')) { $cellPrefix = 'cfg' }

    $staging = Join-Path $OutDir ('_staging-' + $cellPrefix + '-' + $cell.Name)
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    '======================================================================'
    "[$index/$($selected.Count)] set=$($cell.Set)  $($cell.Name)"
    "base       : $($cell.Base)"
    "checkpoint : $cellCheckpoint"
    "lora       : $(if ($cellLora) { "$cellLora @ $cellLoraStrength" } else { '(none)' })"
    "instruction: $($cell.Instruction)"
    '----------------------------------------------------------------------'

    $runnerArgs = @{
        ComfyUiUrl  = $ComfyUiUrl
        SourceImage = $source
        Instruction = $cell.Instruction
        Seed        = $Seed
        Checkpoint  = $cellCheckpoint
        OutDir      = $staging
    }
    if ($cellLora) {
        $runnerArgs.LoraName = $cellLora
        $runnerArgs.LoraStrength = $cellLoraStrength
    }

    & $RunnerPath @runnerArgs

    $produced = Get-ChildItem $staging -Filter *.png | Select-Object -First 1
    if (-not $produced) { throw "Cell '$($cell.Name)' produced no image." }

    # Descriptive name only: cell names are unique per set, so the file name identifies the cell without
    # arbitrary sequence numbers whose meaning changes when a set is added.
    $target = Join-Path $OutDir ('{0}-{1}.png' -f $cellPrefix, $cell.Name)
    Copy-Item $produced.FullName $target -Force
    Remove-Item $staging -Recurse -Force
    "RESULT     : $target"

    $manifest += [ordered]@{
        set          = $cell.Set
        cell         = $cell.Name
        base         = $cell.Base
        checkpoint   = $cellCheckpoint
        lora         = $cellLora
        loraStrength = $(if ($cellLora) { $cellLoraStrength } else { $null })
        instruction  = $cell.Instruction
        output       = (Split-Path $target -Leaf)
    }
}

$gitCommit = ''
try { $gitCommit = (git rev-parse --short HEAD 2>$null) } catch { $gitCommit = '' }

# MERGE with any manifest already in this run folder. A complete suite spans more than one invocation
# (one pass per checkpoint/LoRA config), and each pass must NOT erase the previous pass's records.
# Cells are keyed by output file name so re-running a cell replaces its entry instead of duplicating it.
$manifestPath = Join-Path $OutDir 'manifest.json'
$existingCells = @()
$createdAt = (Get-Date).ToString('o')
if (Test-Path $manifestPath) {
    try {
        $previous = Get-Content $manifestPath -Raw | ConvertFrom-Json
        if ($previous.createdAt) { $createdAt = $previous.createdAt }
        if ($previous.cells) { $existingCells = @($previous.cells) }
    } catch {
        "WARNING: existing manifest.json was unreadable and will be replaced: $($_.Exception.Message)"
    }
}

$mergedCells = @()
foreach ($old in $existingCells) {
    if (-not ($manifest | Where-Object { $_.output -eq $old.output })) { $mergedCells += $old }
}
foreach ($new in $manifest) { $mergedCells += $new }

$manifestDoc = [ordered]@{
    runId     = $RunId
    createdAt = $createdAt
    updatedAt = (Get-Date).ToString('o')
    sets      = @($mergedCells | ForEach-Object { $_.set } | Sort-Object -Unique)
    seed      = $Seed
    graph     = 'MergedCheckpoint (app-faithful: 8 steps, CFG 1, euler_ancestral/beta, denoise 1, AuraFlow 3.1, CFGNorm 1)'
    gitCommit = $gitCommit
    cellCount = $mergedCells.Count
    cells     = $mergedCells
}
$manifestDoc | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8

''
"=================== RUN COMPLETE ==================="
"runId    : $RunId"
"set      : $Set"
"output   : $OutDir"
"manifest : $manifestPath"
Get-ChildItem $OutDir -File -Filter '*.png' | Sort-Object Name |
    Select-Object @{n = 'File'; e = { $_.Name }}, @{n = 'KB'; e = { [int]($_.Length / 1KB) }} |
    Format-Table -AutoSize | Out-String -Width 160
