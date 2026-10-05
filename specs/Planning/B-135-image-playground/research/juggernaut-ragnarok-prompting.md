# Juggernaut XL Ragnarok — prompting + settings (research)

**Checkpoint:** `juggernautXL_ragnarok.safetensors` · **Family:** SDXL · **Dialect:** SDXL natural-language photographic brief.
**Profile:** `ImageCompilerProfiles.CheckpointIdentifier = "juggernautXL_ragnarok.safetensors"`.

## Sources (consolidated in `scene-image-prompt-compiler-standards.instructions.md` §3.1, §6; fetched 2026-09-01)
- RunDiffusion — Juggernaut XIII Ragnarok Prompt Guide — `https://www.rundiffusion.com/prompt-guide-for-juggernaut-xiii-ragnarok-by-rundiffusion`
- Civitai Juggernaut XL model page (Kandoo / Team Juggernaut) — `https://civitai.com/models/133005/juggernaut-xl`
- Stable Diffusion Art — SDXL prompts — `https://stable-diffusion-art.com/sdxl-prompt/`

## What the model is
- Final SDXL release of the Juggernaut line; photorealistic; RAIL++-M. The Civitai page states the
  known SDXL limitation directly: **weak text and faces at a distance** — a distant/shadowed subject
  in a wide shot is a model limitation, not a prompt typo.

## Settings envelope (author)
| Setting | Value |
|---|---|
| Resolution | 832×1216 portrait (any SDXL native ≥1024) |
| Sampler | **DPM++ 2M SDE** (or DPM++ 2M Karras) |
| Steps | 30–40 |
| CFG | 3–6 (lower = more realistic) |
| Negative | start minimal / none (author negatives are SFW-avoidance, not applicable here) |
| VAE | baked in |
| HiRes | 4xNMKD-Siax_200k, 15 steps, 0.3 denoise, 1.5× |
| Keyword weights | ≤ ~1.4, sparingly |

**Production values (`ComfyUIImageClient.BuildSdxlWorkflow`):** `dpmpp_2m_sde` / `karras` / 30 steps / CFG 5.0 / 832×1216.

## Prompt anatomy (the 17 components — §2.1)
Subject first → action → setting/objects → color → style → mood → lighting → perspective →
texture → (optional era/culture) → emotion → medium → **clothing** → text → explicitness.
Rules that matter for the compiler: first sentence sets framing + subject; ≤~75 tokens / 600–800
chars; weights ≤1.4; clothing is a safety anchor on this NSFW-trained model; multi-person scenes
need count+gender first and one self-contained clause per person (token bleed, §2.6).

## Forbidden tokens (profile)
story names, relationships, ownership, negation, POV-character-in-frame, Pony tags.

## Pose in text: FORBIDDEN (B-135 D18 — pose text does not work, especially multi-person).
