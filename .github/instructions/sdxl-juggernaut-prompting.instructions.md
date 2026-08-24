---
description: "SDXL / Juggernaut (ComfyUI) prompting rules for the scene image generator — the fully-separate photorealistic path alongside Pony. Validated on the RunPod pod 2026-08-23. Read before building or changing any SDXL/Juggernaut image prompt."
applyTo: DreamGenClone.Web/Application/RolePlay/SdxlSceneImagePromptBuilder.cs,DreamGenClone.Web/Application/RolePlay/ISdxlSceneImagePromptBuilder.cs,DreamGenClone.Domain/RolePlay/SceneImageModelFamily.cs,DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs,DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs,DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs,DreamGenClone.Tests/RolePlay/**/*.cs,helpers/runpod/workflows/**,specs/001-scene-image-generator/**
---

# SDXL / Juggernaut Prompting Rules (Scene Image Generator)

> The photorealistic companion to `pony-v6-prompting.instructions.md`. SDXL-family checkpoints
> (`sd_xl_base_1.0`, **Juggernaut XL**, RealVisXL, ...) read **natural-language photography briefs**,
> NOT Pony's danbooru tag vocabulary. These rules were validated by live generation on the RunPod
> pod 2026-08-23. The Pony code path is **untouched** — SDXL is a fully separate implementation.

## The model's real nature

- **SDXL base / Juggernaut XL are photorealistic SDXL finetunes.** They follow natural-language
  direction far better than Pony and render correct human/gender anatomy (validated: SDXL base
  produced correct man+woman, correct bodies; base SDXL **cannot** render explicit genital anatomy —
  it is trained to avoid it → use **Juggernaut XL** (NSFW-capable) or Pony for explicit genital shots).
- **Juggernaut XL Ragnarok** (`Juggernaut_XL_Ragnarok_ByRunDiffusion.safetensors`, 6.62GB) is the
  recommended photorealistic NSFW-capable model (RAIL++-M license, "Overwhelmingly Positive" on
  Civitai, ~1.9M downloads). Download requires a **Civitai API token** (anonymous = 403 on the R2
  delivery CDN; the token goes in `Authorization: Bearer`).

## Dual-path routing (single decision path, no fallback)

`SceneImageModelFamilyResolver.Classify(checkpoint)` is the **one** router (Domain/RolePlay):
- `pony*` → `Pony` → Pony builder + Pony workflow (CLIP skip 2) — unchanged.
- `juggernaut`/`jugg`/`sd_xl`/`sdxl`/`realvis`/`realistic vision` → `Sdxl` → SDXL builder + SDXL workflow.
- anything else → `Unknown` → **fail fast** with an explicit diagnostic (never a silent default model).

Used by: `SceneImagePromptGenerationJobHandler` (picks the LLM prompt builder), `SceneImageRenderingJobHandler`
(picks the negative builder + SFW clamp suffix), and `ComfyUIImageClient` (picks the workflow + baseline negative).

## Non-negotiable SDXL/Juggernaut rules (all validated)

1. **Natural language, NOT tags.** Write 2-4 short sentences/phrases like a photography brief. Never
   emit `score_9`, `rating_*`, `1girl/1boy/2people` count tags, or danbooru tokens. The SDXL system
   prompt explicitly teaches the model to avoid Pony vocabulary.
2. **State gender + number explicitly** ("a middle-aged man and a middle-aged woman") to prevent
   figure merging/miscounting (SDXL's equivalent of Pony's count tags).
3. **Photographic style cues help:** `photorealistic`, `35mm`, `natural skin texture`, `sharp focus`.
4. **Keep it short** — target under ~800 chars. Long caption prose degrades output.
5. **Honor beat-stated clothing exactly;** nudity only when the beat implies it AND the explicitness
   level allows it.
6. **No CLIP skip.** SDXL/Juggernaut use default CLIP. The Pony `CLIPSetLastLayer` node must NOT be
   present in the SDXL workflow (`ComfyUIImageClient.BuildSdxlWorkflow` has none).
7. **Sampler for Juggernaut:** `dpmpp_2m_sde`, `karras`, 30 steps, CFG 5 (Juggernaut-recommended).
   Pony keeps `euler_ancestral` / 25 / 7.
8. **Heavier negative than Pony** (SDXL needs a bigger guard set):
   `deformed, bad anatomy, extra limbs, extra legs, four legs, fused legs, extra fingers, extra arms,
   missing limbs, malformed hands, malformed feet, blurry genitals, featureless genitals, censored,
   cartoon, anime, illustration, painting, sketch, watermark, text, low quality, oversaturated, plastic skin`.
9. **For explicit content, SDXL needs concrete anatomy language in the positive** ("erect penis
   penetrating her vagina, correct penis and vagina anatomy") AND the genital guards above in the
   negative. Base SDXL avoids genitals even then; Juggernaut renders them correctly.

## Phase → explicitness (SDXL prose, the analogue of the Pony rating tag)

`SdxlSceneImagePromptBuilder.ResolveExplicitnessProse(phase, policy)`:
- BuildUp / Opening → `safe: fully clothed, wholesome, non-explicit`
- Committed / Approaching / Reset → `questionable: partially undressed, suggestive, implied intimacy`
- Climax → `explicit: nude bodies, explicit sexual activity, correct genital anatomy`
- **SFW provider policy is a hard clamp to `safe` regardless of phase.**

## Model Manager steps (Juggernaut)

1. Add Model: DisplayName `Juggernaut XL Ragnarok`, **ModelIdentifier** `Juggernaut_XL_Ragnarok_ByRunDiffusion.safetensors`
   (must EXACTLY match the pod filename — ComfyUI client uses the identifier verbatim as the checkpoint
   when it looks like a filename, else fails fast), Provider `RunPod ComfyUI`, ModelKind `Image`,
   ImageSizeSupported `1024x1024`, Enabled.
2. Set the `RolePlaySceneImage` FunctionDefault → Juggernaut (replaces Pony V6 XL).
3. Provider unchanged: `RunPod ComfyUI`, `ImageCapability=ImageOnly`, `ContentPolicy=AdultAllowed`.
