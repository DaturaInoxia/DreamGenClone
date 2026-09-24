<#
.SYNOPSIS
  Replay the dual-base-location proof pipeline on the LOCAL ComfyUI host (no RunPod).

.DESCRIPTION
  Same five stages as the proven serverless run (runs/dual-location-20260919-132253), but every
  sampling job goes to the local ComfyUI host instead of a RunPod Serverless endpoint:

    01 refs         pose skeletons (1216x832) + location references, copied from a source run
    03 studio-a     FLUX + XLabs OpenPose figure render, pose A   -> 03-studio-a/
    04 studio-b     FLUX + XLabs OpenPose figure render, pose B   -> 04-studio-b/
    05 composites   rembg cutout + place on each location         -> 05-composites/
    06 harmonized   masked FLUX harmonize per location x pose     -> 06-harmonized-<loc>-<pose>/

  Each job writes its exact workflow + image manifest into its stage folder, and the run writes
  run-manifest.json, so every render is reproducible from the committed inputs alone.

  Graph provenance: the sampling graphs and prompts are the committed proofs
  (proofs/studio-a.workflow.json, proofs/figures-studio-b.workflow.json,
  proofs/harmonize-face-to-face.workflow.json, proofs/harmonize-base-b.workflow.json) with ONE
  mechanical change - the FLUX loader. The pod served flux1-dev-fp8.safetensors from
  models/checkpoints and fed CLIPTextEncode from CheckpointLoaderSimple; the local host has it as
  a UNET under models/diffusion_models, so node 4 is UNETLoader and the encoders read the
  DualCLIPLoader. Samplers, seeds, denoise, masks, prompts and canvas are unchanged.
  Ported graphs: proofs-local/.

  Prerequisite: the XLabs FLUX runtime must be installed on the host
  (helpers/local-comfyui-host/provision-xlabs-flux.ps1). The runner fails fast if it is missing.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File specs/image-generator-tests/dual-base-location/run-local-dual-location.ps1
#>
[CmdletBinding()]
param(
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    [string]$RunLabel,
    [string]$SourceRun = 'runs/dual-location-20260919-132253',
    [string]$Locations = 'bedroom,outdoors',
    [string]$PythonExe = '.venv\Scripts\python.exe',
    [switch]$SkipStudio,
    [switch]$SkipHarmonize,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$harness   = $PSScriptRoot
$repoRoot  = (Resolve-Path (Join-Path $harness '..\..\..')).Path
$runner    = Join-Path $repoRoot 'helpers\local-comfyui-host\run-local-proof.ps1'
$sourceDir = if ([IO.Path]::IsPathRooted($SourceRun)) { $SourceRun } else { Join-Path $harness $SourceRun }

if (-not (Test-Path $runner)) { throw "Local proof runner not found: $runner" }
if (-not (Test-Path $sourceDir)) { throw "Source run not found: $sourceDir" }

$stamp   = Get-Date -Format 'yyyyMMdd-HHmm'
$label   = if ($RunLabel) { $RunLabel } else { "dual-location-local-$stamp" }
$runDir  = Join-Path $harness "runs\$label"
$refsDir = Join-Path $runDir 'refs'
$locList = @($Locations.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })

# ------------------------------------------------------------------ scene prompts
# Both bedroom prompts are VERBATIM from the committed harmonize workflows.
# The outdoors prompts are authored here for the outdoors reference (the previous run's outdoors
# prompt was not recorded anywhere); they mirror the bedroom prompt structure and match the
# reference's dappled forest light. Recorded in run-manifest.json.
$scenePrompts = @{
    'bedroom|A'  = "photorealistic cinematic photograph of a man and a woman standing face to face on a bedroom carpet, warm afternoon window light from behind, soft warm key light on their faces matching the room's lamp glow, natural realistic skin, natural fabric texture on T-shirts and jeans, realistic shadows under their feet, beige carpet on the floor around them, natural eyes, film grain"
    'bedroom|B'  = "photorealistic cinematic photograph of a man and a woman standing side by side facing right on a bedroom carpet, both in right-facing profile, warm afternoon window light, soft warm key light on their faces matching the room's lamp glow, natural realistic skin, natural fabric texture on T-shirts and jeans, realistic shadows under their feet, beige carpet on the floor around them, natural eyes, film grain"
    'outdoors|A' = "photorealistic cinematic photograph of a man and a woman standing face to face in a sunlit forest clearing, dappled green forest light from above and behind, soft natural key light on their faces with green leaf-shadow mottling matching the clearing, natural realistic skin, natural fabric texture on T-shirts and jeans, realistic shadows under their feet on the grass, natural eyes, film grain"
    'outdoors|B' = "photorealistic cinematic photograph of a man and a woman standing side by side facing right in a sunlit forest clearing, both in right-facing profile, dappled green forest light from above and behind, soft natural key light on their faces with green leaf-shadow mottling matching the clearing, natural realistic skin, natural fabric texture on T-shirts and jeans, realistic shadows under their feet on the grass, natural eyes, film grain"
}

function Write-Stage($msg) { Write-Host ""; Write-Host "=== $msg" -ForegroundColor Cyan }

# ------------------------------------------------------------------ run dir + refs
Write-Stage "Run $label"
New-Item -ItemType Directory -Path $refsDir -Force | Out-Null
Write-Host "run dir : $runDir"

$refFiles = @(
    'pose-a-face-to-face-1216x832.png',
    'pose-b-both-right-1216x832.png'
)
foreach ($loc in $locList) { $refFiles += "locref-$loc.png" }

foreach ($f in $refFiles) {
    $src = Join-Path (Join-Path $sourceDir 'refs') $f
    if (-not (Test-Path $src)) { throw "Missing source reference: $src" }
    Copy-Item -Path $src -Destination (Join-Path $refsDir $f) -Force
    Write-Host "  ref $f"
}

$manifest = [ordered]@{
    runLabel    = $label
    comfyUiUrl  = $ComfyUiUrl
    startedAt   = (Get-Date).ToString('o')
    sourceRun   = $SourceRun
    locations   = $locList
    scenePrompts = $scenePrompts
    jobs        = @()
}

function Write-ImagesManifest($path, $entries) {
    $json = ConvertTo-Json -InputObject @($entries) -Depth 6
    $parent = Split-Path -Parent $path
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($path, $json)
}

function Invoke-LocalProof($name, $workflowPath, $imagesManifest, $outDir, $prefix) {
    Write-Host ""
    Write-Host "--- job: $name"
    Write-Host "    workflow: $workflowPath"
    Write-Host "    out     : $outDir"
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runner,
        '-WorkflowPath', $workflowPath,
        '-ImagesJsonPath', $imagesManifest,
        '-ComfyUiUrl', $ComfyUiUrl,
        '-OutDir', $outDir,
        '-Prefix', $prefix
    )
    if ($DryRun) { $arguments += '-DryRun' }
    & powershell @arguments
    if ($LASTEXITCODE -ne 0) { throw "job '$name' failed (exit $LASTEXITCODE)" }
    $script:manifest.jobs += [ordered]@{
        name     = $name
        workflow = (Resolve-Path $workflowPath).Path.Replace($repoRoot + '\', '')
        images   = (Resolve-Path $imagesManifest).Path.Replace($repoRoot + '\', '')
        outDir   = $outDir.Replace($repoRoot + '\', '')
    }
}

# ------------------------------------------------------------------ studio figures
if (-not $SkipStudio) {
    Write-Stage "03/04 studio figures (FLUX + XLabs OpenPose)"
    $studioJobs = @(
        @{ name = 'studio-a'; workflow = 'proofs-local\flux-figures-A.workflow.json'; pose = 'pose-a-face-to-face-1216x832.png'; stage = '03-studio-a'; prefix = 'img-local-studio-a' },
        @{ name = 'studio-b'; workflow = 'proofs-local\flux-figures-B.workflow.json'; pose = 'pose-b-both-right-1216x832.png';      stage = '04-studio-b'; prefix = 'img-local-studio-b' }
    )
    foreach ($job in $studioJobs) {
        $stageDir = Join-Path $runDir $job.stage
        $manPath  = Join-Path $stageDir 'images.json'
        Write-ImagesManifest $manPath @(@{
            name = 'pose_reference.png'
            path = (Join-Path $refsDir $job.pose).Replace($repoRoot + '\', '')
        })
        Invoke-LocalProof $job.name (Join-Path $harness $job.workflow) $manPath $stageDir $job.prefix
    }
} else {
    Write-Host "skipping studio stage (-SkipStudio)"
}

$studioA = Join-Path $runDir '03-studio-a\img-local-studio-a_0.png'
$studioB = Join-Path $runDir '04-studio-b\img-local-studio-b_0.png'

# ------------------------------------------------------------------ composites
# Composites exist to feed the harmonize stage, so they are skipped with it.
if ($SkipHarmonize) {
    Write-Host "skipping composite stage (-SkipHarmonize): composites only feed the harmonize stage"
} elseif (-not $DryRun) {
    Write-Stage "05 composites (rembg, CPU)"
    foreach ($p in @($studioA, $studioB)) {
        if (-not (Test-Path $p)) { throw "composite stage needs $p - run the studio stage first" }
    }
    $py = Join-Path $repoRoot $PythonExe
    if (-not (Test-Path $py)) { throw "python not found: $py" }
    & $py (Join-Path $harness 'composite-two-poses.py') `
        --run-dir $runDir `
        --studio-a $studioA `
        --studio-b $studioB `
        --locations ($locList -join ',')
    if ($LASTEXITCODE -ne 0) { throw "composite stage failed (exit $LASTEXITCODE)" }
} else {
    Write-Host "DRY RUN: skipping composites"
}

# ------------------------------------------------------------------ harmonize
if (-not $SkipHarmonize) {
    Write-Stage "06 harmonized bases (4 images)"
    foreach ($loc in $locList) {
        foreach ($pose in @('a', 'b')) {
            $upper    = $pose.ToUpperInvariant()
            $stage    = "06-harmonized-$loc-$pose"
            $stageDir = Join-Path $runDir $stage
            $compPath = Join-Path $runDir "05-composites\comp-$loc-$pose.png"
            $maskPath = Join-Path $runDir "05-composites\comp-$loc-$pose-mask.png"
            if (-not $DryRun) {
                foreach ($p in @($compPath, $maskPath)) {
                    if (-not (Test-Path $p)) { throw "harmonize stage needs $p" }
                }
            }

            $promptKey = "$loc|$upper"
            if (-not $scenePrompts.ContainsKey($promptKey)) {
                throw "No scene prompt defined for '$promptKey'. Add it to the scenePrompts table in this script and to run-manifest.json's record."
            }

            $template = if ($upper -eq 'A') { 'proofs-local\harmonize-A.workflow.json' } else { 'proofs-local\harmonize-B.workflow.json' }
            $wf = Get-Content -Raw -Path (Join-Path $harness $template) | ConvertFrom-Json
            $wf.PSObject.Properties['6'].Value.inputs.text = $scenePrompts[$promptKey]

            if (-not (Test-Path $stageDir)) { New-Item -ItemType Directory -Path $stageDir -Force | Out-Null }
            $jobWorkflow = Join-Path $stageDir 'workflow.json'
            [IO.File]::WriteAllText($jobWorkflow, ($wf | ConvertTo-Json -Depth 30))

            $manPath = Join-Path $stageDir 'images.json'
            Write-ImagesManifest $manPath @(
                @{ name = 'composite_image.png'; path = $compPath.Replace($repoRoot + '\', '') },
                @{ name = 'harmonize_mask.png';  path = $maskPath.Replace($repoRoot + '\', '') }
            )

            Invoke-LocalProof "$stage" $jobWorkflow $manPath $stageDir "img-local-$loc-$pose"
        }
    }
} else {
    Write-Host "skipping harmonize stage (-SkipHarmonize)"
}

# ------------------------------------------------------------------ manifest
$manifest.finishedAt = (Get-Date).ToString('o')
[IO.File]::WriteAllText((Join-Path $runDir 'run-manifest.json'), ($manifest | ConvertTo-Json -Depth 12))

Write-Host ""
Write-Host "Run complete: $runDir" -ForegroundColor Green
Write-Host "Final bases:" -ForegroundColor Green
foreach ($loc in $locList) {
    foreach ($pose in @('a', 'b')) {
        $p = Join-Path $runDir "06-harmonized-$loc-$pose\img-local-$loc-${pose}_0.png"
        if (Test-Path $p) { Write-Host "  $p" }
    }
}
Write-Host ""
Write-Host "Next step (identity): apply the Qwen edit face pass per character on each base."
