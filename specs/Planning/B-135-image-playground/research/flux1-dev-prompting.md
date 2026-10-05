# FLUX.1-dev (fp8, local ComfyUI) — prompting + settings (research)

**Checkpoint:** `flux1-dev-fp8.safetensors` · **Family:** FLUX · **Dialect:** FLUX natural language.
**Profile:** `ImageCompilerProfiles.CheckpointIdentifier = "flux1-dev-fp8.safetensors"`.

## Sources (fetched 2026-10-03)
- `black-forest-labs/FLUX.1-dev` HF model card — the reference diffusers usage and model facts.
- `docs/flux-local-5080-comfyui-setup.md` — the local 5080-host runbook (fp8 split: T5-XXL + CLIP-L + VAE).
- B-112 / B-116 — internal measured findings.

## What the model is
- **12B rectified-flow transformer**, trained with **guidance distillation** (efficient sampling).
- Dual text encoders: **T5-XXL** (the large one) + **CLIP-L**; VAE `ae.safetensors`.
- Non-commercial license (FLUX.1 [dev] Non-Commercial License).

## Settings envelope (authoritative — the HF card's reference diffusers call)
| Setting | Value |
|---|---|
| guidance_scale | **3.5** |
| num_inference_steps | **50** |
| max_sequence_length | **512** (the T5-XXL token window) |
| resolution | 1024×1024 |

The app maps guidance 3.5 onto the ComfyUI envelope `FluxGuidance 3.5 + cfg 1.0 + euler/simple`
(B-112); the 50-step figure is BFL's reference example — the app's workflow supplies steps itself.

## Negative prompt
- **None, and FluxPipeline takes none.** BFL's diffusers `FluxPipeline` has no `negative_prompt`
  parameter and most FLUX models do not support negatives — the profile's empty negative is the
  architecture, not a style choice.

## Prompt shape
- Natural language; `subject, action, setting, framing, lighting`.
- BFL's own limitation note: *"Prompt following is heavily influenced by the prompting-style."*
- Forbidden: story names, relationships, ownership, negation, POV-character-in-frame.
- **Pose in text: SimpleOnly** (B-116 measured: a dictated two-person pose is fragile in text-T2I;
  geometry holds via an img2img re-skin over a pose base, B-119 route C3).

## Token budget — CORRECTED
- MaxTokens was **256 (provisional)**; the HF card's `max_sequence_length=512` pins the T5 window,
  so the profile now uses **512**.
