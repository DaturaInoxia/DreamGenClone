<#
.SYNOPSIS
  Dual-base sex slideshow: a 20-step edit sequence using TWO base images with different character orientations.

.DESCRIPTION
  This harness extends the sex-slideshow concept to support arbitrary locations where Becky needs to face
  away from the camera (step 12+). Instead of trying to Qwen-edit a pose flip (which risks identity drift),
  we generate TWO T2I bases upfront:
  
  - Base A: Dean (right profile) + Becky (left profile) facing each other — used for steps 1–11
  - Base B: Dean (right profile) + Becky (right profile) facing away — used for steps 12–20
  
  Each step edits from its respective base via -FromBase mode. No frame is ever derived from another edited
  frame, eliminating the chained-edit feedback loop.

  This is the production pattern for the app's scene image pipeline when locations are arbitrary assets.

  Output: <harness>/runs/<RunId>/ as stepNN-<slug>.png plus manifest.json and a slideshow contact sheet.
  Run from the repo root. PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Prefix,
    # --- DUAL BASES ---
    [Parameter(Mandatory = $true)][string]$BaseA,
    [Parameter(Mandatory = $true)][string]$BaseB,
    # --- EDITOR CONFIG ---
    [string]$Checkpoint = 'Qwen-Rapid-AIO-NSFW-v23.safetensors',
    [string]$LoraName = 'QwenEdit2511_AllIncludedGay_v2.safetensors',
    [double]$LoraStrength = 0.8,
    [string]$RunId = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    [long]$Seed = 6601,
    [int]$Width = 1216,
    [int]$Height = 832,
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    [string]$EditRunner = 'helpers/local-comfyui-host/run-local-aio-edit-proof.ps1',
    [string]$SheetBuilder = 'specs/image-generator-tests/anatomy-edit-matrix/make-contact-sheet.py',
    # Sampling passthrough. Left at 0/-1 the app-faithful defaults are used (8 steps, CFG 1).
    [int]$Steps = 0,
    [double]$Cfg = -1,
    [string]$Sampler = '',
    [string]$Scheduler = '',
    [double]$Denoise = -1,
    # Run only steps 1..N. Lets a chain be A/B-tested without paying for all 20.
    [int]$MaxStep = 0,
    # Break the chain's colour-drift feedback loop.
    [switch]$StabilizeColors,
    [string]$Stabilizer = 'specs/image-generator-tests/sex-slideshow/stabilize-color.py',
    [switch]$SkipSheet
)

$ErrorActionPreference = 'Stop'

$harnessRoot = $PSScriptRoot
$OutDir = Join-Path $harnessRoot ('runs/' + $RunId)

foreach ($path in @($EditRunner)) {
    if (-not (Test-Path $path)) { throw "Not found: $path (run this from the repo root)" }
}
if (-not (Test-Path $BaseA)) { throw "Base A not found: $BaseA" }
if (-not (Test-Path $BaseB)) { throw "Base B not found: $BaseB" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# --- the 20 steps -------------------------------------------------------------------------------
# Kind 'edit' = instruction applied to the previous step.
# The harness selects which base to use based on step number:
#   Steps 1-11: Base A (facing-each-other orientation)
#   Steps 12-20: Base B (facing-away orientation)
$both = 'correct male and female anatomy, natural integration with both bodies'
# NOTE: named stepList, NOT $steps — PowerShell variable names are case-INSENSITIVE
$stepList = @(
    @{ N = 1;  Slug = 'man-woman-facing-each-other'; Kind = 'edit'
       Prompt = 'photorealistic studio photograph of a man and a woman standing side by side facing each other, both fully clothed in fitted dark gray short-sleeved t-shirts and blue jeans, barefoot, full body, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 2;  Slug = 'facing-each-other'; Kind = 'edit'
       Prompt = 'the man and the woman stand side by side facing each other, both fully clothed in fitted dark gray short-sleeved t-shirts and blue jeans, barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 3;  Slug = 'woman-on-knees'; Kind = 'edit'
       Prompt = 'the man stands beside the woman who is on her knees on the floor facing him, she stays fully clothed in fitted dark gray short-sleeved t-shirt and blue jeans, he stays fully clothed in fitted dark gray short-sleeved t-shirt and blue jeans, barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 4;  Slug = 'man-soft-penis'; Kind = 'edit'
       Prompt = 'the man stands beside the woman who is on her knees facing him, the man unzips his jeans and pulls the front down revealing his soft flaccid penis, correct male anatomy, natural integration with his body, the woman stays fully clothed in fitted dark gray short-sleeved t-shirt and blue jeans, barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 5;  Slug = 'woman-hand-erect'; Kind = 'edit'
       Prompt = 'the man stands beside the woman who is on her knees facing him, the woman reaches out and holds the man''s erect penis in her hand, his penis is now erect, correct male anatomy, natural integration with his body, the woman stays fully clothed in fitted dark gray short-sleeved t-shirt and blue jeans, barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 6;  Slug = 'woman-positions-mouth'; Kind = 'edit'
       Prompt = 'the woman kneels in front of the man bringing her face to his erect penis to take it into her mouth, correct male and female anatomy, natural integration with both bodies, the man stays clothed in fitted dark gray short-sleeved t-shirt and blue jeans, barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 7;  Slug = 'tip-in-mouth'; Kind = 'edit'
       Prompt = 'the woman kneels in front of the man taking the tip of his erect penis into her mouth, correct male and female anatomy, natural integration with both bodies, the man stays clothed in fitted dark gray short-sleeved t-shirt and blue jeans, barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 8;  Slug = 'half-in-mouth'; Kind = 'edit'
       Prompt = 'the woman kneels in front of the man taking half of his erect penis into her mouth, correct male and female anatomy, natural integration with both bodies, the man stays clothed in fitted dark gray short-sleeved t-shirt and blue jeans, barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 9;  Slug = 'all-in-mouth'; Kind = 'edit'
       Prompt = 'the woman kneels in front of the man taking his entire erect penis into her mouth, correct male and female anatomy, natural integration with both bodies, the man stays clothed in fitted dark gray short-sleeved t-shirt and blue jeans, barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 10; Slug = 'man-ejaculates-face'; Kind = 'edit'
       Prompt = 'the woman kneels in front of the man and he ejaculates onto her face, semen visible on her skin, correct male and female anatomy, natural integration with both bodies, the man stays clothed in fitted dark gray short-sleeved t-shirt and blue jeans, barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 11; Slug = 'woman-stands-facing'; Kind = 'edit'
       Prompt = 'the woman stands up facing the man, semen visible on the woman''s face, the man''s jeans are pulled down with his erect penis fully visible, the woman wears her fitted dark gray short-sleeved t-shirt and blue jeans, the man wears his fitted dark gray short-sleeved t-shirt, both barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 12; Slug = 'woman-turns-away'; Kind = 'edit'
       Prompt = 'the woman stands on the right side of the frame with her back toward the man who stands on the left side, the woman faces away from the camera, semen visible on the woman''s face, the man''s jeans are pulled down with his erect penis visible, the woman wears her fitted dark gray short-sleeved t-shirt and blue jeans, the man wears his fitted dark gray short-sleeved t-shirt, both barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 13; Slug = 'woman-lowers-jeans'; Kind = 'edit'
       Prompt = 'the woman stands facing away from the camera on the right side of the frame, her jeans pulled down to her thighs exposing her bare buttocks, her back and buttocks visible to the camera, semen visible on the side of her face seen in profile, the man stands behind her to the left with his jeans pulled down and erect penis visible, the woman wears her fitted dark gray short-sleeved t-shirt, the man wears his fitted dark gray short-sleeved t-shirt, both barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 14; Slug = 'woman-bends-over'; Kind = 'edit'
       Prompt = 'the woman bends forward at the waist on the right side of the frame facing away from the camera, her bare buttocks presented toward the man, her jeans pulled down to her thighs, her t-shirt still on, semen visible on the side of her face in profile, the man stands directly behind her with his jeans pulled down and his erect penis visible, both wearing their fitted dark gray short-sleeved t-shirts, both barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 15; Slug = 'man-positions-entry'; Kind = 'edit'
       Prompt = 'the woman bends forward at the waist facing away from the camera, the man stands directly behind her with his erect penis positioned at the entrance to her vagina from behind, her jeans pulled down to her thighs, his jeans pulled down, semen visible on the side of her face in profile, both wearing their fitted dark gray short-sleeved t-shirts, both barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 16; Slug = 'tip-penetrates'; Kind = 'edit'
       Prompt = 'the woman bends forward at the waist facing away from the camera, the man behind her, the tip of his erect penis penetrating her vagina from behind, her jeans pulled down to her thighs, his jeans pulled down, semen visible on the side of her face in profile, both wearing their fitted dark gray short-sleeved t-shirts, both barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 17; Slug = 'half-penetrates'; Kind = 'edit'
       Prompt = 'the woman bends forward at the waist facing away from the camera, the man behind her, half of his erect penis inside her vagina penetrating from behind, her jeans pulled down to her thighs, his jeans pulled down, semen visible on the side of her face in profile, both wearing their fitted dark gray short-sleeved t-shirts, both barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 18; Slug = 'fully-penetrates'; Kind = 'edit'
       Prompt = 'the woman bends forward at the waist facing away from the camera, the man behind her, his entire erect penis fully inside her vagina penetrating from behind, her jeans pulled down to her thighs, his jeans pulled down, semen visible on the side of her face in profile, both wearing their fitted dark gray short-sleeved t-shirts, both barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 19; Slug = 'man-withdraws'; Kind = 'edit'
       Prompt = 'the woman bends forward at the waist facing away from the camera, the man behind her has withdrawn, his erect penis pulled out of her vagina and visible between them, her jeans pulled down to her thighs, his jeans pulled down, semen visible on the side of her face in profile, both wearing their fitted dark gray short-sleeved t-shirts, both barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 20; Slug = 'man-ejaculates'; Kind = 'edit'
       Prompt = 'the woman bends forward at the waist facing away from the camera, the man behind her ejaculates onto her bare buttocks and lower back, semen visible on her buttocks and on the side of her face in profile, her jeans pulled down to her thighs, her t-shirt still on, his jeans pulled down, both wearing their fitted dark gray short-sleeved t-shirts, both barefoot, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' }
)

if ($MaxStep -gt 0) { $stepList = @($stepList | Where-Object { $_.N -le $MaxStep }) }

'================================================================================'
"DUAL-BASE SEX SLIDESHOW  config=$Prefix"
"checkpoint : $Checkpoint"
"lora       : $(if ($LoraName) { "$LoraName @ $LoraStrength" } else { '(none)' })"
"runId      : $RunId"
"output     : $OutDir"
"base A     : $BaseA"
"base B     : $BaseB"
"seed       : $Seed   canvas: ${Width}x${Height}   steps: $($stepList.Count)"
"sampling   : $(if ($Steps -gt 0) { "$Steps steps" } else { 'app-default 8 steps' }) / CFG $(if ($Cfg -ge 0) { $Cfg } else { 'app-default 1' })$(if ($Sampler) { " / $Sampler" })$(if ($Scheduler) { "/$Scheduler" })"
'================================================================================'

$manifest = @()
$previous = $null
$failedAt = $null

foreach ($step in $stepList) {
    $label = '{0:d2}-{1}' -f $step.N, $step.Slug
    $target = Join-Path $OutDir ("step$label.png")

    # Select base: steps 1-11 use Base A, steps 12+ use Base B
    $sourceBase = if ($step.N -le 11) { $BaseA } else { $BaseB }
    $baseLabel = if ($step.N -le 11) { 'BASE-A' } else { 'BASE-B' }

    ''
    '------------------------------------------------------------------------------'
    "STEP $($step.N)/$($stepList.Count)  $($step.Slug)  [$baseLabel]"
    "instruction: $($step.Prompt)"
    '------------------------------------------------------------------------------'

    $staging = Join-Path $OutDir ('_staging-step' + $step.N)
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    try {
        $editArgs = @{
            ComfyUiUrl  = $ComfyUiUrl
            SourceImage = $sourceBase
            Instruction = $step.Prompt
            Seed        = $Seed
            Checkpoint  = $Checkpoint
            OutDir      = $staging
        }
        if ($LoraName) { $editArgs.LoraName = $LoraName; $editArgs.LoraStrength = $LoraStrength }
        if ($Steps -gt 0) { $editArgs.Steps = $Steps }
        if ($Cfg -ge 0) { $editArgs.Cfg = $Cfg }
        if ($Sampler) { $editArgs.Sampler = $Sampler }
        if ($Scheduler) { $editArgs.Scheduler = $Scheduler }
        if ($Denoise -ge 0) { $editArgs.Denoise = $Denoise }
        & $EditRunner @editArgs

        $produced = Get-ChildItem $staging -Filter *.png | Select-Object -First 1
        if (-not $produced) { throw "No image produced for step $($step.N)." }
        Copy-Item $produced.FullName $target -Force
        Remove-Item $staging -Recurse -Force

        $manifest += [ordered]@{
            step        = $step.N
            slug        = $step.Slug
            kind        = 'edit'
            sourceBase  = $baseLabel
            instruction = $step.Prompt
            checkpoint  = $Checkpoint
            lora        = $(if ($LoraName) { $LoraName } else { $null })
            loraStrength = $(if (-not $LoraName) { $null } else { $LoraStrength })
            source      = Split-Path $sourceBase -Leaf
            output      = (Split-Path $target -Leaf)
        }

        $previous = $target
        "OK  -> $target"
    }
    catch {
        $failedAt = $step.N
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
        "FAILED at step $($step.N): $($_.Exception.Message)"
        break
    }
}

$gitCommit = ''
try { $gitCommit = (git rev-parse --short HEAD 2>$null) } catch { $gitCommit = '' }

$manifestDoc = [ordered]@{
    runId        = $RunId
    configName   = $Prefix
    createdAt    = (Get-Date).ToString('o')
    checkpoint   = $Checkpoint
    lora         = $LoraName
    loraStrength = $(if ($LoraName) { $LoraStrength } else { $null })
    seed         = $Seed
    graph        = "MergedCheckpoint (app-faithful) | steps=$(if ($Steps -gt 0) { $Steps } else { 8 }) cfg=$(if ($Cfg -ge 0) { $Cfg } else { 1 }) sampler=$(if ($Sampler) { $Sampler } else { 'euler_ancestral' }) scheduler=$(if ($Scheduler) { $Scheduler } else { 'beta' })"
    baseA        = $BaseA
    baseB        = $BaseB
    gitCommit    = $gitCommit
    completed    = ($null -eq $failedAt)
    failedAtStep = $failedAt
    stepCount    = $manifest.Count
    steps        = $manifest
}
$manifestDoc | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $OutDir 'manifest.json') -Encoding UTF8

if (-not $SkipSheet) {
    $sheetFiles = Get-ChildItem $OutDir -File -Filter 'step*.png' | Sort-Object Name | Select-Object -ExpandProperty FullName
    if ($sheetFiles.Count -gt 0) {
        $sheetOut = Join-Path $OutDir 'slideshow.png'
        & .venv/Scripts/python.exe $SheetBuilder $sheetOut 5 "DUAL-BASE SLIDESHOW $Prefix | $Checkpoint | lora=$(if($LoraName){$LoraName+' @'+$LoraStrength}else{'(none)'}) | seed $Seed" @sheetFiles
    }
}

''
'=================== DUAL-BASE SLIDESHOW RUN COMPLETE ==================='
"completed  : $($null -eq $failedAt)"
"steps done : $($manifest.Count)/$($stepList.Count)"
if ($failedAt) { "FAILED AT  : step $failedAt" }
"output     : $OutDir"
