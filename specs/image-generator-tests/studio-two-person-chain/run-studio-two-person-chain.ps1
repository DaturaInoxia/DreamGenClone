<#
.SYNOPSIS
  Two-person studio chain: a 14-edit CHAINED sequence with SAFE (non-explicit) content, used to
  reproduce the chained-edit backdrop drift and to prove the fix for it.

.DESCRIPTION
  Same STRUCTURE as the sex-slideshow harness -- two subjects, a flat studio backdrop, one edit per step,
  each step editing the PREVIOUS step's output -- but the content is family-safe, so the run itself can be
  executed by anyone (including an automated agent) and committed as evidence. The point of the harness is
  the PIPELINE behaviour, not the subject matter.

  What it proves (measured failure, 2026-09-12 sex-slideshow run 06):
    * At the app-faithful 8 steps / CFG 1 the flat studio backdrop walks monotonically toward a colour
      cast -- (17,19,21) -> (99,56,86) over 18 links -- while edge_std RISES, i.e. the damage is
      chromatic, not blur.
    * -PinBackground feeds each link a copy of the previous frame whose backdrop has been put back to
      step 1's exactly (sex-slideshow/pin-background.py). Same sampling, only the switch differs.

  A/B protocol. Run this twice with identical parameters except the switch, then compare:
    & ./run-studio-two-person-chain.ps1 -Prefix baseline -RunId <id>-baseline
    & ./run-studio-two-person-chain.ps1 -Prefix pinned   -RunId <id>-pinned -PinBackground
    .venv/Scripts/python.exe specs/image-generator-tests/sex-slideshow/measure-chain-degradation.py `
        specs/image-generator-tests/studio-two-person-chain/runs/<id>-baseline `
        specs/image-generator-tests/studio-two-person-chain/runs/<id>-pinned

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
    [string]$PythonExe = '.venv/Scripts/python.exe',
    # Sampling passthrough. Left at 0/-1 the app-faithful defaults are used (8 steps, CFG 1) -- which is
    # exactly the configuration the drift was measured at, so the baseline run reproduces it.
    [int]$Steps = 0,
    [double]$Cfg = -1,
    [string]$Sampler = '',
    [string]$Scheduler = '',
    [double]$Denoise = -1,
    # Run only steps 1..N (lets a chain be A/B-tested cheaply before paying for all 15).
    [int]$MaxStep = 0,
    # The fix under test: colour-pin each frame's backdrop back to step 1 before feeding it forward.
    [switch]$PinBackground,
    [string]$PinTool = 'specs/image-generator-tests/sex-slideshow/pin-background.py',
    # Alternative/complementary fix: mean-offset colour transfer of the fed-forward frame.
    [switch]$StabilizeColors,
    [string]$Stabilizer = 'specs/image-generator-tests/sex-slideshow/stabilize-color.py',
    # Pass a fixed LOCATION REFERENCE (image2) on every edit, so the environment is anchored by a clean
    # subject-free reference instead of only by the drifting previous frame. Mirrors the app's multi-image
    # reference support (SceneImageService location references -> EditAsync referenceImageNames).
    [switch]$LocationReference,
    [string]$LocationRefPrompt = 'photorealistic photograph of an empty cozy modern living room, a gray fabric sofa, a wooden bookshelf, potted plants and warm ambient lighting, no people, sharp focus, high detail',
    # Which environment step 1 places the subjects in. 'studio' is a flat backdrop (drift is measurable
    # by the corner-box probe); 'livingroom' is a real detailed environment (degradation is judged by eye
    # and the sharpness/colour proxies, since the corners are not a flat backdrop there).
    [ValidateSet('studio', 'livingroom')]
    [string]$Scene = 'studio',
    # The harness is only meaningful as a CHAIN, so refuse to run a stub.
    [int]$MinEdits = 12,
    [switch]$SkipSheet
)

$ErrorActionPreference = 'Stop'

$harnessRoot = $PSScriptRoot
$OutDir = Join-Path $harnessRoot ('runs/' + $RunId)

foreach ($path in @($EditRunner, $BaseRunner, $SheetBuilder, $PythonExe)) {
    if (-not (Test-Path $path)) { throw "Not found: $path (run this from the repo root)" }
}
if ($PinBackground -and -not (Test-Path $PinTool)) { throw "Not found: $PinTool" }
if ($StabilizeColors -and -not (Test-Path $Stabilizer)) { throw "Not found: $Stabilizer" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

# --- the 15 steps (14 chained edits) -------------------------------------------------------------
# SAFE CONTENT: two clothed adults, a flat studio backdrop, and pose/prop changes only. Wardrobe is
# restated every step so the chain cannot silently re-dress a subject, and every instruction is written
# as a state/action rather than "add X".
# NOTE: named stepList, NOT $steps -- PowerShell names are case-INSENSITIVE and $steps would collide
# with the [int]$Steps sampling parameter above.
$wardrobe = 'they stay fully clothed in their navy blue t-shirts and beige trousers'
$sceneBase = if ($Scene -eq 'livingroom') {
    'photorealistic photograph of a man and a woman standing side by side facing the camera in a cozy modern living room, both fully clothed in plain navy blue short-sleeved t-shirts and beige trousers, barefoot, full body, a gray fabric sofa, a wooden bookshelf, potted plants and warm ambient lighting, sharp focus, high detail'
} else {
    'photorealistic studio photograph of a man and a woman standing side by side facing the camera, both fully clothed in plain navy blue short-sleeved t-shirts and beige trousers, barefoot, full body, plain dark gray studio backdrop, soft studio lighting, sharp focus, high detail'
}
$stepList = @(
    @{ N = 1;  Slug = 'man-woman-standing'; Kind = 'base'
       Prompt = $sceneBase },

    @{ N = 2;  Slug = 'facing-each-other'; Kind = 'edit'
       Prompt = "the man and the woman turn to face each other, $wardrobe" },

    @{ N = 3;  Slug = 'handshake'; Kind = 'edit'
       Prompt = "the man and the woman shake hands, $wardrobe" },

    @{ N = 4;  Slug = 'woman-waves'; Kind = 'edit'
       Prompt = "the woman raises her right hand and waves at the camera, $wardrobe" },

    @{ N = 5;  Slug = 'man-waves'; Kind = 'edit'
       Prompt = "the man raises his right hand and waves at the camera, $wardrobe" },

    @{ N = 6;  Slug = 'woman-sits'; Kind = 'edit'
       Prompt = "the woman sits down on a plain wooden stool, $wardrobe" },

    @{ N = 7;  Slug = 'man-sits'; Kind = 'edit'
       Prompt = "the man sits down on a second plain wooden stool beside the woman, $wardrobe" },

    @{ N = 8;  Slug = 'both-stand'; Kind = 'edit'
       Prompt = "the man and the woman stand up from the stools and stand side by side, $wardrobe" },

    @{ N = 9;  Slug = 'woman-jacket-on'; Kind = 'edit'
       Prompt = 'the woman wears a light gray open jacket over her navy blue t-shirt, she stays fully clothed in her t-shirt and beige trousers, the man stays fully clothed in his navy blue t-shirt and beige trousers' },

    @{ N = 10; Slug = 'woman-jacket-off'; Kind = 'edit'
       Prompt = "the woman takes the light gray jacket off again, $wardrobe" },

    @{ N = 11; Slug = 'woman-holds-notebook'; Kind = 'edit'
       Prompt = "the woman holds a red notebook in both hands in front of her, $wardrobe" },

    @{ N = 12; Slug = 'notebook-handed-over'; Kind = 'edit'
       Prompt = "the woman hands the red notebook to the man, he holds it in both hands, $wardrobe" },

    @{ N = 13; Slug = 'man-holds-notebook'; Kind = 'edit'
       Prompt = "the man holds the red notebook under his left arm, $wardrobe" },

    @{ N = 14; Slug = 'notebook-put-down'; Kind = 'edit'
       Prompt = "the man puts the red notebook down out of frame, $wardrobe" },

    @{ N = 15; Slug = 'facing-each-other-again'; Kind = 'edit'
       Prompt = "the man and the woman turn to face each other again, $wardrobe" }
)

if ($MaxStep -gt 0) { $stepList = @($stepList | Where-Object { $_.N -le $MaxStep }) }
$editCount = @($stepList | Where-Object { $_.Kind -eq 'edit' }).Count
if ($editCount -lt $MinEdits) {
    throw "This harness only proves anything as a chain: it has $editCount edit steps but at least $MinEdits are required. Raise -MaxStep."
}

$fixLabel = if ($PinBackground) { 'PinBackground' } elseif ($StabilizeColors) { 'StabilizeColors' } else { 'none (baseline)' }

'================================================================================'
"TWO-PERSON STUDIO CHAIN  config=$Prefix"
"checkpoint : $Checkpoint"
"lora       : $(if ($LoraName) { "$LoraName @ $LoraStrength" } else { '(none)' })"
"runId      : $RunId"
"scene      : $Scene"
"location   : $(if ($LocationReference) { 'reference image (image2)' } else { 'none' })"
"output     : $OutDir"
"seed       : $Seed   canvas: ${Width}x${Height}   steps: $($stepList.Count) ($editCount chained edits)"
"sampling   : $(if ($Steps -gt 0) { "$Steps steps" } else { 'app-default 8 steps' }) / CFG $(if ($Cfg -ge 0) { $Cfg } else { 'app-default 1' })$(if ($Sampler) { " / $Sampler" })$(if ($Scheduler) { "/$Scheduler" })"
"anti-drift : $fixLabel"
'================================================================================'

$manifest = @()
$previous = $null
$referenceFrame = $null
$failedAt = $null

# --- optional location reference (image2) ------------------------------------------------
# Generate ONE clean, subject-free reference of the environment and pass it as image2 on every edit,
# so the scene is anchored by a fixed reference rather than only by the drifting previous frame.
$locationRefPath = $null
if ($LocationReference) {
    $refStaging = Join-Path $OutDir '_staging-locationref'
    if (Test-Path $refStaging) { Remove-Item $refStaging -Recurse -Force }
    New-Item -ItemType Directory -Path $refStaging -Force | Out-Null
    & $BaseRunner -ComfyUiUrl $ComfyUiUrl -Checkpoint $BaseCheckpoint -Positive $LocationRefPrompt `
        -Negative '' -Seed $Seed -Width $Width -Height $Height -OutDir $refStaging -Prefix "locref$Prefix"
    $refProduced = Get-ChildItem $refStaging -Filter *.png | Select-Object -First 1
    if (-not $refProduced) { throw "No location reference produced." }
    $locationRefPath = Join-Path $OutDir 'location-reference.png'
    Copy-Item $refProduced.FullName $locationRefPath -Force
    Remove-Item $refStaging -Recurse -Force
    "LOCATION REFERENCE : $locationRefPath"
}

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
            & $BaseRunner -ComfyUiUrl $ComfyUiUrl -Checkpoint $BaseCheckpoint -Positive $step.Prompt `
                -Negative '' -Seed $Seed -Width $Width -Height $Height -OutDir $staging -Prefix "chain$Prefix"
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
            if ($locationRefPath) { $editArgs.AnchorImage = $locationRefPath }
            & $EditRunner @editArgs
        }

        $produced = Get-ChildItem $staging -Filter *.png | Select-Object -First 1
        if (-not $produced) { throw "No image produced for step $($step.N)." }
        Copy-Item $produced.FullName $target -Force
        Remove-Item $staging -Recurse -Force

        $manifest += [ordered]@{
            step         = $step.N
            slug         = $step.Slug
            kind         = $step.Kind
            instruction  = $step.Prompt
            checkpoint   = $(if ($step.Kind -eq 'base') { $BaseCheckpoint } else { $Checkpoint })
            lora         = $(if ($step.Kind -eq 'base') { $null } else { $LoraName })
            loraStrength = $(if ($step.Kind -eq 'base' -or -not $LoraName) { $null } else { $LoraStrength })
            source       = $(if ($step.Kind -eq 'base') { '(text-to-image)' } else { Split-Path $previous -Leaf })
            anchor       = $(if ($locationRefPath -and $step.Kind -ne 'base') { 'location-reference.png' } else { $null })
            output       = (Split-Path $target -Leaf)
        }

        # Feed the NEXT link a corrected copy. Correcting the SOURCE rather than the saved frame means the
        # model's own output for the next link is already clean, so the saved frames improve too -- and the
        # measurement stays a measurement of the MODEL, not of the correction.
        if ($step.N -eq 1) { $referenceFrame = $target }

        if ($PinBackground -and $step.N -gt 1 -and $referenceFrame) {
            $chainDir = Join-Path $OutDir '_chain'
            if (-not (Test-Path $chainDir)) { New-Item -ItemType Directory -Path $chainDir -Force | Out-Null }
            $corrected = Join-Path $chainDir ('step{0:d2}-pinned.png' -f $step.N)
            & $PythonExe $PinTool $referenceFrame $target $corrected | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "pin-background.py failed on step $($step.N) (exit $LASTEXITCODE)." }
            $previous = $corrected
            "OK  -> $target   [next link fed a backdrop-pinned copy]"
        }
        elseif ($StabilizeColors -and $step.N -gt 1 -and $referenceFrame) {
            $chainDir = Join-Path $OutDir '_chain'
            if (-not (Test-Path $chainDir)) { New-Item -ItemType Directory -Path $chainDir -Force | Out-Null }
            $corrected = Join-Path $chainDir ('step{0:d2}-stabilized.png' -f $step.N)
            & $PythonExe $Stabilizer $referenceFrame $target $corrected | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "stabilize-color.py failed on step $($step.N) (exit $LASTEXITCODE)." }
            $previous = $corrected
            "OK  -> $target   [next link fed a colour-stabilised copy]"
        }
        else {
            $previous = $target
            "OK  -> $target"
        }
    }
    catch {
        # A chain is only as strong as its last step, so record and stop rather than emitting a run whose
        # later frames silently descend from the wrong source image.
        $failedAt = $step.N
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
        "FAILED at step $($step.N): $($_.Exception.Message)"
        break
    }
}

$gitCommit = ''
try { $gitCommit = (git rev-parse --short HEAD 2>$null) } catch { $gitCommit = '' }

$manifestDoc = [ordered]@{
    runId           = $RunId
    configName      = $Prefix
    createdAt       = (Get-Date).ToString('o')
    checkpoint      = $Checkpoint
    lora            = $LoraName
    loraStrength    = $(if ($LoraName) { $LoraStrength } else { $null })
    baseCheckpoint  = $BaseCheckpoint
    scene           = $Scene
    locationReference = [bool]$LocationReference
    seed            = $Seed
    graph           = "MergedCheckpoint (app-faithful) | steps=$(if ($Steps -gt 0) { $Steps } else { 8 }) cfg=$(if ($Cfg -ge 0) { $Cfg } else { 1 }) sampler=$(if ($Sampler) { $Sampler } else { 'euler_ancestral' }) scheduler=$(if ($Scheduler) { $Scheduler } else { 'beta' })"
    chained         = $true
    pinBackground   = [bool]$PinBackground
    colorStabilized = [bool]$StabilizeColors
    editCount       = $editCount
    maxStep         = $MaxStep
    gitCommit       = $gitCommit
    completed       = ($null -eq $failedAt)
    failedAtStep    = $failedAt
    stepCount       = $manifest.Count
    steps           = $manifest
}
$manifestDoc | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $OutDir 'manifest.json') -Encoding UTF8

if (-not $SkipSheet) {
    $sheetFiles = Get-ChildItem $OutDir -File -Filter 'step*.png' | Sort-Object Name | Select-Object -ExpandProperty FullName
    if ($sheetFiles.Count -gt 0) {
        $sheetOut = Join-Path $OutDir 'slideshow.png'
        & $PythonExe $SheetBuilder $sheetOut 5 "TWO-PERSON STUDIO CHAIN $Prefix | $Checkpoint | lora=$(if($LoraName){$LoraName+' @'+$LoraStrength}else{'(none)'}) | anti-drift=$fixLabel | seed $Seed" @sheetFiles
    }
}

''
'=================== CHAIN RUN COMPLETE ==================='
"completed  : $($null -eq $failedAt)"
"steps done : $($manifest.Count)/$($stepList.Count)  ($editCount chained edits)"
if ($failedAt) { "FAILED AT  : step $failedAt" }
"anti-drift : $fixLabel"
"output     : $OutDir"
