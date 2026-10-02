# Renders corrected catalog prompts on the LOCAL ComfyUI host, one family graph per model key, so the
# dialect corrections can be validated by eye instead of asserted.
#
# Families (all verified present on the host inventory 2026-10-01):
#   sdxl    bigLust_v16 / juggernautXL_ragnarok / ponyDiffusionV6XL_v6   (CheckpointLoaderSimple)
#   flux    flux1-dev-fp8 + t5xxl_fp8_e4m3fn + clip_l + ae.safetensors   + aidmaNSFWunlock-FLUX LoRA
#   qwen21  qwen_image_2.1_int8_convrot + qwen_2.5_vl_7b_fp8 + 2.1 VAE   (TextEncodeQwenImage21)
# The prompt is read FROM the catalog file, so the text rendered is the text shipped.
#
# NOTE: the two `*-edit` dialects are instructions applied to an existing image and are NOT rendered
# here - they need a source image per cell and are handled by a separate pass.
[CmdletBinding()]
param(
    [string[]]$Tasks = @('all'),
    [string]$OutRoot = 'artifacts/tmp/baseline-dialects',
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    [string]$RunStamp = '',
    [int]$TimeoutSec = 1800,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot
$runner = Join-Path $PSScriptRoot 'run-local-proof.ps1'
$catRoot = Join-Path $repoRoot 'specs/image-generator-tests/baseline/positions'
$stamp = if ($RunStamp) { $RunStamp } else { Get-Date -Format 'yyyyMMdd-HHmmss' }
$runDir = Join-Path $OutRoot $stamp
$wfDir = Join-Path $runDir 'workflows'
New-Item -ItemType Directory -Force -Path $wfDir | Out-Null

# ---- host model files -------------------------------------------------------------------
$SdxlCheckpoints = @{
    'biglust'    = 'bigLust_v16.safetensors'
    'juggernaut' = 'juggernautXL_ragnarok.safetensors'
    'pony'       = 'ponyDiffusionV6XL_v6.safetensors'
}
$PonyNegative = 'extra penis, multiple penises, two penises, duplicate anatomy, blurry, low quality, ugly, deformed, extra limbs, bad anatomy, watermark, text, censored, mosaic, airbrushed, plastic skin'
$FluxUnet = 'flux1-dev-fp8.safetensors'
$FluxT5 = 't5xxl_fp8_e4m3fn.safetensors'
$FluxClipL = 'clip_l.safetensors'
$FluxVae = 'ae.safetensors'
$FluxUnlockLora = 'aidmaNSFWunlock-FLUX-V0.2.safetensors'
$Qwen21Unet = 'qwen_image_2.1_int8_convrot.safetensors'
# Qwen-Image-2.1's text encoder is the 8B Qwen3-VL (hidden 4096). Using the 7B (hidden 3584) fails
# deep inside the DiT with "Given normalized_shape=[4096] ... got input of size[1, 136, 3584]".
$Qwen21Clip = 'qwen3vl_8b_int8_convrot.safetensors'
$Qwen21Vae = 'qwen_image_2.1_vae_bf16.safetensors'

function Get-CatalogValue([string]$cellId, [string]$dialect) {
    $p = Join-Path $catRoot "$cellId.json"
    if (-not (Test-Path $p)) { throw "catalog position '$cellId' not found" }
    $j = Get-Content -Raw $p | ConvertFrom-Json
    $v = $j.variants.$dialect
    if (-not $v) { throw "position '$cellId' has no '$dialect' variant" }
    return $v
}
function Get-CatalogSetting([string]$cellId, [string]$name, $default) {
    $j = Get-Content -Raw (Join-Path $catRoot "$cellId.json") | ConvertFrom-Json
    if ($j.settings.PSObject.Properties.Name -contains $name) { return $j.settings.$name }
    return $default
}

function New-SdxlGraph([string]$prompt, [string]$dialect, [int]$w, [int]$h, [int]$seed) {
    $neg = if ($dialect -eq 'pony') { $PonyNegative } else { '' }
    $g = [ordered]@{}
    $g['1'] = @{ class_type = 'CheckpointLoaderSimple'; inputs = @{ ckpt_name = $SdxlCheckpoints[$dialect] } }
    $g['2'] = @{ class_type = 'CLIPTextEncode'; inputs = @{ text = $prompt; clip = @('1', 1) } }
    $g['3'] = @{ class_type = 'CLIPTextEncode'; inputs = @{ text = $neg; clip = @('1', 1) } }
    $g['4'] = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $w; height = $h; batch_size = 1 } }
    $g['5'] = @{ class_type = 'KSampler'; inputs = @{
            model = @('1', 0); positive = @('2', 0); negative = @('3', 0); latent_image = @('4', 0)
            seed = $seed; steps = 30; cfg = 5.0; sampler_name = 'dpmpp_2m_sde'; scheduler = 'karras'; denoise = 1.0 } }
    $g['6'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('5', 0); vae = @('1', 2) } }
    $g['7'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('6', 0); filename_prefix = 'baseline-dialect' } }
    return $g
}

function New-FluxGraph([string]$prompt, [int]$w, [int]$h, [int]$seed) {
    $g = [ordered]@{}
    $g['1'] = @{ class_type = 'UNETLoader'; inputs = @{ unet_name = $FluxUnet; weight_dtype = 'default' } }
    $g['2'] = @{ class_type = 'DualCLIPLoader'; inputs = @{ clip_name1 = $FluxT5; clip_name2 = $FluxClipL; type = 'flux' } }
    $g['3'] = @{ class_type = 'VAELoader'; inputs = @{ vae_name = $FluxVae } }
    # The unlock lever the app uses for FLUX; without it the model refuses explicit content.
    $g['9'] = @{ class_type = 'LoraLoaderModelOnly'; inputs = @{ model = @('1', 0); lora_name = $FluxUnlockLora; strength_model = 1.0 } }
    $g['4'] = @{ class_type = 'CLIPTextEncode'; inputs = @{ text = $prompt; clip = @('2', 0) } }
    $g['5'] = @{ class_type = 'FluxGuidance'; inputs = @{ conditioning = @('4', 0); guidance = 3.5 } }
    $g['6'] = @{ class_type = 'ConditioningZeroOut'; inputs = @{ conditioning = @('4', 0) } }
    $g['7'] = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $w; height = $h; batch_size = 1 } }
    $g['8'] = @{ class_type = 'KSampler'; inputs = @{
            model = @('9', 0); positive = @('5', 0); negative = @('6', 0); latent_image = @('7', 0)
            seed = $seed; steps = 20; cfg = 1.0; sampler_name = 'euler'; scheduler = 'simple'; denoise = 1.0 } }
    $g['10'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('8', 0); vae = @('3', 0) } }
    $g['11'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('10', 0); filename_prefix = 'baseline-dialect' } }
    return $g
}

function New-Qwen21Graph([string]$prompt, [int]$w, [int]$h, [int]$seed) {
    $g = [ordered]@{}
    $g['1'] = @{ class_type = 'UNETLoader'; inputs = @{ unet_name = $Qwen21Unet; weight_dtype = 'default' } }
    $g['2'] = @{ class_type = 'CLIPLoader'; inputs = @{ clip_name = $Qwen21Clip; type = 'qwen_image'; device = 'default' } }
    $g['3'] = @{ class_type = 'VAELoader'; inputs = @{ vae_name = $Qwen21Vae } }
    $g['4'] = @{ class_type = 'TextEncodeQwenImage21'; inputs = @{ clip = @('2', 0); prompt = $prompt; negative_prompt = ''; resolution = $w } }
    $g['5'] = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $w; height = $h; batch_size = 1 } }
    $g['6'] = @{ class_type = 'KSampler'; inputs = @{
            model = @('1', 0); positive = @('4', 0); negative = @('4', 1); latent_image = @('5', 0)
            seed = $seed; steps = 40; cfg = 1.0; sampler_name = 'euler'; scheduler = 'simple'; denoise = 1.0 } }
    $g['7'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('6', 0); vae = @('3', 0) } }
    $g['8'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('7', 0); filename_prefix = 'baseline-dialect' } }
    return $g
}

# ---- worklist: every corrected dialect string that is a text-to-image render -------------
$worklist = @(
    @{ Cell = '69';                              Dialect = 'biglust';        Family = 'sdxl' }
    @{ Cell = 'mmf-double-penetration';          Dialect = 'biglust';        Family = 'sdxl' }
    @{ Cell = 'mff-cowgirl-cunnilingus-closeup'; Dialect = 'biglust';        Family = 'sdxl' }
    @{ Cell = 'mmf-spitroast-closeup';           Dialect = 'biglust';        Family = 'sdxl' }
    @{ Cell = 'orgy-four-way-closeup';           Dialect = 'biglust';        Family = 'sdxl' }
    @{ Cell = 'erotic-hands-and-knees';          Dialect = 'biglust';        Family = 'sdxl' }
    @{ Cell = 'doggy-penetration-closeup';       Dialect = 'biglust';        Family = 'sdxl' }
    @{ Cell = 'mmf-double-penetration';          Dialect = 'juggernaut';     Family = 'sdxl' }
    @{ Cell = 'mff-cowgirl-cunnilingus-closeup'; Dialect = 'juggernaut';     Family = 'sdxl' }
    @{ Cell = 'orgy-four-way-closeup';           Dialect = 'juggernaut';     Family = 'sdxl' }
    @{ Cell = 'mmf-spitroast-closeup';           Dialect = 'pony';           Family = 'sdxl' }
    @{ Cell = 'orgy-four-way-closeup';           Dialect = 'pony';           Family = 'sdxl' }
    @{ Cell = '69';                              Dialect = 'flux';           Family = 'flux' }
    @{ Cell = 'mmf-double-penetration';          Dialect = 'flux';           Family = 'flux' }
    @{ Cell = 'spooning';                        Dialect = 'flux';           Family = 'flux' }
    @{ Cell = '69';                              Dialect = 'qwen-image-2.1'; Family = 'qwen21' }
    @{ Cell = 'mff-cowgirl-cunnilingus-closeup'; Dialect = 'qwen-image-2.1'; Family = 'qwen21' }
    @{ Cell = 'spooning';                        Dialect = 'qwen-image-2.1'; Family = 'qwen21' }
)

$selected = if ($Tasks -contains 'all') { $worklist } else {
    # `powershell -File <script> -Tasks a,b,c` binds the WHOLE comma list as ONE string (unlike a
    # cmdlet call), so split at the parameter boundary. Same trap as the -Cells bug.
    $want = @($Tasks | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    @($worklist | Where-Object { $key = "$($_.Cell)/$($_.Dialect)"; ($want -contains $key) -or ($want -contains $_.Cell) })
}
if ($selected.Count -eq 0) { throw "No tasks selected from: $($Tasks -join ', ')" }

$results = @()
$i = 0
foreach ($t in $selected) {
    $i++
    $prompt = Get-CatalogValue $t.Cell $t.Dialect
    $w = [int](Get-CatalogSetting $t.Cell 'width' 1024)
    $h = [int](Get-CatalogSetting $t.Cell 'height' 1024)
    $seed = [int](Get-CatalogSetting $t.Cell 'seed' 4242)
    $graph = switch ($t.Family) {
        'sdxl'   { New-SdxlGraph   $prompt $t.Dialect $w $h $seed }
        'flux'   { New-FluxGraph   $prompt $w $h $seed }
        'qwen21' { New-Qwen21Graph $prompt $w $h $seed }
        default  { throw "unknown family $($t.Family)" }
    }
    $label = ("{0:d2}-{1}-{2}" -f $i, $t.Cell, ($t.Dialect -replace '[^a-zA-Z0-9\-]', '_'))
    $cellDir = Join-Path $runDir $label
    New-Item -ItemType Directory -Force -Path $cellDir | Out-Null
    $wfFile = Join-Path $wfDir "$label.workflow.json"
    $graph | ConvertTo-Json -Depth 12 | Set-Content -Path $wfFile -Encoding utf8

    Write-Host ''
    Write-Host ('=' * 100)
    Write-Host ("[{0}/{1}] {2} / {3}  ({4})  {5}x{6}  seed={7}" -f $i, $selected.Count, $t.Cell, $t.Dialect, $t.Family, $w, $h, $seed)
    Write-Host ("PROMPT: {0}" -f $prompt)
    Write-Host ('=' * 100)
    if ($DryRun) { $results += [pscustomobject]@{ Cell = $t.Cell; Dialect = $t.Dialect; Family = $t.Family; Dir = $cellDir; Saved = 0; Prompt = $prompt }; continue }

    try {
        & powershell -ExecutionPolicy RemoteSigned -File $runner -WorkflowPath $wfFile -OutDir $cellDir `
            -Prefix ("{0}-{1}" -f $t.Cell, ($t.Dialect -replace '[^a-zA-Z0-9\-]', '_')) -ComfyUiUrl $ComfyUiUrl -TimeoutSec $TimeoutSec -Seed $seed
        $saved = @(Get-ChildItem $cellDir -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.png', '.jpg', '.jpeg', '.webp' })
        $results += [pscustomobject]@{ Cell = $t.Cell; Dialect = $t.Dialect; Family = $t.Family; Dir = $cellDir; Saved = $saved.Count; Prompt = $prompt }
    } catch {
        Write-Host "FAILED $($t.Cell)/$($t.Dialect): $($_.Exception.Message)"
        $results += [pscustomobject]@{ Cell = $t.Cell; Dialect = $t.Dialect; Family = $t.Family; Dir = $cellDir; Saved = 0; Prompt = $prompt }
    }
}

if (-not $DryRun) {
    $results | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $runDir 'results.json') -Encoding utf8
    Write-Host ''
    Write-Host "=== SUMMARY ($runDir) ==="
    $results | ForEach-Object { "  {0,-34} {1,-18} {2,-7} saved={3}" -f $_.Cell, $_.Dialect, $_.Family, $_.Saved }
}
