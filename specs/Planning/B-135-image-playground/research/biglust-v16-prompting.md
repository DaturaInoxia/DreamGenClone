# BigLust v1.6 — prompting + settings (research)

**Checkpoint:** `bigLust_v16.safetensors` · **Family:** SDXL · **Dialect:** SDXL natural-language photographic brief.
**Profile:** `ImageCompilerProfiles.CheckpointIdentifier = "bigLust_v16.safetensors"`.

## Sources (consolidated in `scene-image-prompt-compiler-standards.instructions.md` §3.1, §6; fetched 2026-09-01)
- Civitai Big Lust model page — `https://civitai.com/models/575395/big-lust`
- RunDiffusion — Juggernaut XIII Ragnarok Prompt Guide — `https://www.rundiffusion.com/prompt-guide-for-juggernaut-xiii-ragnarok-by-rundiffusion` (SDXL-family anatomy the merge inherits)
- Stable Diffusion Art — SDXL prompts — `https://stable-diffusion-art.com/sdxl-prompt/`

## What the model is
- SDXL 1.0 photorealistic finetune; a **bigASP × LUSTIFY** merge. v1.6 = bigASP 2 (2024-11-20, fp16);
  v1.5 = LUSTIFY 4.0 (darker images — a known stylistic trade-off).
- **No author-written prompt guide and no trigger words.** It reads natural-language photography
  briefs — the same anatomy as Juggernaut/SDXL. **Never feed it Pony tag vocabulary.**

## Settings envelope (SDXL family §3.1; BigLust community)
| Setting | Value | Source |
|---|---|---|
| Sampler | **DPM++ 2M SDE** (or DPM++ 2M Karras) | Juggernaut guide |
| Steps | 30–40 | Juggernaut guide |
| CFG | 3–6 family; **BigLust ~4–5** (tighter) | guide + BigLust community |
| Resolution | 1024×1024 (SDXL native, ≥1024) | SDXL art guide |
| Negative | **empty** | BigLust v1.6 example workflows use an empty negative |
| Keyword weights | ≤ ~1.4, sparingly | SDXL art guide |
| Companion LoRA (community) | "Sunburned (Big Lust)" 0.25–0.4, no trigger word | Civitai page |

**Production values (`ComfyUIImageClient.BuildSdxlWorkflow`):** `dpmpp_2m_sde` / `karras` / 30 steps / CFG 5.0 / 1024×1024.

## Prompt shape
- No author guide ⇒ the shape is **operator-stated**: `subject, action, framing, lighting, clothing-when-clothed`.
- Forbidden tokens: story names, relationships, ownership, negation, POV-character-in-frame, Pony tags.
- **Pose text: FORBIDDEN** (B-135 D18 — pose text does not work on this checkpoint, especially with more than one person).

## PROVISIONAL (must be closed by B135-037 measurement)
- The prompt **shape/component list is operator-stated, not author-documented**. Before any
  BigLust-specific builder prose is written, derive it from measured A/B in the Playground.
