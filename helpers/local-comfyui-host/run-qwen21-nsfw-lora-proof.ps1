# Controlled proof: does a community NSFW LoRA for Qwen-Image-2.1 actually beat the base model,
# and does the LoRA author's CFG/sampler recipe matter -- or only the LoRA?
#
# Design (the krea2 lesson: an arm only counts when its CONTROL was run too):
#   base-envelope  = the app's qualified Qwen-2.1 envelope   (steps 25, cfg 1.0, euler/simple)  NO LoRA
#   lora-envelope  = the same envelope + the LoRA            -> isolates the LoRA alone
#   base-recipe    = the LoRA author's recipe                (steps 30, cfg 3.0, er_sde/beta)   NO LoRA
#   lora-recipe    = recipe + LoRA                           -> isolates recipe+LoRA
# Prompt text is read FROM the baseline catalog file (dialect `qwen-image-2.1`), so the text that is
# rendered is the text that ships. Cells are 1024x1024 with the catalog's declared seed.
#
# LoRA: "NSFW Qwen by TheseAlpacas V2.safetensors" (Civitai 2958918 / version 3357315).
# Fetch it with helpers/local-comfyui-host/fetch-qwen21-nsfw-lora.ps1.
[CmdletBinding()]
param(
    [ValidateSet('core', 'multiperson', 'genital', 'malestate', 'maleneg', 'female', 'femaleneg', 'phantomfix', 'personframing', 'missionary', 'penetration', 'size', 'vulvacheck')]
    [string]$Suite = 'core',
    [string[]]$Cells = @('all'),
    [string[]]$Arms = @('all'),
    [double]$LoraStrength = 1.0,
    [string]$NegativePrompt = '',
    [string]$OutRoot = 'artifacts/tmp/qwen21-nsfw-lora',
    [string]$ComfyUiUrl = 'http://192.168.0.11:8188',
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

$Qwen21Unet = 'qwen_image_2.1_int8_convrot.safetensors'
$Qwen21Clip = 'qwen3vl_8b_int8_convrot.safetensors'   # 8B Qwen3-VL (hidden 4096) - NOT the 7B
$Qwen21Vae = 'qwen_image_2.1_vae_bf16.safetensors'

# ---- LoRAs (all three must already be on the host; see fetch-qwen21-nsfw-lora.ps1) --------
$LoraAlpacas   = 'NSFW Qwen by TheseAlpacas V2.safetensors'             # general acts + anatomy
$LoraCoachBate = 'qwen-image-2.1_penis_coachbate_preview1.safetensors'   # male anatomy specialist
$LoraVagina    = 'qwen21_v2_000002750.safetensors'                       # female vulva specialist (v1)
# Added 2026-10-02 after enumerating all 291 Qwen-2.1-base LoRAs on Civitai by downloads:
#   Translucent Penetration v5 (model 2322841 / version 3370853, 4.3k dl) - the ONLY 2.1 penetration
#       specialist found. Author's own constraints: "works best within 1.0 MP - 2.5 MP" (so 2048^2 =
#       4.2 MP is OUTSIDE its trained range and 1536^2 = 2.36 MP is inside), "use with CFG ~3.0 and a
#       weight of 1.0", and "using a general NSFW lora would still help with positions/consistency".
#   Perfect erect penis (2955779 / 3348119, 2.3k dl) - more-downloaded male specialist than CoachBate.
#   qwen 2.1 vagina v2.0 (2976277 / 3377365, 1.7k dl, published 2026-10-02) - newer female specialist.
$LoraPenetration = 'translucent_penetration-V5+Qwen-Image-2.1.safetensors'
$LoraPenis2      = 'qwen2.1_penisV01_000004956.safetensors'
$LoraVagina2     = 'pussyV2.safetensors'
# The ONLY size control that exists for 2.1 (model 2961110 / version 3354620, 528 dl). Acquired after the
# operator rejected the s11 penetration arms for "abnormal penis size" - no arm in that round controlled
# size, and the prompt text never stated one (it said only "his erect penis").
$LoraPenisSmall  = 'Q21 make the penis small.safetensors'

# ---- suites ------------------------------------------------------------------------------
# core        : 2 envelopes x {base, LoRA} on 4 mixed cells (the matrix recorded 2026-10-02)
# multiperson : do 3-4 person cells prefer a DIFFERENT envelope, and does a LoRA help them?
# genital     : close-up genital anatomy - female LoRA vs male LoRA vs both chained
# malestate   : new probe prompts - can 2.1 do flaccid / semi / erect-size variants at all?
$suiteArms = @{
    core        = @(
        @{ Key = 'base-envelope';   Loras = @();                             Steps = 25; Cfg = 1.0; Sampler = 'euler';  Scheduler = 'simple' },
        @{ Key = 'lora-envelope';   Loras = @($LoraAlpacas);                 Steps = 25; Cfg = 1.0; Sampler = 'euler';  Scheduler = 'simple' },
        @{ Key = 'base-recipe';     Loras = @();                             Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta'   },
        @{ Key = 'lora-recipe';     Loras = @($LoraAlpacas);                 Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta'   }
    )
    multiperson = @(
        @{ Key = 'base-envelope';    Loras = @();                              Steps = 25; Cfg = 1.0; Sampler = 'euler';  Scheduler = 'simple' },
        @{ Key = 'base-recipe';      Loras = @();                              Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta'   },
        @{ Key = 'alpacas-recipe';   Loras = @($LoraAlpacas);                  Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta'   },
        @{ Key = 'coachbate-recipe'; Loras = @($LoraCoachBate);                Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta'   }
    )
    genital = @(
        @{ Key = 'base-recipe';      Loras = @();                              Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'vagina-recipe';    Loras = @($LoraVagina);                   Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'both-recipe';      Loras = @($LoraCoachBate, $LoraVagina);   Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' }
    )
    malestate = @(
        @{ Key = 'base-recipe';      Loras = @();                              Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'coachbate-recipe'; Loras = @($LoraCoachBate);                Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' }
    )
    maleneg = @(
        @{ Key = 'base-recipe';      Loras = @();                              Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'coachbate-recipe'; Loras = @($LoraCoachBate);                Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' }
    )
    female = @(
        @{ Key = 'base-recipe';      Loras = @();                              Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'vagina-recipe';    Loras = @($LoraVagina);                   Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' }
    )
    femaleneg = @(
        @{ Key = 'base-recipe';      Loras = @();                              Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' }
    )
    phantomfix = @(
        @{ Key = 'base-recipe';      Loras = @();                              Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' }
    )
    personframing = @(
        @{ Key = 'base-recipe';      Loras = @();                              Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'vagina-recipe';    Loras = @($LoraVagina);                   Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' }
    )
    # 50 steps (not 30): 2.1 is NOT distilled, official range is 25-50, and under-stepping is a candidate
    # cause of incoherent fingers/genitalia. Canvas is varied per probe, so this suite pairs on size.
    missionary = @(
        @{ Key = 'base-50';          Loras = @();                              Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'alpacas-50';       Loras = @($LoraAlpacas);                  Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' }
    )
    # NOTE: until this suite, EVERY arm ran with strength_model = 1.0 on every LoRA. Strength was the
    # one untested lever, and 1.0 is exactly where the v1 vulva specialist regressed. Strengths here are
    # per-arm and positional (parallel to Loras). The penetration LoRA keeps its author-recommended 1.0
    # (with alpacas at 0.6 as the general-NSFW companion the author says the LoRA needs).
    penetration = @(
        @{ Key = 'alpmain-060';             Loras = @($LoraAlpacas);                                            Strengths = @(0.6);           Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'penet-100';               Loras = @($LoraPenetration);                                        Strengths = @(1.0);           Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'alpmain060-penet100';     Loras = @($LoraAlpacas, $LoraPenetration);                          Strengths = @(0.6, 1.0);      Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'alp060-penet100-vag060';  Loras = @($LoraAlpacas, $LoraPenetration, $LoraVagina2);            Strengths = @(0.6, 1.0, 0.6); Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'alp060-penet100-pen080';  Loras = @($LoraAlpacas, $LoraPenetration, $LoraPenis2);             Strengths = @(0.6, 1.0, 0.8); Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'penisspec-080';           Loras = @($LoraPenis2);                                             Strengths = @(0.8);           Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'vagspec2-060';            Loras = @($LoraVagina2);                                            Strengths = @(0.6);           Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' }
    )
    # s11 verdict (operator, 2026-10-02): the penetration LoRA DID make the join readable, but every arm
    # rendered an oversized thick penis and stretched the labia into a ring around the shaft. This suite
    # attacks both: an explicit size/proportion clause in the POSITIVE prompt (never a negative - negatives
    # were dropped for this model), a LOWER penetration strength, and the dedicated size-control LoRA.
    size = @(
        @{ Key = 'base-normsize';           Loras = @();                                                        Strengths = @();              Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'alp060-penet100-norm';    Loras = @($LoraAlpacas, $LoraPenetration);                          Strengths = @(0.6, 1.0);      Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'alp060-penet060';         Loras = @($LoraAlpacas, $LoraPenetration);                          Strengths = @(0.6, 0.6);      Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'alp060-penet060-sm060';   Loras = @($LoraAlpacas, $LoraPenetration, $LoraPenisSmall);         Strengths = @(0.6, 0.6, 0.6); Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'alp060-penet060-sm100';   Loras = @($LoraAlpacas, $LoraPenetration, $LoraPenisSmall);         Strengths = @(0.6, 0.6, 1.0); Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'alp060-penet100-sm060';   Loras = @($LoraAlpacas, $LoraPenetration, $LoraPenisSmall);         Strengths = @(0.6, 1.0, 0.6); Steps = 50; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' }
    )
    # BINDING CHECK, not a quality test. pussyV2.safetensors is the only one of the seven staged 2.1 LoRAs
    # whose tensor keys do not match the model's layer names: it carries `transformer_blocks.N.attn.to_k.
    # lora_A.default.weight` (no `diffusion_model.` prefix, PEFT `.default.` infix, 448 tensors) where every
    # other file carries `diffusion_model.transformer_blocks.N.attn.to_k.lora_A.weight` (384 tensors) and
    # declares ss_base_model_version=qwen_image_2. ComfyUI reports unplaced keys as "lora key not loaded" and
    # still renders, which is indistinguishable from a LoRA with no visible effect.
    #
    # The test needs no judgement: prompt, seed, canvas, steps, cfg, sampler and scheduler are IDENTICAL across
    # arms, so a LoRA that binds NOTHING must produce byte-identical output. TheseAlpacas is the positive control
    # - a file known to bind - and strength 2.0 is included because a subtle effect at 1.0 is still an effect.
    vulvacheck = @(
        @{ Key = 'base';            Loras = @();             Strengths = @();      Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'pussyv2-100';     Loras = @($LoraVagina2); Strengths = @(1.0);   Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'pussyv2-200';     Loras = @($LoraVagina2); Strengths = @(2.0);   Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' },
        @{ Key = 'thesealpacas-100'; Loras = @($LoraAlpacas); Strengths = @(1.0); Steps = 30; Cfg = 3.0; Sampler = 'er_sde'; Scheduler = 'beta' }
    )
}
$armDefs = $suiteArms[$Suite]

# ---- worklist: catalog cells and/or hand-written probes ----------------------------------
$suiteCells = @{
    core        = @('fellatio', 'missionary', 'erotic-legs-spread', 'cumshot-facial')
    multiperson = @('mmf-double-penetration', 'mmf-spitroast-closeup', 'orgy-four-way')
    genital     = @('missionary-penetration-closeup', 'cumshot-creampie')
    malestate   = @()
    maleneg     = @()
    female      = @()
    femaleneg   = @()
    phantomfix  = @()
    personframing = @()
    missionary  = @()
    penetration = @()
    size        = @()
    vulvacheck  = @()
}
# Probes are NOT catalog cells. They exist to answer "can 2.1 render this erect STATE at all?".
# All four use one identical photographic brief (one adult man, plainly lit room, same framing, lens
# and lighting) and change ONLY the erect state, so a difference is attributable to the state.
$probes = @(
    @{ Id = 'male-flaccid';       Seed = 71001; Prompt = 'A photorealistic photograph of one adult man standing nude in a plainly lit room, facing the camera with his weight relaxed on one leg and his arms loose at his sides, looking slightly away from the lens. His penis is completely soft and flaccid, hanging down naturally against his scrotum with no erection at all; the glans, foreskin and shaft are at rest. Natural realistic body proportions and unretouched skin with chest hair and natural pubic hair. Medium shot from the front at chest height, 50mm lens, soft daylight from a window on the left, shallow depth of field, sharp focus on the middle of his body.' },
    @{ Id = 'male-semi-erect';    Seed = 71002; Prompt = 'A photorealistic photograph of one adult man standing nude in a plainly lit room, facing the camera with his weight relaxed on one leg and his arms loose at his sides, looking slightly away from the lens. His penis is half erect, thickening and beginning to lift away from his scrotum at a shallow upward angle but not fully hard; the glans is partly uncovered and the shaft is visibly swollen. Natural realistic body proportions and unretouched skin with chest hair and natural pubic hair. Medium shot from the front at chest height, 50mm lens, soft daylight from a window on the left, shallow depth of field, sharp focus on the middle of his body.' },
    @{ Id = 'male-erect-average'; Seed = 71003; Prompt = 'A photorealistic photograph of one adult man standing nude in a plainly lit room, facing the camera with his weight relaxed on one leg and his arms loose at his sides, looking slightly away from the lens. His penis is fully erect and average in size, standing up and forward from his body at a natural angle with the glans exposed and the shaft straight. Natural realistic body proportions and unretouched skin with chest hair and natural pubic hair. Medium shot from the front at chest height, 50mm lens, soft daylight from a window on the left, shallow depth of field, sharp focus on the middle of his body.' },
    @{ Id = 'male-erect-large';   Seed = 71004; Prompt = 'A photorealistic photograph of one adult man standing nude in a plainly lit room, facing the camera with his weight relaxed on one leg and his arms loose at his sides, looking slightly away from the lens. His penis is fully erect and noticeably large and thick, standing up and forward from his body with a heavy shaft, prominent veins and the glans fully exposed, the scrotum hanging low beneath it. Natural realistic body proportions and unretouched skin with chest hair and natural pubic hair. Medium shot from the front at chest height, 50mm lens, soft daylight from a window on the left, shallow depth of field, sharp focus on the middle of his body.' },
    @{ Id = 'male-erect-small';   Seed = 71005; Prompt = 'A photorealistic photograph of one adult man standing nude in a plainly lit room, facing the camera with his weight relaxed on one leg and his arms loose at his sides, looking slightly away from the lens. His penis is fully erect but small and slender with modest, unobtrusive proportions; it lifts only slightly away from his full scrotum and the shaft is thin with the glans exposed. Natural realistic body proportions and unretouched skin with chest hair and natural pubic hair. Medium shot from the front at chest height, 50mm lens, soft daylight from a window on the left, shallow depth of field, sharp focus on the middle of his body.' }
)

# At cfg > 1 the negative branch is LIVE (it is inert at cfg 1), so the erect STATE can be steered by
# negative prompt instead of only being asked for in the positive wording. This is the lever the
# `maleneg` suite tests - the same positive text, a negative that names the unwanted state.
$negatives = @{
    'male-flaccid'     = 'erect penis, erection, hard shaft, engorged glans, swollen penis, large penis, thick shaft, tumescence'
    'male-erect-small' = 'large penis, thick shaft, huge penis, very big penis, oversized genitals, bulbous glans, long penis'
    # Every female probe gets the same male-organ negative: the failure mode being tested is a shaft
    # growing out of the woman's own pubic mound, which appeared at the creampie/coitus framings.
    'female-vulva-full'    = 'penis, erect penis, shaft, glans, testicles, scrotum, male genitalia, penis head'
    'female-vulva-medium'  = 'penis, erect penis, shaft, glans, testicles, scrotum, male genitalia, penis head'
    'female-vulva-closeup' = 'penis, erect penis, shaft, glans, testicles, scrotum, male genitalia, penis head'
    'female-vulva-aroused' = 'penis, erect penis, shaft, glans, testicles, scrotum, male genitalia, penis head'
    'female-vulva-spread'  = 'penis, erect penis, shaft, glans, testicles, scrotum, male genitalia, penis head'
    'female-vulva-macro'   = 'penis, erect penis, shaft, glans, testicles, scrotum, male genitalia, penis head'
    # A/B pair for the phantom-organ failure: identical prompt text, only the negative differs.
    'coitus-anchored-posonly' = ''
    'coitus-anchored-neg'     = 'fused genitals, penis attached to the female body, one figure with both sets of organs, hermaphrodite anatomy, merged bodies, penis growing from the vulva'
}

# FEMALE vulva probes - the female analogue of the penis series. ONE adult woman alone in every prompt:
# no male actor, no act, no penetration. That removes the coitus trigger that made the `creampie` cell
# grow a shaft out of the woman's own pubic mound (verified at 2x zoom 2026-10-02 - the earlier
# "best male+female anatomy" reading of that cell was wrong and is retracted).
# The set walks DISTANCE (full-length -> medium hip -> tight -> macro) and STATE (resting,
# aroused, spread) so vulva rendering can be judged the same way the penis states were.
$femaleProbes = @(
    @{ Id = 'female-vulva-full';      Seed = 72001; Prompt = 'A photorealistic nude photograph of one adult woman standing alone in a plainly lit room, facing the camera with her weight on one leg and her arms loose at her sides, looking slightly away from the lens. She is fully nude and her vulva is visible low in the frame between her thighs, with natural proportions, natural pubic hair and unretouched skin showing pores and faint stretch marks on her stomach and thighs. Full-length shot from the front with her whole figure inside the frame, 35mm lens, soft daylight from a window on the left, shallow depth of field.' },
    @{ Id = 'female-vulva-medium';    Seed = 72002; Prompt = 'A photorealistic nude photograph of one adult woman standing alone in a plainly lit room with her hips turned square to the camera, her legs together and her hands resting on her stomach. The frame runs from her waist to her knees, so her vulva is centred and clearly in view between her thighs, with natural proportions, natural pubic hair and unretouched skin. Medium shot from the front at hip height, 50mm lens, soft daylight from a window on the left, shallow depth of field, sharp focus on the middle of her body.' },
    @{ Id = 'female-vulva-closeup';   Seed = 72003; Prompt = 'A photorealistic explicit close-up photograph of the vulva of one adult woman lying on her back on a bed with her knees drawn up and her thighs apart, alone in the frame. Her labia are soft and closed, the outer lips resting together over the vaginal opening, the clitoral hood visible at the top, natural pubic hair around it and unretouched skin with visible pores on her inner thighs. Tight shot from between her legs at eye level, 50mm lens, warm lamp light from the side, very shallow depth of field, sharp focus on her vulva.' },
    @{ Id = 'female-vulva-aroused';   Seed = 72004; Prompt = 'A photorealistic explicit close-up photograph of the vulva of one adult woman lying on her back on a bed with her knees drawn up and her thighs apart, alone in the frame. Her labia are swollen and parted, the inner lips fuller and darker than the outer, the vaginal opening glistening and wet with natural lubrication, the clitoris visible beneath its hood with natural pubic hair around it, and unretouched skin with visible pores on her inner thighs. Tight shot from between her legs at eye level, 50mm lens, warm lamp light from the side, very shallow depth of field, sharp focus on her vulva.' },
    @{ Id = 'female-vulva-spread';    Seed = 72005; Prompt = 'A photorealistic explicit close-up photograph of the vulva of one adult woman lying on her back on a bed with her knees drawn up, alone in the frame. Both of her hands are between her legs, two fingers of each hand spreading her outer labia apart so that her inner labia, vaginal opening and clitoris are fully exposed in the centre of the frame, with natural pubic hair and unretouched skin. Tight shot from between her legs at eye level, 50mm lens, warm lamp light from the side, very shallow depth of field, sharp focus on her vulva.' },
    @{ Id = 'female-vulva-macro';     Seed = 72006; Prompt = 'A photorealistic explicit macro photograph of the vulva of one adult woman, alone in the frame. The frame is filled by her vulva: the outer lips, the inner labia, the clitoral hood and the vaginal opening all in sharp focus, natural pubic hair at the edges and unretouched skin with visible pores. Very shallow depth of field on a 100mm macro lens, warm low-key lamp light from the side, at eye level.' }
)

function Split-List([string[]]$Values) {
    # `powershell -File x.ps1 -Cells a,b,c` binds the comma list as ONE string (documented trap).
    @($Values | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
}
$wantCells = Split-List $Cells
# Probes belong to their own suite only - every other suite would otherwise inherit them.
$phantomProbes = @(
    @{ Id = 'coitus-anchored-posonly'; Seed = 73001; Prompt = 'A photorealistic explicit close-up photograph of two nude adults having sex on a bed in a dimly lit bedroom, framed on the point where their bodies join. The man is clearly a separate person whose lower body enters the frame from above: his flat stomach and the tops of his hairy thighs are visible above the join, his scrotum rests against her perineum, and his erect penis runs from his own groin into her vagina. Her vulva is below his body, with her inner labia visible on either side of the base of his penis and her own thighs spreading away to the left and right; thick semen is flowing out around the base and dripping onto the sheets. Both bodies have natural realistic proportions and unretouched skin, with correct penis and vagina anatomy. Shallow depth of field with sharp focus on the point of contact, at eye level, 35mm macro lens, warm low-key lamp light from the side.' },
    @{ Id = 'coitus-anchored-neg';     Seed = 73001; Prompt = 'A photorealistic explicit close-up photograph of two nude adults having sex on a bed in a dimly lit bedroom, framed on the point where their bodies join. The man is clearly a separate person whose lower body enters the frame from above: his flat stomach and the tops of his hairy thighs are visible above the join, his scrotum rests against her perineum, and his erect penis runs from his own groin into her vagina. Her vulva is below his body, with her inner labia visible on either side of the base of his penis and her own thighs spreading away to the left and right; thick semen is flowing out around the base and dripping onto the sheets. Both bodies have natural realistic proportions and unretouched skin, with correct penis and vagina anatomy. Shallow depth of field with sharp focus on the point of contact, at eye level, 35mm macro lens, warm low-key lamp light from the side.' }
)

# PERSON-PRESENT framings. The previous close-up/macro cells asked the model to REMOVE the person
# ("the frame is filled by her vulva", "alone in the frame", "only the man's lower torso is in frame"),
# and the model duly returned a disembodied, toy-like crop. These probes keep the same subject and the
# same level of genital detail but REQUIRE person anchors in the frame (face/chest/hands/thighs, or both
# partners' bodies), so "is a human in this picture" and "is the anatomy readable" can be judged apart.
$personProbes = @(
    @{ Id = 'coitus-person';          Seed = 74001; Prompt = 'A photorealistic explicit photograph of two nude adults having sex on a bed in a dimly lit bedroom, framed as a medium close-up at hip level rather than a macro. Both people are clearly present: the man is on top with his chest, shoulder and the side of his face visible in the upper part of the frame, and the woman lies under him with her raised knee, her forearm and her hand gripping the sheet visible at the lower left, her head turned away at the edge of the frame. Between their bodies their genitals are joined and in sharp focus, his erect penis entering her vagina with her labia visible where they meet. Two separate bodies with natural realistic proportions and unretouched skin, correct penis and vagina anatomy. 50mm lens, warm low-key lamp light from the side, shallow depth of field, sharp focus on the point where their bodies join.' },
    @{ Id = 'female-closeup-person'; Seed = 74002; Prompt = 'A photorealistic nude photograph of one adult woman lying on her back on a bed, framed as a close-up of her hips and thighs rather than a macro. She is fully present in the frame: her face and her loose hair are visible at the top edge, her breasts and stomach are in view above her pelvis, and both of her hands rest on her inner thighs, which are drawn up and apart so that her vulva is clearly in focus in the lower centre of the frame, with natural pubic hair and unretouched skin showing pores. Nobody else is in the frame. 50mm lens, warm lamp light from the side, shallow depth of field, sharp focus on her vulva.' },
    @{ Id = 'female-macro-person';    Seed = 74003; Prompt = 'A photorealistic explicit close photograph of the vulva of one adult woman lying on her back on a bed with her knees drawn up, framed so tight that her vulva fills the centre of the frame. She is still visibly a person, not a detached crop: her lower stomach and the tops of her thighs frame the subject on all sides, one of her hands rests on her thigh in the lower left corner with her fingers in view, and her pubic hair is continuous with the skin of her stomach. Natural unretouched skin with visible pores and fine hair. 50mm lens, warm lamp light from the side, very shallow depth of field, sharp focus on her vulva.' })

# MISSIONARY focus (operator request 2026-10-02): ONE simple prompt, correct anatomy, with the main NSFW
# LoRA (TheseAlpacas v2 = highest downloads). Same prompt text and same seed for all three probes so the only
# differences are canvas size and the LoRA arm. The wording follows the rules that came out of this programme:
# both bodies are named, the man's parts in frame are named, the join is named, no "frame filled by X".
$missionaryPrompt = 'A photorealistic explicit photograph of two nude adults having sex in the missionary position on a bed in a plainly lit bedroom. The man lies on top of the woman face to face, his body above hers and clearly a separate person; his erect penis is inside her vagina and the point where their bodies join is in sharp focus, with her labia visible around the base of his penis. Both adults are fully present in the frame: his chest, shoulder and the side of his face are visible above her, and her face, her breasts and one hand gripping the sheet are visible beneath him. Two separate bodies with natural realistic proportions, unretouched skin with visible pores and natural body hair, correct penis and vagina anatomy, no fused or merged bodies. Medium shot from a low side angle at bed height, 50mm lens, soft daylight from a window, shallow depth of field, sharp focus on the point where their bodies meet.'
$missionaryProbes = @(
    @{ Id = 'missionary-1024'; Seed = 75001; Width = 1024; Height = 1024; Prompt = $missionaryPrompt },
    @{ Id = 'missionary-1536'; Seed = 75001; Width = 1536; Height = 1536; Prompt = $missionaryPrompt },
    @{ Id = 'missionary-2048'; Seed = 75001; Width = 2048; Height = 2048; Prompt = $missionaryPrompt }
)

# SIZE-NORMALISED twin of $missionaryPrompt (suite `size`). Same scene, framing, seed and canvas; the ONLY
# additions are an explicit ORDINARY-SIZE / natural-proportion clause for the man and a relaxed-labia clause
# for the woman, both stated in the positive branch. s11 showed these LoRAs default to exaggerated porn
# proportions whenever size is left unstated, and that the penetration LoRA stretches the labia into a ring.
$missionaryPromptNorm = 'A photorealistic explicit photograph of two nude adults having sex in the missionary position on a bed in a plainly lit bedroom. The man lies on top of the woman face to face, his body above hers and clearly a separate person; his erect penis is of ordinary average size with unremarkable thickness and natural proportions, and it is inside her vagina with her labia relaxed, soft and undistorted around the base of his penis and the point where their bodies join in sharp focus. Both adults are fully present in the frame: his chest, shoulder and the side of his face are visible above her, and her face, her breasts and one hand gripping the sheet are visible beneath him. Two separate bodies with natural realistic proportions, unretouched skin with visible pores and natural body hair, correct penis and vagina anatomy, no fused or merged bodies. Medium shot from a low side angle at bed height, 50mm lens, soft daylight from a window, shallow depth of field, sharp focus on the point where their bodies meet.'
$sizeProbes = @(
    @{ Id = 'missionary-1536-normsize'; Seed = 75001; Width = 1536; Height = 1536; Prompt = $missionaryPromptNorm }
)

$suiteProbes = @{
    malestate = $probes
    maleneg   = @($probes | Where-Object { $_.Id -in @('male-flaccid', 'male-erect-small') })
    female    = $femaleProbes
    femaleneg = $femaleProbes
    phantomfix = $phantomProbes
    personframing = $personProbes
    missionary = $missionaryProbes
    penetration = $missionaryProbes
    size = $sizeProbes
    vulvacheck = $femaleProbes
}
$probeIds = if ($suiteProbes.ContainsKey($Suite)) { @($suiteProbes[$Suite] | ForEach-Object { $_.Id }) } else { @() }
$allIds = @($suiteCells[$Suite]) + $probeIds
$selectedIds = if ($wantCells -contains 'all') { $allIds } else { @($allIds | Where-Object { $wantCells -contains $_ }) }
if ($selectedIds.Count -eq 0) { throw "No work selected from: $($Cells -join ', ')" }
$wantArms = Split-List $Arms
$selectedArms = if ($wantArms -contains 'all') { $armDefs } else { @($armDefs | Where-Object { $wantArms -contains $_.Key }) }
if ($selectedArms.Count -eq 0) { throw "No arms selected from: $($Arms -join ', ')" }

function Get-CatalogCell([string]$cellId) {
    $p = Join-Path $catRoot "$cellId.json"
    if (-not (Test-Path $p)) { throw "catalog position '$cellId' not found" }
    $j = Get-Content -Raw $p | ConvertFrom-Json
    $prompt = $j.variants.'qwen-image-2.1'
    if (-not $prompt) { throw "position '$cellId' has no 'qwen-image-2.1' variant" }
    $w = 1024; $h = 1024; $seed = 4242
    if ($j.settings.PSObject.Properties.Name -contains 'width')  { $w = [int]$j.settings.width }
    if ($j.settings.PSObject.Properties.Name -contains 'height') { $h = [int]$j.settings.height }
    if ($j.settings.PSObject.Properties.Name -contains 'seed')   { $seed = [int]$j.settings.seed }
    return @{ Prompt = $prompt; Width = $w; Height = $h; Seed = $seed }
}

function New-Qwen21Graph {
    param([string]$Prompt, [int]$W, [int]$H, [int]$Seed, [string[]]$Loras, [int]$Steps, [double]$Cfg, [string]$Sampler, [string]$Scheduler, [string]$Negative = '', [double[]]$Strengths = @())
    $g = [ordered]@{}
    $g['1'] = @{ class_type = 'UNETLoader'; inputs = @{ unet_name = $Qwen21Unet; weight_dtype = 'default' } }
    $g['2'] = @{ class_type = 'CLIPLoader'; inputs = @{ clip_name = $Qwen21Clip; type = 'qwen_image'; device = 'default' } }
    $g['3'] = @{ class_type = 'VAELoader'; inputs = @{ vae_name = $Qwen21Vae } }
    # Chained model-only LoRAs: each loader takes the previous loader's model.
    # Strength is per-LoRA when the arm supplies Strengths (positional); otherwise the uniform -LoraStrength.
    $model = @('1', 0)
    $nodeId = 9
    $li = 0
    foreach ($lora in $Loras) {
        $s = if ($li -lt $Strengths.Count) { [double]$Strengths[$li] } else { $LoraStrength }
        $g["$nodeId"] = @{ class_type = 'LoraLoaderModelOnly'; inputs = @{ model = $model; lora_name = $lora; strength_model = $s } }
        $model = @("$nodeId", 0)
        $nodeId++
        $li++
    }
    $g['4'] = @{ class_type = 'TextEncodeQwenImage21'; inputs = @{ clip = @('2', 0); prompt = $Prompt; negative_prompt = $Negative; resolution = $W } }
    $g['5'] = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $W; height = $H; batch_size = 1 } }
    $g['6'] = @{ class_type = 'KSampler'; inputs = @{
            model = $model; positive = @('4', 0); negative = @('4', 1); latent_image = @('5', 0)
            seed = $Seed; steps = $Steps; cfg = $Cfg; sampler_name = $Sampler; scheduler = $Scheduler; denoise = 1.0 } }
    $g['7'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('6', 0); vae = @('3', 0) } }
    $g['8'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('7', 0); filename_prefix = 'qwen21-nsfw-lora' } }
    return $g
}

# NOTE: this must run AFTER the function definitions above - PowerShell resolves Get-CatalogCell at call time.
$items = @()
foreach ($id in $selectedIds) {
    $probe = @($probes | Where-Object { $_.Id -eq $id }) | Select-Object -First 1
    if (-not $probe) { $probe = @($femaleProbes | Where-Object { $_.Id -eq $id }) | Select-Object -First 1 }
    if (-not $probe) { $probe = @($phantomProbes | Where-Object { $_.Id -eq $id }) | Select-Object -First 1 }
    if (-not $probe) { $probe = @($personProbes | Where-Object { $_.Id -eq $id }) | Select-Object -First 1 }
    if (-not $probe) { $probe = @($missionaryProbes | Where-Object { $_.Id -eq $id }) | Select-Object -First 1 }
    if (-not $probe) { $probe = @($sizeProbes | Where-Object { $_.Id -eq $id }) | Select-Object -First 1 }
    if (-not $probe) { $probe = @($femaleProbes | Where-Object { $_.Id -eq $id }) | Select-Object -First 1 }
    if ($probe) {
        # Probes may declare their own canvas; default stays 1024 so existing probes are unchanged.
        $pw = if ($probe.ContainsKey('Width')) { [int]$probe.Width } else { 1024 }
        $ph = if ($probe.ContainsKey('Height')) { [int]$probe.Height } else { 1024 }
        $items += @{ Id = $probe.Id; Prompt = $probe.Prompt; Width = $pw; Height = $ph; Seed = $probe.Seed; Negative = $(if ($negatives.ContainsKey($probe.Id)) { $negatives[$probe.Id] } else { $NegativePrompt }) }
    } else {
        $c = Get-CatalogCell $id
        $items += @{ Id = $id; Prompt = $c.Prompt; Width = $c.Width; Height = $c.Height; Seed = $c.Seed; Negative = $NegativePrompt }
    }
}

$results = @()
$i = 0
$total = $items.Count * $selectedArms.Count
foreach ($item in $items) {
    foreach ($arm in $selectedArms) {
        $i++
        $label = ("{0:d2}-{1}-{2}" -f $i, $item.Id, $arm.Key)
        $cellDir = Join-Path $runDir $label
        New-Item -ItemType Directory -Force -Path $cellDir | Out-Null
        $armStrengths = if ($arm.ContainsKey('Strengths')) { @($arm.Strengths) } else { @() }
        $graph = New-Qwen21Graph -Prompt $item.Prompt -W $item.Width -H $item.Height -Seed $item.Seed `
            -Loras @($arm.Loras) -Strengths $armStrengths -Steps $arm.Steps -Cfg $arm.Cfg -Sampler $arm.Sampler -Scheduler $arm.Scheduler -Negative $item.Negative
        $wfFile = Join-Path $wfDir "$label.workflow.json"
        $graph | ConvertTo-Json -Depth 12 | Set-Content -Path $wfFile -Encoding utf8

        Write-Host ''
        Write-Host ('=' * 100)
        $effStrengths = if ($armStrengths.Count -gt 0) { ($armStrengths -join ', ') } else { "$LoraStrength" }
        Write-Host ("[{0}/{1}] suite={2} {3} / {4}  loras=[{5}] strengths=[{6}]  steps={7} cfg={8} {9}/{10}  {11}x{12} seed={13}" -f `
            $i, $total, $Suite, $item.Id, $arm.Key, ($arm.Loras -join ' + '), $effStrengths, $arm.Steps, $arm.Cfg, $arm.Sampler, $arm.Scheduler, $item.Width, $item.Height, $item.Seed)
        Write-Host ("PROMPT: {0}" -f $item.Prompt)
        if ($item.Negative) { Write-Host ("NEGATIVE (live only at cfg>1): {0}" -f $item.Negative) }
        Write-Host ('=' * 100)
        if ($DryRun) { $results += [pscustomobject]@{ Cell = $item.Id; Arm = $arm.Key; Dir = $cellDir; Saved = 0; Prompt = $item.Prompt }; continue }

        try {
            & powershell -ExecutionPolicy RemoteSigned -File $runner -WorkflowPath $wfFile -OutDir $cellDir `
                -Prefix $label -ComfyUiUrl $ComfyUiUrl -TimeoutSec $TimeoutSec -Seed $item.Seed
            $saved = @(Get-ChildItem $cellDir -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.png', '.jpg', '.jpeg', '.webp' })
            $results += [pscustomobject]@{ Cell = $item.Id; Arm = $arm.Key; Dir = $cellDir; Saved = $saved.Count; Prompt = $item.Prompt }
        } catch {
            Write-Host "FAILED $label : $($_.Exception.Message)"
            $results += [pscustomobject]@{ Cell = $item.Id; Arm = $arm.Key; Dir = $cellDir; Saved = 0; Prompt = $item.Prompt }
        }
    }
}

if (-not $DryRun) {
    $results | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $runDir 'results.json') -Encoding utf8
    Write-Host ''
    Write-Host "=== SUMMARY ($runDir) ==="
    $results | ForEach-Object { "  {0,-24} {1,-18} saved={2}" -f $_.Cell, $_.Arm, $_.Saved }
}
