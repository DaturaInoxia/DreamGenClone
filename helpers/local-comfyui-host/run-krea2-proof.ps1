<#
.SYNOPSIS
  Krea 2 Turbo proof driver for the LOCAL ComfyUI host (WOOD-GAME-MAIN, RTX 5080 16 GB).

.DESCRIPTION
  Answers one question: is Krea 2 usable for explicit adult content on a 16 GB card, and what
  does each unlocked-LoRA add? Builds a CORE-ONLY ComfyUI API graph per cell and submits it
  through helpers/local-comfyui-host/run-local-proof.ps1.

  Graph (mirrors the official Comfy-Org template exactly, minus the prompt enhancer):
      UNETLoader(krea2_turbo_*) -> [LoraLoaderModelOnly chain] -> KSampler
      CLIPLoader(qwen3vl_4b_fp8_scaled, type="krea2") -> CLIPTextEncode -> positive
                                                      -> ConditioningZeroOut -> negative
      EmptyLatentImage -> KSampler(8 steps, cfg 1, euler, simple, denoise 1) -> VAEDecode
      VAELoader(qwen_image_vae)

  DELIBERATE OMISSION: the official template's TextGenerate "prompt enhancement" node is NOT in
  this graph. Its system prompt contains "Rule 8: Respect the Human Form: ... Assume clothing
  covers genitals and intimate anatomy." - it launders NSFW prompts. Any NSFW proof must run
  without it.

  Also note cfg=1 with ConditioningZeroOut means there is NO negative prompt: the LoRA carries
  the content, so strength sweeps matter far more than prompt phrasing.

.PARAMETER Cells
  Which cells to run. Default 'all'.

.PARAMETER OutRoot
  Root output dir. Git-ignored by convention: artifacts/tmp/krea2-nsfw.

.PARAMETER Unet
  Diffusion model file name (default krea2_turbo_fp8_scaled.safetensors).

.PARAMETER RunStamp
  Reuse an existing run folder name instead of creating a new timestamped one.
  results.json is merged by cell id, so batches can share one folder.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-krea2-proof.ps1
  powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-krea2-proof.ps1 -Cells 3,4
#>
[CmdletBinding()]
param(
    [string[]]$Cells = @('all'),
    [string]$OutRoot = 'artifacts/tmp/krea2-nsfw',
    [string]$Unet = 'krea2_turbo_fp8_scaled.safetensors',
    [string]$Clip = 'qwen3vl_4b_fp8_scaled.safetensors',
    [string]$ComfyUiUrl = 'https://comfy.kenacwood.net',
    [int]$TimeoutSec = 1800,
    [string]$RunStamp = '',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot
$runner = Join-Path $PSScriptRoot 'run-local-proof.ps1'
$stamp = if ($RunStamp) { $RunStamp } else { Get-Date -Format 'yyyyMMdd-HHmmss' }

# --- LoRA files staged on the host (ComfyUI/models/loras) -------------------------------
$L = @{
    NsfwV4     = 'krea2_nsfw_v4_v43exp.safetensors'          # Krea 2 NSFW V4 v4.3_EXP  (99k dl, the flagship unpaker)
    Mystic     = 'krea2_mysticxxx_v3.safetensors'            # Mystic XXX v3            (51k dl)
    Adherence  = 'krea2_nsfw_prompt_adherence.safetensors'   # projector-scale adherence fix
    UltraReal  = 'krea2_bloomgirls_ultrarealism.safetensors' # realism layer            (25k dl)
    AnatomyF   = 'krea2_anatomy_pussyhm.safetensors'         # female anatomy detail
    AnatomyB   = 'krea2_anatomy_breastshm.safetensors'       # breast helper
    AnatomyM   = 'krea2_anatomy_dicktator_male.safetensors'  # male anatomy
    ActD33P    = 'krea2_act_deepthroat_v2.safetensors'       # act-specific
    SDetail    = 'krea2_slider_detail.safetensors'
    SRealism   = 'krea2_slider_realism.safetensors'
    SWeight    = 'krea2_slider_weight.safetensors'
}

# Uncensored/abliterated arms - the two censorship levers. Defined BEFORE the matrix because
# individual cells reference these by name. The Krea 2 text encoder is Qwen3-VL 4B (an aligned LLM),
# so TE selection is a lever separate from the diffusion weights.
$TeUncensored = 'qwen3vl_4b_uncensored_fp8_chriscoletech.safetensors'
$TeAbliterated = 'qwen3vl_4b_abliterated_fp8_quiho.safetensors'
$CkUncensored = 'krea2_turbo_uncensored_v11_fp8.safetensors'
$CkMoody      = 'krea2_moodymix_v3_fp8.safetensors'

# --- the matrix ---------------------------------------------------------------------------
# Every cell states its own intent so the report can score it honestly.
$matrix = @(
    [pscustomobject]@{
        Id = 1; Label = 'sfw-control'; W = 1024; H = 1024; Loras = @()
        Intent = 'Baseline sanity: does the pipeline work at all, composition follows the prompt.'
        Prompt = 'Full-body editorial photograph of a woman standing in a sunlit living room, wearing a linen summer dress, looking toward the window, warm natural light, 35mm photograph, shallow depth of field'
    }
    [pscustomobject]@{
        Id = 2; Label = 'nudity-base-nolora'; W = 1024; H = 1024; Loras = @()
        Intent = 'How censored is the BASE model? Same prompt as cell 3 without any unlock LoRA.'
        Prompt = 'Full-body photograph of a completely nude woman standing in a sunlit bedroom, arms relaxed at her sides, natural skin texture, visible nipples, natural body hair, soft morning light, 35mm photograph, photorealistic'
    }
    [pscustomobject]@{
        Id = 3; Label = 'nudity-v4-0.6'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 0.6 })
        Intent = 'Unlock LoRA at 0.6 - first strength of the sweep.'
        Prompt = $null   # same prompt as cell 2
    }
    [pscustomobject]@{
        Id = 4; Label = 'nudity-v4-1.0'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 })
        Intent = 'Unlock LoRA at 1.0 - top of the sweep.'
        Prompt = $null
    }
    [pscustomobject]@{
        Id = 5; Label = 'nudity-mystic-0.8'; W = 1024; H = 1024; Loras = @(@{ File = $L.Mystic; Str = 0.8 })
        Intent = 'A/B the second-biggest unpaker against V4 (same prompt, same seed).'
        Prompt = $null
    }
    [pscustomobject]@{
        Id = 6; Label = 'explicit-act-v4'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }, @{ File = $L.ActD33P; Str = 0.8 })
        Intent = 'Explicit act coverage: does the unpaker carry a specific act, and does the act LoRA sharpen it?'
        Prompt = 'Photorealistic interior bedroom scene, a completely nude adult man and a completely nude adult woman performing fellatio, woman kneeling in front of him in profile view, both adults in their thirties, explicit genital detail, natural skin texture, warm bedside lamp light, 35mm photograph'
    }
    [pscustomobject]@{
        Id = 7; Label = 'male-anatomy'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }, @{ File = $L.AnatomyM; Str = 0.8 })
        Intent = 'Male anatomy correctness (a known weak point of the current stack) - erect penis present and anatomically coherent.'
        Prompt = 'Full-body photograph of a completely nude adult man standing in a bedroom, erect penis clearly visible, natural body hair, athletic build, soft window light, natural skin texture, photorealistic, 35mm photograph'
    }
    [pscustomobject]@{
        Id = 8; Label = 'female-anatomy'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }, @{ File = $L.AnatomyF; Str = 0.8 })
        Intent = 'Female genital detail - anatomical coherence, no mangling.'
        Prompt = 'Close three-quarter view photograph of a completely nude adult woman lying on a bed with her legs apart, explicit vulva detail, natural body hair, natural skin texture, soft warm light, photorealistic, 35mm photograph'
    }
    [pscustomobject]@{
        Id = 9; Label = 'stacked-realism'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }, @{ File = $L.UltraReal; Str = 0.6 })
        Intent = 'Does stacking the realism layer improve skin/anatomy without fighting the unlock LoRA?'
        Prompt = $null   # same as cell 2
    }
    [pscustomobject]@{
        Id = 10; Label = 'implied-softcore'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 0.8 })
        Intent = 'THE ORIGINAL GAP: implied/softcore, non-explicit intimacy with real composition binding.'
        Prompt = 'Photorealistic photograph, an adult couple lying together under a thin white sheet in a sunlit bedroom, her head resting on his chest, her bare shoulder and back visible above the sheet, intimate tender mood, no explicit nudity, warm morning light, 35mm photograph'
    }
    [pscustomobject]@{
        Id = 11; Label = 'frame-explicit-lang'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 0.8 }); Clip = $TeUncensored
        Intent = 'FRAMING PROBE 1: same content as cell 2 but with explicit framing language. Cells 13/15 both returned head-cropped torso framing from "full-body photograph" (2 seeds), so test whether the wording fixes it.'
        Prompt = 'Full body shot, wide angle, the entire figure visible from head to toe, standing upright facing camera, whole body including head and bare feet inside the frame, completely nude woman in a sunlit bedroom, natural skin texture, soft morning light, 35mm photograph, photorealistic'
    }
    [pscustomobject]@{
        Id = 12; Label = 'frame-portrait-ratio'; W = 832; H = 1216; Loras = @(@{ File = $L.NsfwV4; Str = 0.8 }); Clip = $TeUncensored
        Intent = 'FRAMING PROBE 2: fixes the aspect ratio instead of the wording (portrait 832x1216, 1.01 MP). If framing improves here but not in cell 11, the cause is 1:1 square rather than prompt adherence.'
        Prompt = 'Full body photograph of a completely nude woman standing in a sunlit bedroom, entire figure head to toe in frame, arms relaxed at her sides, natural body hair, natural skin texture, soft morning light, 35mm photograph, photorealistic'
    }
)

# --- ARMS: the two censorship levers are INDEPENDENT --------------------------------
# (TE + checkpoint variable names are defined above the matrix.)
$nudityText = ($matrix | Where-Object Id -eq 2).Prompt
$actText    = ($matrix | Where-Object Id -eq 6).Prompt
$maleText   = ($matrix | Where-Object Id -eq 7).Prompt

$matrix += @(
    [pscustomobject]@{ Id = 13; Label = 'arm-te-uncensored'; W = 1024; H = 1024; Loras = @(); Clip = $TeUncensored
        Intent = 'TE lever alone: stock Turbo + uncensored text encoder, no LoRA. If the TE is the censor, this alone changes the output.'
        Prompt = $nudityText }
    [pscustomobject]@{ Id = 14; Label = 'arm-te-abliterated'; W = 1024; H = 1024; Loras = @(); Clip = $TeAbliterated
        Intent = 'Second TE vendor A/B against cell 13 (same weights, different abliteration).'
        Prompt = $nudityText }
    [pscustomobject]@{ Id = 15; Label = 'arm-te+lora'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $TeUncensored
        Intent = 'Both levers: uncensored TE + NSFW V4 on stock weights - the expected fully-capable config.'
        Prompt = $nudityText }
    [pscustomobject]@{ Id = 16; Label = 'arm-ck-uncensored'; W = 1024; H = 1024; Loras = @(); Unet = $CkUncensored
        Intent = 'CHECKPOINT A/B (no LoRA): uncensored finetune + stock TE + the BRIEF prompt shape, against cell 29 (same prompt, stock weights, no LoRA). Is the finetune better than base when nothing else differs?'
        Prompt = 'Full-body editorial photograph of a nude woman standing in a sunlit bedroom, turning her head to look toward the window, warm natural light on her bare skin, 35mm photograph, shallow depth of field' }
    [pscustomobject]@{ Id = 17; Label = 'arm-ck-moody'; W = 1024; H = 1024; Loras = @(); Unet = $CkMoody
        Intent = 'CHECKPOINT A/B vendor 2 (Quiho moody mix) + stock TE + brief shape, against cells 16 and 29.'
        Prompt = 'Full-body editorial photograph of a nude woman standing in a sunlit bedroom, turning her head to look toward the window, warm natural light on her bare skin, 35mm photograph, shallow depth of field' }
    [pscustomobject]@{ Id = 18; Label = 'arm-ck-act-nolora'; W = 1024; H = 1024; Loras = @(); Unet = $CkUncensored
        Intent = 'CEILING TEST - explicit act with NO LoRA at all: uncensored checkpoint + stock TE + brief-shaped act prompt. Cell 6 needed V4 + an act LoRA; if this works alone the finetune is carrying the act.'
        Prompt = 'Editorial photograph of an adult couple in a sunlit bedroom, the woman kneeling in front of the man performing fellatio, both turning their heads toward the window light, explicit genital detail, warm natural light on bare skin, 35mm photograph, shallow depth of field' }
    [pscustomobject]@{ Id = 19; Label = 'arm-ck+brief-act'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Unet = $CkUncensored
        Intent = 'Best case on the finetune: uncensored checkpoint + NSFW V4 @1.0 + stock TE + brief act prompt. Compare against cell 6 (base + V4 + act LoRA) and cell 30.'
        Prompt = 'Editorial photograph of an adult couple in a sunlit bedroom, the woman kneeling in front of the man performing fellatio, both turning their heads toward the window light, explicit genital detail, warm natural light on bare skin, 35mm photograph, shallow depth of field' }
    [pscustomobject]@{ Id = 20; Label = 'arm-ck-male-brief'; W = 1024; H = 1024; Loras = @(@{ File = $L.AnatomyM; Str = 0.8 }); Unet = $CkUncensored
        Intent = 'Male anatomy on the finetune, brief shape, stock TE - the known weak point of the previous stack, against cell 7.'
        Prompt = 'Full-body editorial photograph of a nude adult man standing in a sunlit bedroom, turning his head to look toward the window, erect penis clearly visible, warm natural light on his bare skin, 35mm photograph, shallow depth of field' }
)

# --- FRAMING ROUND 2 --------------------------------------------------------------------
# Cells 11/12 result: the crop is NOT a wording problem and NOT fixed by portrait ratio.
#   cell 11 (1:1, explicit "head to toe" wording) -> feet recovered, head still cropped.
#   cell 12 (832x1216 portrait)                   -> WORSE: cut at the neck AND the thighs.
# Remaining hypotheses, one cell each: landscape canvas, camera-distance language, canvas pixels.
$frameText = ($matrix | Where-Object Id -eq 12).Prompt
$matrix += @(
    [pscustomobject]@{ Id = 21; Label = 'frame-landscape-ratio'; W = 1216; H = 832; Loras = @(@{ File = $L.NsfwV4; Str = 0.8 }); Clip = $TeUncensored
        Intent = 'FRAMING 2a: landscape canvas, wording held identical to cell 12 so only W/H changes. Portrait made it tighter, so landscape is the untested direction.'
        Prompt = $frameText }
    [pscustomobject]@{ Id = 22; Label = 'frame-distant-lang'; W = 1216; H = 832; Loras = @(@{ File = $L.NsfwV4; Str = 0.8 }); Clip = $TeUncensored
        Intent = 'FRAMING 2b: same landscape canvas plus explicit camera-distance language ("far from the camera, small in frame"), to test whether the crop is a scale prior rather than a ratio prior.'
        Prompt = 'Long shot from across the room, the nude woman stands far from the camera at the far side of the bedroom, her whole body small in the frame, entire head and bare feet visible with empty space above her head, full room visible around her, natural skin texture, soft morning light, 35mm photograph, photorealistic' }
    [pscustomobject]@{ Id = 23; Label = 'frame-2k-square'; W = 1536; H = 1536; Loras = @(@{ File = $L.NsfwV4; Str = 0.8 }); Clip = $TeUncensored
        Intent = 'FRAMING 2c: 1536-square at 2.25x the pixels. If the body is drawn at a fixed fraction of canvas height, more pixels reveal the head. Also a 16 GB VRAM ceiling probe for the fp8 model.'
        Prompt = $frameText }
)

# --- FRAMING ROUND 3: isolate WHY the head is missing ------------------------------------
# Round 2 result: the head is not cropped by the canvas - it is ABSENT. Landscape, portrait and
# 1536-square all lose it; the "far from the camera" wording shrank the body as asked and the
# head never appeared (clean neck stub). So this is a composition prior, not a framing problem.
# Two candidate causes, one cell each: the face is never requested, or nudity itself suppresses it.
$matrix += @(
    [pscustomobject]@{ Id = 24; Label = 'frame-face-forced'; W = 1216; H = 832; Loras = @(@{ File = $L.NsfwV4; Str = 0.8 }); Clip = $TeUncensored
        Intent = 'FRAMING 3a: cause = the face is simply never asked for. Names the face/head/gaze explicitly and puts the head FIRST in the prompt, on the canvas that framed worst.'
        Prompt = 'Head to toe portrait: her face and eyes clearly visible and in focus as she looks at the camera, head and full nude body both inside the frame, she stands far enough away that her whole figure from the top of her head to her bare feet is small in the frame with space above her head, sunlit bedroom, natural skin texture, soft morning light, 35mm photograph, photorealistic' }
    [pscustomobject]@{ Id = 25; Label = 'frame-clothed-control'; W = 1024; H = 1024; Loras = @(); Clip = $TeUncensored
        Intent = 'FRAMING 3b: cause = nudity itself suppresses the head. Identical scene and subject, fully clothed. If the head appears here and vanishes in cell 13 (same settings, nude), nudity is the suppressor.'
        Prompt = 'Full-body photograph of a woman fully dressed in a long grey dress standing in a sunlit bedroom, arms relaxed at her sides, entire figure head to toe in frame, natural skin texture, soft morning light, 35mm photograph, photorealistic' }
)

# --- TEXT-ENCODER CONTROL (the confound) -------------------------------------------------
# Every cell judged so far - including the CLOTHED control cell 25 - used a community-modified
# Qwen3-VL text encoder (uncensored or abliterated). The head crop/omission is therefore NOT yet
# attributable to the base model: a degraded abliterated LLM is a candidate cause.
# Cell 26 is cell 25 with the ONE variable changed back: the official stock text encoder.
$matrix += @(
    [pscustomobject]@{ Id = 26; Label = 'te-control-clothed-stock'; W = 1024; H = 1024; Loras = @(); Clip = $Clip
        Intent = 'TE CONFOUND CONTROL: byte-for-byte the same prompt and settings as cell 25, stock official text encoder. Cell 25 (uncensored TE) cropped the head; if this cell frames a whole figure, the modified TE is the cause, not the model.'
        Prompt = 'Full-body photograph of a woman fully dressed in a long grey dress standing in a sunlit bedroom, arms relaxed at her sides, entire figure head to toe in frame, natural skin texture, soft morning light, 35mm photograph, photorealistic' }
)

# --- FILTER-BYPASS MICRO-LORA vs THE CENSOR ------------------------------------------------
# Established earlier: the Qwen3-VL text encoder is the censor, and swapping it for a community
# abliteration is what unlocks nudity. `krea2_filter_bypass3.safetensors` is a 160-byte LoRA whose
# ONLY tensor is `diffusion_model.txtfusion.projector.diff` (F32, shape [1,12]) - a direct patch to
# the projection that carries text conditioning into the DiT. If it neutralises the censor bias,
# we get the unlock on FULLY STOCK weights + FULLY STOCK text encoder, which is the cleanest
# possible configuration for the app (no community-modified LLM in the model path).
$L.Bypass = 'krea2_filter_bypass3.safetensors'
$matrix += @(
    [pscustomobject]@{ Id = 27; Label = 'bypass-micro-stock'; W = 1024; H = 1024; Loras = @(@{ File = $L.Bypass; Str = 1.0 }); Clip = $Clip
        Intent = 'Is the 160-byte projector patch enough on its own? Stock weights + STOCK text encoder + bypass micro-LoRA only, no NSFW LoRA. Compare directly against cell 2 (same config without it).'
        Prompt = $nudityText }
    [pscustomobject]@{ Id = 28; Label = 'bypass-micro+v4'; W = 1024; H = 1024; Loras = @(@{ File = $L.Bypass; Str = 1.0 }, @{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'Bypass micro-LoRA stacked with NSFW V4, stock weights and stock TE. If this matches cell 4, the app can ship Krea 2 with no community-modified text encoder at all.'
        Prompt = $nudityText }
)

# --- WHY CELL 1 FRAMES AND EVERYTHING ELSE DOES NOT ---------------------------------------
# Cell 1 (stock TE, no LoRA) returned a PERFECT head-to-toe figure - face, dress, bare feet,
# whole room. Cell 2 (stock TE, no LoRA, nude) lost the head. Same model, same graph, same
# resolution; only the prompt shape differs:
#   cell 1  = a photographic BRIEF: medium/format + subject + a subject ACTION engaging the
#             scene ("looking toward the window") + lens language + depth of field.
#   cell 2  = a body-centred NOUN LIST ("a completely nude woman standing ... arms relaxed at
#             her sides, natural skin texture, visible nipples ...") with no action, no lens
#             framing. The explicit "entire figure head to toe in frame" demands added later
#             (cells 11/12/21/23/25/26) ALL failed too.
# Cell 29 keeps the exact nude content of cell 2 but writes it in cell 1's brief shape, changing
# nothing else. If the head appears, the app's prompt compiler - not the model - is the fix.
$briefText = 'Full-body editorial photograph of a nude woman standing in a sunlit bedroom, turning her head to look toward the window, warm natural light on her bare skin, 35mm photograph, shallow depth of field'
$matrix += @(
    [pscustomobject]@{ Id = 29; Label = 'brief-nude-stock'; W = 1024; H = 1024; Loras = @(); Clip = $Clip
        Intent = 'THE COMPILER TEST: identical nudity to cell 2, but written as a photographic brief (format + action + lens + depth of field) instead of a body noun-list. Head present here = the fix is the prompt compiler, not the model.'
        Prompt = $briefText }
    [pscustomobject]@{ Id = 30; Label = 'brief-nude-v4'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'The same brief shape plus NSFW V4 at 1.0 - the candidate SHIPPING config if the brief shape holds and V4 adds genital detail.'
        Prompt = $briefText }
)

# --- MULTI-CHARACTER / POSITION SUITE -----------------------------------------------------
# Open question: does Krea 2 hold TWO people in a named sex position? All 33 cells so far are
# either one figure or one act (cell 6/19 = fellatio). These run on the candidate SHIPPING config
# (stock weights + stock TE + NSFW V4 @1.0) with the brief shape, because that is what the app
# would ship. Both people must survive: two heads, two faces, no merged/duplicated anatomy, and
# the NAMED position must be recognisable - not merely "an intimate arrangement".
# NOTE: Krea 2 has no ControlNet, so this is POSE-IN-TEXT (the app's `PoseInText` profile flag),
# not the OpenPose pose-library path used by the SDXL/Juggernaut suites.
$cpl = 'Editorial photograph of an adult couple in a sunlit bedroom, both turning their heads toward the window light, warm natural light on bare skin, 35mm photograph, shallow depth of field'
$matrix += @(
    [pscustomobject]@{ Id = 31; Label = 'mc-sfw-baseline'; W = 1024; H = 1024; Loras = @(); Clip = $Clip
        Intent = 'MULTI-CHARACTER SFW BASELINE: two clothed adults in one frame, no act. Establishes whether two complete people compose at all before adding any sex.'
        Prompt = 'Editorial photograph of an adult couple standing together in a sunlit bedroom, both fully dressed, the man with his arm around the woman waist, both turning their heads toward the window light, warm natural light, 35mm photograph, shallow depth of field' }
    [pscustomobject]@{ Id = 32; Label = 'mc-nsfw-baseline'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'MULTI-CHARACTER NSFW BASELINE: two nude adults standing together, NO act. Isolates two-nude-body coherence from position adherence.'
        Prompt = 'Editorial photograph of an adult couple standing together in a sunlit bedroom, both completely nude, the man with his arm around the woman waist, both turning their heads toward the window light, warm natural light on bare skin, 35mm photograph, shallow depth of field' }
    [pscustomobject]@{ Id = 33; Label = 'pos-missionary'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'POSITION missionary: the man on top of the woman who lies on her back with her knees raised, having sex. Both faces should remain visible.'
        Prompt = "$cpl, the man on top of the woman as she lies on her back with her knees raised, missionary position, they are having sex, explicit genital detail, both faces clearly visible" }
    [pscustomobject]@{ Id = 34; Label = 'pos-doggy'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'POSITION doggy: the woman on all fours with the man kneeling behind her, having sex from behind.'
        Prompt = "$cpl, the woman on all fours on the bed with the man kneeling behind her, doggy style, they are having sex from behind, explicit genital detail, both faces clearly visible" }
    [pscustomobject]@{ Id = 35; Label = 'pos-cowgirl'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'POSITION cowgirl: the woman sitting astride the man who lies on his back, having sex.'
        Prompt = "$cpl, the woman straddling and sitting on top of the man who lies on his back, cowgirl position, they are having sex, explicit genital detail, both faces clearly visible" }
    [pscustomobject]@{ Id = 36; Label = 'pos-oral-female-gives'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'POSITION oral, the woman giving: kneeling in front of the standing man performing fellatio. Re-tests cell 6/19 in the brief shape as the control for the set.'
        Prompt = "$cpl, the woman kneeling in front of the man performing fellatio on him, explicit genital detail, both faces clearly visible" }
    [pscustomobject]@{ Id = 37; Label = 'pos-oral-male-gives'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'POSITION oral, the man giving: he performs cunnilingus on the woman, who lies back with her hips raised to his face.'
        Prompt = "$cpl, the man lying between the woman thighs performing cunnilingus on her as she lies back with her hips raised to his face, explicit genital detail, both faces clearly visible" }
)

# --- ISOLATING THE TWO ORAL MISSES ---------------------------------------------------------
# Result on the shipping config (base weights + V4@1.0): missionary/doggy/cowgirl PASS, but
# cell 36 (woman gives oral) returned an embrace and cell 37 (man gives oral) only got the posture.
# Cells 6 and 19 DID render fellatio, so exactly two variables differ. One cell each:
#   38 -> same prompt as the FAILING cell 36, but on the UNCENSORED CHECKPOINT   (isolates weights)
#   39 -> same base weights as cell 36, but the WORKING cell-19 phrasing         (isolates wording)
#   40 -> base + the ACT LoRA that cell 6 used, with the failing cell-36 prompt  (isolates the LoRA)
$oralFailing = "$cpl, the woman kneeling in front of the man performing fellatio on him, explicit genital detail, both faces clearly visible"
$oralWorking = 'Editorial photograph of an adult couple in a sunlit bedroom, the woman kneeling in front of the man performing fellatio, both turning their heads toward the window light, explicit genital detail, warm natural light on bare skin, 35mm photograph, shallow depth of field'
$matrix += @(
    [pscustomobject]@{ Id = 38; Label = 'diag-oral-ck'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Unet = $CkUncensored; Clip = $Clip
        Intent = 'DIAGNOSTIC (weights?): cell 36 prompt verbatim on the uncensored checkpoint. If the act appears, the failure was the base weights, not the words.'
        Prompt = $oralFailing }
    [pscustomobject]@{ Id = 39; Label = 'diag-oral-phrasing'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'DIAGNOSTIC (wording?): cell 36 weights, but the cell-19 phrasing that worked. If the act appears, the failure was the prompt wording (act clause before the head-turn clause).'
        Prompt = $oralWorking }
    [pscustomobject]@{ Id = 40; Label = 'diag-oral-actlora'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }, @{ File = $L.ActD33P; Str = 0.8 }); Clip = $Clip
        Intent = 'DIAGNOSTIC (LoRA?): cell 36 weights and wording, plus the act LoRA that cell 6 used successfully. If the act appears, oral acts need the act LoRA on base weights.'
        Prompt = $oralFailing }
)

# --- CORRECTING MY OWN DIAGNOSTIC ----------------------------------------------------------
# Cells 36/39/40 were NOT a clean weights-vs-wording test: all three carried the same ambiguous
# clause "the woman kneeling in front of the man performing fellatio on him", which parses as
# "the man who is performing fellatio". So they re-tested the same broken grammar three times.
# Cell 6, which DID work on base weights, instead named both people as the actors:
#   "a completely nude adult man and a completely nude adult woman performing fellatio,
#    woman kneeling in front of him in profile view"
# These two cells re-run the oral pair on the SHIPPING config with unambiguous grammar, which is
# the control the earlier diagnostic was missing.
$matrix += @(
    [pscustomobject]@{ Id = 41; Label = 'oral-f-clean-grammar'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'CLEAN CONTROL: base weights + V4 + unambiguous fellatio grammar naming BOTH adults as the actors (the exact structure cell 6 used). If the act appears here, the earlier oral misses were my prompt grammar, not the model.'
        Prompt = 'Editorial photograph of an adult couple in a sunlit bedroom, an adult man and an adult woman performing fellatio, the woman kneeling in front of him with his penis in her mouth, in profile view, both turning their heads toward the window light, explicit genital detail, warm natural light on bare skin, 35mm photograph, shallow depth of field' }
    [pscustomobject]@{ Id = 42; Label = 'oral-m-clean-grammar'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'CLEAN CONTROL: base weights + V4 + unambiguous cunnilingus grammar naming BOTH adults as the actors, with her hips raised to his mouth.'
        Prompt = 'Editorial photograph of an adult couple in a sunlit bedroom, an adult man and an adult woman performing cunnilingus, the man lying face down between her thighs with his mouth on her vulva while she lies on her back with her hips raised to his face, both turning their heads toward the window light, explicit genital detail, warm natural light on bare skin, 35mm photograph, shallow depth of field' }
)

# Cell 41 rendered a KISS instead of fellatio, which points at my own shared couple template: it ends
# with "both turning their heads toward the window light", a clause that pulls the two faces TOGETHER
# and fights any act. Cell 38 (checkpoint) overrode that clause; base weights did not. Cell 43 removes
# the clause and uses cell 6's act-specific wording, on base weights with NO act LoRA, to settle
# whether the clause or the weights caused the substitution.
$matrix += @(
    [pscustomobject]@{ Id = 43; Label = 'oral-f-no-headturn-clause'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'Is my own template clause the culprit? Cell 6 wording (which worked on base weights) with NO "turning their heads" clause and NO act LoRA. If the act renders, the shared $cpl clause was suppressing it all along.'
        Prompt = 'Photorealistic interior bedroom scene, a completely nude adult man and a completely nude adult woman performing fellatio, woman kneeling in front of him in profile view, both adults in their thirties, explicit genital detail, natural skin texture, warm bedside lamp light, 35mm photograph' }
)

# --- VERIFY THE AUTHORED CATALOG TEXT ------------------------------------------------------
# Cells 44-48 render the krea2 variants straight out of specs/image-generator-tests/baseline so the
# text under test IS the text in the catalog, not a hand-copied duplicate. This is how the new
# woman-receiving-oral cells and the 1F "erotic" shape get validated instead of merely written.
$catRoot = 'specs/image-generator-tests/baseline/positions'
function Get-Krea2Prompt([string]$id) {
    $p = Join-Path $catRoot "$id.json"
    if (-not (Test-Path $p)) { throw "catalog position '$id' not found at $p" }
    $j = Get-Content -Raw $p | ConvertFrom-Json
    $v = $j.variants.krea2
    if (-not $v) { throw "catalog position '$id' has no krea2 variant" }
    return $v
}
$matrix += @(
    [pscustomobject]@{ Id = 44; Label = 'cat-cunnilingus'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'CATALOG TEXT, new cell: cunnilingus (1M1F). Does the authored brief render the act on the shipping config?'
        Prompt = (Get-Krea2Prompt 'cunnilingus') }
    [pscustomobject]@{ Id = 45; Label = 'cat-cunnilingus-closeup'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'CATALOG TEXT, new cell: cunnilingus-closeup (1M1F). Tight contact framing must still resolve to the act, not a torso.'
        Prompt = (Get-Krea2Prompt 'cunnilingus-closeup') }
    [pscustomobject]@{ Id = 46; Label = 'cat-facesitting'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'CATALOG TEXT, new cell: facesitting (1M1F). She receives oral sitting on his face - the third of the new cells.'
        Prompt = (Get-Krea2Prompt 'facesitting') }
    [pscustomobject]@{ Id = 47; Label = 'cat-erotic-legs-spread'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'CATALOG TEXT, existing 1F cell: the riskiest shape I authored. A single woman with no partner to act against - does the brief hold up, or does it fall into the torso-crop mode?'
        Prompt = (Get-Krea2Prompt 'erotic-legs-spread') }
    [pscustomobject]@{ Id = 48; Label = 'cat-flash-shirt-lifted'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'CATALOG TEXT, existing 1F SFW-adjacent cell: clothed-with-exposure flash. Checks the brief shape survives a non-nude, action-led single-subject prompt.'
        Prompt = (Get-Krea2Prompt 'flash-shirt-lifted-bare-breasts') }
)

# --- VERIFY THE GEOMETRY FIXES -------------------------------------------------------------
# Cells 49-54 re-render the prompts rewritten by the validation pass, again straight out of the
# catalog. Each one was self-contradictory before: two occluded contacts in one frame (69,
# spitroast close-up), an unviewable whole-pose inside an extreme close-up (cowgirl), an entry
# asked to be visible from an occluding angle (spooning), or an implausible pose (double
# penetration). The fix is only real if the image is.
$matrix += @(
    [pscustomobject]@{ Id = 49; Label = 'fix-69'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'FIX VERIFY: 69 with ONE visible contact. Did dropping the second, occluded oral contact produce a readable 69 instead of a confused tangle?'
        Prompt = (Get-Krea2Prompt '69') }
    [pscustomobject]@{ Id = 50; Label = 'fix-mmf-dp'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'FIX VERIFY: double penetration restated as the coherent pose (one behind, one beneath) instead of two men both kneeling behind her.'
        Prompt = (Get-Krea2Prompt 'mmf-double-penetration') }
    [pscustomobject]@{ Id = 51; Label = 'fix-spitroast-closeup'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'FIX VERIFY: spitroast close-up reduced to the oral contact in front, rear man described positionally rather than asked to be visible.'
        Prompt = (Get-Krea2Prompt 'mmf-spitroast-closeup') }
    [pscustomobject]@{ Id = 52; Label = 'fix-facesitting'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'FIX VERIFY: facesitting re-stated as her knees either side of his head with his face pressed between her thighs. The first attempt put her above his torso.'
        Prompt = (Get-Krea2Prompt 'facesitting') }
    [pscustomobject]@{ Id = 53; Label = 'fix-cowgirl-pen-closeup'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'FIX VERIFY: cowgirl penetration close-up with the unviewable whole-pose sentence removed, leaving only in-frame anchors.'
        Prompt = (Get-Krea2Prompt 'cowgirl-penetration-closeup') }
    [pscustomobject]@{ Id = 54; Label = 'fix-spooning-pen-closeup'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }); Clip = $Clip
        Intent = 'FIX VERIFY: spooning penetration close-up with the camera moved to behind-and-below, the only angle where the entry is not occluded by her own buttocks.'
        Prompt = (Get-Krea2Prompt 'spooning-penetration-closeup') }
)

# --- GROUNDED ACT LoKrs (ethanfel/Krea2-NSFW-Grounded-IC-LoKr) -----------------------------
# Five 6.94 MB LoKr adapters arrived with the extras: Cowgirl-POV, Missionary-POV, RearEntry-POV,
# Lying-Oral-POV, Mating-Press. They are "grounded" per-act keys, i.e. the act-adherence lever that
# the plain NSFW V4 LoRA does not provide (cell 18/41/43 could not render oral on base weights).
# Each cell pairs one POV key with its matching CATALOG prompt, so this tests the shipped text.
$matrix += @(
    [pscustomobject]@{ Id = 55; Label = 'lokr-lying-oral'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }, @{ File = 'krea2_act_Lying-Oral-POV-v1-step0700.safetensors'; Str = 1.0 }); Clip = $Clip
        Intent = 'Does the Lying-Oral LoKr close the ORAL-ACT gap? Cell 41/43 could not render fellatio on base weights with V4 alone. Catalog fellatio text, unchanged.'
        Prompt = (Get-Krea2Prompt 'fellatio') }
    [pscustomobject]@{ Id = 56; Label = 'lokr-missionary'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }, @{ File = 'krea2_act_Missionary-POV-v1-step0700.safetensors'; Str = 1.0 }); Clip = $Clip
        Intent = 'Missionary LoKr on the catalog missionary text - does a grounded pose key beat plain V4 on position adherence?'
        Prompt = (Get-Krea2Prompt 'missionary') }
    [pscustomobject]@{ Id = 57; Label = 'lokr-rearentry'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }, @{ File = 'krea2_act_RearEntry-POV-v1-step0800.safetensors'; Str = 1.0 }); Clip = $Clip
        Intent = 'RearEntry LoKr on the catalog doggy text - the rear-entry key should sharpen the behind-penetration pose.'
        Prompt = (Get-Krea2Prompt 'doggy') }
    [pscustomobject]@{ Id = 58; Label = 'lokr-cowgirl'; W = 1024; H = 1024; Loras = @(@{ File = $L.NsfwV4; Str = 1.0 }, @{ File = 'krea2_act_Cowgirl-POV-v1-step0800.safetensors'; Str = 1.0 }); Clip = $Clip
        Intent = 'Cowgirl LoKr on the catalog cowgirl text - A/B against cell 35 (V4 only) which already passed, to see what the key adds.'
        Prompt = (Get-Krea2Prompt 'cowgirl') }
)

# NOTE: `powershell -File <script> -Cells 13,15` binds the WHOLE comma list as ONE string (unlike a
# cmdlet call). `[int]'13,15'` then parses the comma as a THOUSANDS SEPARATOR and yields 1315 - a
# valid number that quietly matches no cell, so the loop runs zero times with no error. Split first.
$want = if ($Cells -contains 'all') {
    $matrix.Id
} else {
    @($Cells |
        ForEach-Object { $_ -split ',' } |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne '' } |
        ForEach-Object { [int]$_ })
}
if ($want.Count -eq 0) { throw "No cells selected (Cells='$($Cells -join '|')')." }
$seedBase = 20261001

function New-Krea2Workflow {
    param([string]$Prompt, [int]$W, [int]$H, [object[]]$Loras, [string]$UnetName, [string]$ClipName, [int]$Seed)
    $wf = [ordered]@{}
    $wf['1'] = @{ class_type = 'UNETLoader'; inputs = @{ unet_name = $UnetName; weight_dtype = 'default' } }
    $modelRef = @('1', 0)
    $i = 2
    foreach ($lo in $Loras) {
        $wf["$i"] = @{ class_type = 'LoraLoaderModelOnly'; inputs = @{ model = $modelRef; lora_name = $lo.File; strength_model = [double]$lo.Str } }
        $modelRef = @("$i", 0)
        $i++
    }
    $clipId = "$i"; $i++
    $wf[$clipId] = @{ class_type = 'CLIPLoader'; inputs = @{ clip_name = $ClipName; type = 'krea2'; device = 'default' } }
    $posId = "$i"; $i++
    $wf[$posId] = @{ class_type = 'CLIPTextEncode'; inputs = @{ clip = @($clipId, 0); text = $Prompt } }
    $zeroId = "$i"; $i++
    $wf[$zeroId] = @{ class_type = 'ConditioningZeroOut'; inputs = @{ conditioning = @($posId, 0) } }
    $latId = "$i"; $i++
    $wf[$latId] = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $W; height = $H; batch_size = 1 } }
    $ksId = "$i"; $i++
    $wf[$ksId] = @{ class_type = 'KSampler'; inputs = @{
            model = $modelRef; positive = @($posId, 0); negative = @($zeroId, 0); latent_image = @($latId, 0)
            seed = $Seed; steps = 8; cfg = 1; sampler_name = 'euler'; scheduler = 'simple'; denoise = 1 } }
    $vaeId = "$i"; $i++
    $wf[$vaeId] = @{ class_type = 'VAELoader'; inputs = @{ vae_name = 'qwen_image_vae.safetensors' } }
    $decId = "$i"; $i++
    $wf[$decId] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @($ksId, 0); vae = @($vaeId, 0) } }
    $svId = "$i"
    $wf[$svId] = @{ class_type = 'SaveImage'; inputs = @{ images = @($decId, 0); filename_prefix = 'krea2proof' } }
    return $wf
}

$nudityPrompt = ($matrix | Where-Object Id -eq 2).Prompt
$runDir = Join-Path $OutRoot "$stamp"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$tmpWfDir = Join-Path $runDir 'workflows'
New-Item -ItemType Directory -Force -Path $tmpWfDir | Out-Null
# Accumulate across batches that share the same RunStamp (results.json is merged by cell id).
$results = @()
$resultsPath = Join-Path $runDir 'results.json'
if (Test-Path $resultsPath) {
    # PS 5.1 does not enumerate a JSON array into the pipeline, so use foreach and
    # self-heal files previously written with one level of nesting.
    try {
        $loaded = Get-Content -Raw $resultsPath | ConvertFrom-Json
        foreach ($r in $loaded) {
            if ($r -is [System.Array]) {
                foreach ($r2 in $r) { if ($r2.PSObject.Properties.Name -contains 'Cell') { $results += $r2 } }
            } elseif ($r.PSObject.Properties.Name -contains 'Cell') { $results += $r }
        }
    } catch { $results = @() }
}

foreach ($c in $matrix | Where-Object { $_.Id -in $want -and -not $_.Skip }) {
    $prompt = if ($c.Prompt) { $c.Prompt } else { $nudityPrompt }
    $seed = $seedBase + $c.Id
    $cellDir = Join-Path $runDir ("{0:d2}-{1}" -f $c.Id, $c.Label)
    New-Item -ItemType Directory -Force -Path $cellDir | Out-Null
    $unetName = if ($c.PSObject.Properties.Name -contains 'Unet' -and $c.Unet) { $c.Unet } else { $Unet }
    $clipName = if ($c.PSObject.Properties.Name -contains 'Clip' -and $c.Clip) { $c.Clip } else { $Clip }
    $wf = New-Krea2Workflow -Prompt $prompt -W $c.W -H $c.H -Loras $c.Loras -UnetName $unetName -ClipName $clipName -Seed $seed
    $wfFile = Join-Path $tmpWfDir ("{0:d2}-{1}.workflow.json" -f $c.Id, $c.Label)
    $wf | ConvertTo-Json -Depth 12 | Set-Content -Path $wfFile -Encoding utf8

    $loraDesc = if ($c.Loras.Count -eq 0) { '(none)' } else { ($c.Loras | ForEach-Object { "$($_.File)@$($_.Str)" }) -join ' + ' }
    Write-Host ''
    Write-Host ('=' * 100)
    Write-Host ("CELL {0} [{1}]  {2}x{3}  seed={4}" -f $c.Id, $c.Label, $c.W, $c.H, $seed)
    Write-Host ("INTENT : {0}" -f $c.Intent)
    Write-Host ("LORAS  : {0}" -f $loraDesc)
    Write-Host ("PROMPT : {0}" -f $prompt)
    Write-Host ('=' * 100)

    if ($DryRun) { continue }

    try {
        & powershell -ExecutionPolicy RemoteSigned -File $runner -WorkflowPath $wfFile -OutDir $cellDir -Prefix ("c{0:d2}-{1}" -f $c.Id, $c.Label) -ComfyUiUrl $ComfyUiUrl -TimeoutSec $TimeoutSec -Seed $seed
        $saved = @(Get-ChildItem $cellDir -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in '.png', '.jpg', '.jpeg', '.webp' })
        $results = @($results | Where-Object { $_.Cell -ne $c.Id })
        $results += [pscustomobject]@{ Cell = $c.Id; Label = $c.Label; Intent = $c.Intent; Loras = $loraDesc; Prompt = $prompt; Seed = $seed; Dir = $cellDir; Saved = $saved.Count }
    } catch {
        Write-Host "CELL $($c.Id) FAILED: $($_.Exception.Message)"
        $results = @($results | Where-Object { $_.Cell -ne $c.Id })
        $results += [pscustomobject]@{ Cell = $c.Id; Label = $c.Label; Intent = $c.Intent; Loras = $loraDesc; Prompt = $prompt; Seed = $seed; Dir = $cellDir; Saved = 0 }
    }
}

if (-not $DryRun) {
    $results = @($results | Sort-Object Cell)
    $results | ConvertTo-Json -Depth 6 | Set-Content -Path $resultsPath -Encoding utf8
    Write-Host ''
    Write-Host "=== RUN SUMMARY ($runDir) ==="
    $results | ForEach-Object { "  cell {0,2}  {1,-22} saved={2}  {3}" -f $_.Cell, $_.Label, $_.Saved, $_.Loras }
}
