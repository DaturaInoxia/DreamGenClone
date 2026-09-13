<#
.SYNOPSIS
  Sex slideshow: a 19-step CHAINED edit sequence, runnable against different editor configurations.

.DESCRIPTION
  Step 1 is a text-to-image render (the editor is an EDITOR, so it needs a starting image). Every later
  step edits the PREVIOUS STEP'S OUTPUT — the chain is the point. That makes this harness fundamentally
  different from the anatomy matrix, which edits a fixed base: here drift, clothing continuity and the
  ability to hold two subjects accumulate across 18 successive edits.

  Prompting rules applied throughout (learned from the anatomy matrix):
   - Clothing/pose ACTION phrasing only. Never "add <body part>": told to "add", the editor leaves the
     clothing alone and stamps the anatomy on top of it. Instruct what the clothing/pose DOES.
   - Male anatomy always carries "correct male anatomy, natural integration with his body" — the v23
     checkpoint alone renders a female-biased groin form, so the anatomy prior comes from the editor LoRA.
   - Clothing state is restated every step so the chain cannot silently re-dress or undress a subject.
   - Seed stays pinned across a run so two configurations are comparable step-for-step.

  Output: <harness>/runs/<RunId>/ as stepNN-<slug>.png plus manifest.json and a slideshow contact sheet.
  Run from the repo root. PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Prefix,
    [string]$Checkpoint = 'qwenImageEditRemix_aioV20.safetensors',
    [string]$LoraName = 'QwenEdit2511_AllIncludedGay_v2.safetensors',
    [double]$LoraStrength = 0.8,
    [string]$RunId = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    [long]$Seed = 6601,
    [int]$Width = 1216,
    [int]$Height = 832,
    [string]$BaseCheckpoint = 'bigLust_v16.safetensors',
    [string]$ComfyUiUrl = 'http://192.168.0.16:8188',
    [string]$EditRunner = 'helpers/local-comfyui-host/run-local-aio-edit-proof.ps1',
    [string]$BaseRunner = 'specs/image-generator-tests/anatomy-edit-matrix/make-base-image.ps1',
    [string]$SheetBuilder = 'specs/image-generator-tests/anatomy-edit-matrix/make-contact-sheet.py',
    # Sampling passthrough. Left at 0/-1 the app-faithful defaults are used (8 steps, CFG 1). Exposed so a
    # chain can be re-run at higher sampling to test the reported quality degradation across the chain:
    # 8 steps at CFG 1 gives each link very little room to resolve a clean frame before it becomes the next
    # link's reference, and errors have no pressure to correct.
    [int]$Steps = 0,
    [double]$Cfg = -1,
    [string]$Sampler = '',
    [string]$Scheduler = '',
    [double]$Denoise = -1,
    # Run only steps 1..N. Lets a chain be A/B-tested (e.g. sampling settings) without paying for all 19.
    [int]$MaxStep = 0,
    # Break the chain's colour-drift feedback loop: colour-match each frame to step 1 before it is fed
    # forward. Measured cause of the "interference" -- see stabilize-color.py for the numbers.
    [switch]$StabilizeColors,
    [string]$Stabilizer = 'specs/image-generator-tests/sex-slideshow/stabilize-color.py',
    # ANCHORING. Both forms feed an extra reference (image2) on every link, alongside the previous frame
    # (image1). -AnchorFrame passes the WHOLE step-1 frame; measured WORSE than baseline for drift because
    # it contradicts the current pose instruction (it still shows the step-1 pose). -AnchorBackground
    # passes only the subject-free studio backdrop, built once from step 1 -- the environment is what
    # drifts, so anchoring it need not carry any pose information.
    [switch]$AnchorFrame,
    [switch]$AnchorBackground,
    [string]$BackgroundRefTool = 'specs/image-generator-tests/sex-slideshow/make-background-reference.py',
    [switch]$SkipSheet
)

$ErrorActionPreference = 'Stop'

$harnessRoot = $PSScriptRoot
$OutDir = Join-Path $harnessRoot ('runs/' + $RunId)

foreach ($path in @($EditRunner, $BaseRunner, $SheetBuilder)) {
    if (-not (Test-Path $path)) { throw "Not found: $path (run this from the repo root)" }
}
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# --- the 19 steps -------------------------------------------------------------------------------
# Kind 'base' = text-to-image seed of the scene. Kind 'edit' = instruction applied to the previous step.
$both = 'correct male and female anatomy, natural integration with both bodies'
# NOTE: named stepList, NOT $steps — PowerShell variable names are case-INSENSITIVE, so a variable called
# $steps would collide with the [int]$Steps sampling parameter above and blow up on the array assignment.
$stepList = @(
    @{ N = 1;  Slug = 'man-woman-standing'; Kind = 'base'
       Prompt = 'photorealistic studio photograph of a man and a woman standing side by side facing the camera, both fully clothed in fitted dark gray short-sleeved t-shirts and blue jeans, barefoot, full body, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

    @{ N = 2;  Slug = 'facing-each-other'; Kind = 'edit'
       Prompt = 'the man and the woman turn to face each other, they stay fully clothed' },

    @{ N = 3;  Slug = 'woman-hands-and-knees'; Kind = 'edit'
       Prompt = 'the woman is down on her hands and knees on the floor facing the man, she stays fully clothed, natural integration with her body' },

    @{ N = 4;  Slug = 'man-soft-penis'; Kind = 'edit'
       Prompt = 'the man unzips his jeans and pulls the front down, his soft flaccid penis is fully visible, correct male anatomy, natural integration with his body' },

    @{ N = 5;  Slug = 'woman-hand-erect'; Kind = 'edit'
       Prompt = 'the woman reaches out and holds the man''s penis in her hand, his penis is now erect, correct male anatomy, natural integration with his body' },

    @{ N = 6;  Slug = 'woman-positions-mouth'; Kind = 'edit'
       Prompt = 'the woman kneels in front of the man and brings her face to his erect penis to take it into her mouth, ' + $both },

    @{ N = 7;  Slug = 'tip-in-mouth'; Kind = 'edit'
       Prompt = 'the woman takes the tip of the man''s erect penis into her mouth, ' + $both },

    @{ N = 8;  Slug = 'half-in-mouth'; Kind = 'edit'
       Prompt = 'the woman takes half of the man''s erect penis into her mouth, ' + $both },

    @{ N = 9;  Slug = 'all-in-mouth'; Kind = 'edit'
       Prompt = 'the woman takes the man''s entire erect penis into her mouth, ' + $both },

    @{ N = 10; Slug = 'woman-stands-facing'; Kind = 'edit'
       Prompt = 'the woman stands up and faces the man, they stay clothed as they are' },

    @{ N = 11; Slug = 'woman-turns-away'; Kind = 'edit'
       Prompt = 'the woman turns around so she faces the same direction as the man, her back toward him' },

    @{ N = 12; Slug = 'woman-lowers-jeans'; Kind = 'edit'
       Prompt = 'the woman unbuttons her jeans and pulls them down, her bare buttocks and vulva are exposed, correct female anatomy, natural integration with her body' },

    @{ N = 13; Slug = 'woman-bends-over'; Kind = 'edit'
       Prompt = 'the woman bends forward at the waist in front of the man, presenting her buttocks to him, correct female anatomy, natural integration with her body' },

    @{ N = 14; Slug = 'man-positions-entry'; Kind = 'edit'
       Prompt = 'the man steps up behind the woman and positions his erect penis at the entrance to her vagina, ' + $both },

    @{ N = 15; Slug = 'tip-penetrates'; Kind = 'edit'
       Prompt = 'the tip of the man''s erect penis is inside the woman''s vagina, ' + $both },

    @{ N = 16; Slug = 'half-penetrates'; Kind = 'edit'
       Prompt = 'half of the man''s erect penis is inside the woman''s vagina, ' + $both },

    @{ N = 17; Slug = 'fully-penetrates'; Kind = 'edit'
       Prompt = 'the man''s entire erect penis is fully inside the woman''s vagina, ' + $both },

    @{ N = 18; Slug = 'man-withdraws'; Kind = 'edit'
       Prompt = 'the man pulls his erect penis out of the woman''s vagina, ' + $both },

    @{ N = 19; Slug = 'man-ejaculates'; Kind = 'edit'
       Prompt = 'the man ejaculates onto the woman''s buttocks and lower back, semen visible on her skin, ' + $both }
)

if ($MaxStep -gt 0) { $stepList = @($stepList | Where-Object { $_.N -le $MaxStep }) }

'================================================================================'
"SEX SLIDESHOW  config=$Prefix"
"checkpoint : $Checkpoint"
"lora       : $(if ($LoraName) { "$LoraName @ $LoraStrength" } else { '(none)' })"
"runId      : $RunId"
"output     : $OutDir"
"seed       : $Seed   canvas: ${Width}x${Height}   steps: $($stepList.Count)"
"sampling   : $(if ($Steps -gt 0) { "$Steps steps" } else { 'app-default 8 steps' }) / CFG $(if ($Cfg -ge 0) { $Cfg } else { 'app-default 1' })$(if ($Sampler) { " / $Sampler" })$(if ($Scheduler) { "/$Scheduler" })"
'================================================================================'

$manifest = @()
$previous = $null
$referenceFrame = $null
$failedAt = $null

foreach ($step in $stepList) {
    $label = '{0:d2}-{1}' -f $step.N, $step.Slug
    $target = Join-Path $OutDir ("step$label.png")

    ''
    '------------------------------------------------------------------------------'
    "STEP $($step.N)/$($steps.Count)  $($step.Slug)  [$($step.Kind)]"
    "instruction: $($step.Prompt)"
    '------------------------------------------------------------------------------'

    $staging = Join-Path $OutDir ('_staging-step' + $step.N)
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    try {
        if ($step.Kind -eq 'base') {
            & $BaseRunner -ComfyUiUrl $ComfyUiUrl -Checkpoint $BaseCheckpoint -Positive $step.Prompt `
                -Negative '' -Seed $Seed -Width $Width -Height $Height -OutDir $staging -Prefix "slide$Prefix"
        } else {
            if (-not $previous) { throw "Step $($step.N) needs a previous step but none succeeded." }
            $editArgs = @{
                ComfyUiUrl  = $ComfyUiUrl
                SourceImage = $previous
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
            if ($anchorImage) { $editArgs.AnchorImage = $anchorImage }
            & $EditRunner @editArgs
        }

        $produced = Get-ChildItem $staging -Filter *.png | Select-Object -First 1
        if (-not $produced) { throw "No image produced for step $($step.N)." }
        Copy-Item $produced.FullName $target -Force
        Remove-Item $staging -Recurse -Force

        $manifest += [ordered]@{
            step        = $step.N
            slug        = $step.Slug
            kind        = $step.Kind
            instruction = $step.Prompt
            checkpoint  = $(if ($step.Kind -eq 'base') { $BaseCheckpoint } else { $Checkpoint })
            lora        = $(if ($step.Kind -eq 'base') { $null } else { $LoraName })
            loraStrength = $(if ($step.Kind -eq 'base' -or -not $LoraName) { $null } else { $LoraStrength })
            source      = $(if ($step.Kind -eq 'base') { '(text-to-image)' } else { Split-Path $previous -Leaf })
            anchor      = $(if ($AnchorFrame -and $step.Kind -ne 'base') { 'step01' } else { $null })
            output      = (Split-Path $target -Leaf)
        }

        # Feed the NEXT link a colour-stabilised copy. Stabilising the SOURCE rather than the saved frame
        # means the model's own output for the next link is already clean, so the saved frames improve too.
        if ($step.N -eq 1) {
            $referenceFrame = $target
            # Resolve the anchor ONCE from step 1, before any later link needs it.
            if ($AnchorBackground) {
                $anchorImage = Join-Path $OutDir 'anchor-background.png'
                & .venv/Scripts/python.exe $BackgroundRefTool $target $anchorImage | Out-Null
                "ANCHOR     : subject-free studio reference built from step01"
            } elseif ($AnchorFrame) {
                $anchorImage = $target
                "ANCHOR     : full step01 frame used as image2"
            }
        }
        if ($StabilizeColors -and $step.N -gt 1 -and $referenceFrame) {
            $chainDir = Join-Path $OutDir '_chain'
            if (-not (Test-Path $chainDir)) { New-Item -ItemType Directory -Path $chainDir -Force | Out-Null }
            $stabilized = Join-Path $chainDir ('step{0:d2}-stabilized.png' -f $step.N)
            & .venv/Scripts/python.exe $Stabilizer $referenceFrame $target $stabilized | Out-Null
            $previous = $stabilized
            "OK  -> $target   [next link fed a colour-stabilised copy]"
        } else {
            $previous = $target
            "OK  -> $target"
        }
    }
    catch {
        # The chain is only as strong as its last step: a failure breaks every later step, so record and stop
        # rather than emitting a run whose later images silently descend from the wrong source.
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
    baseCheckpoint = $BaseCheckpoint
    seed         = $Seed
    graph        = "MergedCheckpoint (app-faithful) | steps=$(if ($Steps -gt 0) { $Steps } else { 8 }) cfg=$(if ($Cfg -ge 0) { $Cfg } else { 1 }) sampler=$(if ($Sampler) { $Sampler } else { 'euler_ancestral' }) scheduler=$(if ($Scheduler) { $Scheduler } else { 'beta' })"
    chained      = $true
    colorStabilized = [bool]$StabilizeColors
    maxStep      = $MaxStep
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
        & .venv/Scripts/python.exe $SheetBuilder $sheetOut 5 "SEX SLIDESHOW $Prefix | $Checkpoint | lora=$(if($LoraName){$LoraName+' @'+$LoraStrength}else{'(none)'}) | seed $Seed" @sheetFiles
    }
}

''
'=================== SLIDESHOW RUN COMPLETE ==================='
"completed  : $($null -eq $failedAt)"
"steps done : $($manifest.Count)/$($stepList.Count)"
if ($failedAt) { "FAILED AT  : step $failedAt" }
"output     : $OutDir"
