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
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
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
    # it still shows the step-1 pose). -AnchorBackground passes only the subject-free studio backdrop, built once from step 1 -- the environment is what
    # drifts, so anchoring it need not carry any pose information.
    [switch]$AnchorFrame,
    [switch]$AnchorBackground,
    [string]$BackgroundRefTool = 'specs/image-generator-tests/sex-slideshow/make-background-reference.py',
    # IDENTITY CONDITIONING. -Identity enables regional IP-Adapter on the base render (step 1). When
    # used without -DeanRef/-BeckyRef, refs are auto-resolved from specs/image-generator-tests/refs/versions.json
    # (dean v8 → profr, becky v5 → profl — matching the side-profile "facing each other" base pose).
    [switch]$Identity,
    [string]$DeanRef,
    [string]$BeckyRef,
    [string]$DeanMask = 'specs/image-generator-tests/identity-two-character/masks/c6_left.png',
    [string]$BeckyMask = 'specs/image-generator-tests/identity-two-character/masks/c6_right.png',
    [string]$IdentityBaseRunner = 'specs/image-generator-tests/sex-slideshow/make-identity-base.ps1',
    # FROM-BASE mode: every edit uses the step-1 base image as its source instead of the previous
    # step's output. Each prompt must fully describe the target state (they do). This removes the
    # chained-edit feedback loop entirely: no frame is ever derived from another edited frame.
    [switch]$FromBase,
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
    # Base is deliberately the side-profile "facing each other" pose so the multiangle identity pack
    # can directly condition on the curated profile refs (dean_profl/profr, becky_profl/profr) instead
    # of forcing the model to synthesize profile faces from a front-facing base.
    @{ N = 1;  Slug = 'man-woman-facing-each-other'; Kind = 'base'
       Prompt = 'photorealistic studio photograph of a man and a woman standing side by side facing each other, both fully clothed in fitted dark gray short-sleeved t-shirts and blue jeans, barefoot, full body, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail' },

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
$baseImage = $null
$referenceFrame = $null
$failedAt = $null

foreach ($step in $stepList) {
    $label = '{0:d2}-{1}' -f $step.N, $step.Slug
    $target = Join-Path $OutDir ("step$label.png")

    ''
    '------------------------------------------------------------------------------'
    "STEP $($step.N)/$($stepList.Count)  $($step.Slug)  [$($step.Kind)]"
    "instruction: $($step.Prompt)"
    '------------------------------------------------------------------------------'

    $staging = Join-Path $OutDir ('_staging-step' + $step.N)
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    try {
        if ($step.Kind -eq 'base') {
            if ($Identity) {
                # Identity-conditioned base: use the regional IP-Adapter runner (Dean left, Becky right)
                & $IdentityBaseRunner `
                    -ComfyUiUrl $ComfyUiUrl `
                    -DeanRef $DeanRef `
                    -BeckyRef $BeckyRef `
                    -DeanMask $DeanMask `
                    -BeckyMask $BeckyMask `
                    -Prompt $step.Prompt `
                    -Seed $Seed `
                    -Width $Width `
                    -Height $Height `
                    -Checkpoint $BaseCheckpoint `
                    -OutDir $staging `
                    -Prefix "slide$Prefix"
            } else {
                & $BaseRunner -ComfyUiUrl $ComfyUiUrl -Checkpoint $BaseCheckpoint -Positive $step.Prompt `
                    -Negative '' -Seed $Seed -Width $Width -Height $Height -OutDir $staging -Prefix "slide$Prefix"
            }
        } else {
            if (-not $previous) { throw "Step $($step.N) needs a previous step but none succeeded." }
            $editArgs = @{
                ComfyUiUrl  = $ComfyUiUrl
                SourceImage = if ($FromBase) { $baseImage } else { $previous }
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

        if ($step.Kind -eq 'base') { $baseImage = $target }

        $manifest += [ordered]@{
            step        = $step.N
            slug        = $step.Slug
            kind        = $step.Kind
            instruction = $step.Prompt
            checkpoint  = $(if ($step.Kind -eq 'base') { $BaseCheckpoint } else { $Checkpoint })
            lora        = $(if ($step.Kind -eq 'base') { $null } else { $LoraName })
            loraStrength = $(if ($step.Kind -eq 'base' -or -not $LoraName) { $null } else { $LoraStrength })
            source      = $(if ($step.Kind -eq 'base') { '(text-to-image)' } elseif ($FromBase) { 'step01 (from-base)' } else { Split-Path $previous -Leaf })
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
