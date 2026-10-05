# Pony Realism v2.3 ULTRA — prompting + settings (research)

**Checkpoint:** `ponyRealism_V23ULTRA.safetensors` · **Family:** Pony · **Dialect:** Pony V6 tag vocabulary.
**Profile:** `ImageCompilerProfiles.CheckpointIdentifier = "ponyRealism_V23ULTRA.safetensors"`.

## Sources (consolidated in `scene-image-prompt-compiler-standards.instructions.md` §3.4, §6; fetched 2026-09-08)
- Civitai Pony Realism v2.3 ULTRA model card (ZyloO) — `https://civitai.com/models/372465/ponyrealism` (version 1920896)

## What the model is
- A **Pony-V6-based photorealistic merge** → keeps the Pony tag vocabulary. Never feed it
  natural-language SDXL prose. ULTRA improves lighting/skin realism.

## Settings envelope (author)
| Setting | Value | Note |
|---|---|---|
| CLIP skip | 2 | same as Pony V6 |
| Sampler | **Euler A** or **DPM2 A** (best detail) | avoid DPM++ 2M Karras |
| Steps | ≥ 30 | |
| CFG | 6–7 | |
| Resolution | > 1024px | |
| Vocabulary | Danbooru tags | `female`/`male` preferred over `woman`/`man`; per-tag weight ≤ 1.5 |

## Prompt shape (Pony V6 rules apply unchanged — see `pony-v6-prompting.md`)
Full 6-tag quality string first; rating tag; explicit count tags; explicit camera view;
mature-age tokens (faces skew young).

## Negative (the ONE cited exception — B-135 D10)
`lowres, bad anatomy, bad hands, extra digits, watermark, text, blurry`
(see `pony-v6-prompting.md` for the why).

## Pose in text: SimpleOnly.
