# Pony V6 XL — prompting + settings (research)

**Checkpoint:** `ponyDiffusionV6XL_v6.safetensors` · **Family:** Pony · **Dialect:** Pony V6 tag vocabulary.
**Profile:** `ImageCompilerProfiles.CheckpointIdentifier = "ponyDiffusionV6XL_v6.safetensors"`.

## Sources (consolidated in `scene-image-prompt-compiler-standards.instructions.md` §4, §6; fetched 2026-09-08)
- Civitai Pony Diffusion V6 XL official card (PurpleSmartAI) — `https://civitai.com/models/257749/pony-diffusion-v6-xl`
- Author `score_9` explainer — `https://civitai.com/articles/4248`

## Prompt language
- **Dense comma tags**, never natural-language prose.
- The full 6-tag quality string **first** — `score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up`
  (a short `score_9` alone is "much weaker" — a training quirk, per the author explainer).
- `rating_safe` / `rating_questionable` / `rating_explicit` is mandatory.
- Explicit count tags (`1girl` / `1boy` / `2people`) are mandatory — "two female" without a count
  tag returns one figure.
- Explicit camera view (`front view, eye level`) is required.
- Repeat mature-age tokens; the model's faces skew young.

## Settings envelope
| Setting | Value |
|---|---|
| Sampler | `euler_ancestral` |
| Steps | 25 |
| CFG | 7.0 |
| Resolution | 1024×1024 |
| CLIP skip | 2 |

## Negative (the ONE cited exception — B-135 D10)
`lowres, bad anatomy, bad hands, extra digits, watermark, text, blurry`
Pony ignores "no X" in the positive, so negations must live in the negative; a huge negative fights
the model; score drops in the negative are weak (author explainer), so they are deliberately excluded.

## Pose in text: SimpleOnly (simple poses only; complex/multi-person poses do not work in text).
