---
description: "Pony V6 XL (ComfyUI) prompting rules for the scene image generator. Validated on the RunPod pod 2026-08-23. Read before building or changing any Pony/ComfyUI image prompt."
applyTo: DreamGenClone.Web/Application/RolePlay/PonySceneImagePromptBuilder.cs,DreamGenClone.Web/Application/RolePlay/IPonySceneImagePromptBuilder.cs,DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs,DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs,DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs,DreamGenClone.Web/Application/RolePlay/SceneImageBeatAnalysisService.cs,DreamGenClone.Tests/RolePlay/**/*.cs,helpers/runpod/workflows/**,specs/Planning/B-032-scene-image-generator/**
---

# Pony V6 XL Prompting Rules (Scene Image Generator)

> Source of truth for how Pony V6 XL (ComfyUI) reads prompts. These rules were **validated by live generation on the RunPod pod** on 2026-08-23 (a sequence of beach-scene test images). Any prompt or builder that violates them produces cartoon/overhead/deformed/missing-subject output.

## The model's real nature

- **Pony V6 XL is an anime/cartoon/furry model.** Its official card states the training data is a ~1:1 mix of anime / cartoon / furry / pony images. It is *designed* to output anime/cartoon style. Do NOT try to force photorealism out of the base V6 checkpoint — that is a model-selection problem, not a prompt problem.
- **The Pony family also includes photorealistic human merges built on V6** (e.g. **Pony Realism v2.3 ULTRA**), which keep the same danbooru-tag vocabulary but render photoreal humans. Selecting one of those checkpoints is how realism is achieved *within* the Pony tag path — do not paste natural-language SDXL prose at them.
- **Routing is model-family driven.** Prompt generation now targets the prompt style of the selected/enabled image model family (Pony → tag compiler; SDXL/FLUX/API → natural-language compiler) and records the style on the prompt record. Registering a photoreal Pony merge as `Pony`/`PonyV6Tags` sends it through this tag compiler. See `SceneImagePromptStyle`/`SceneImagePromptStyleResolver` and `ModelResolutionService.ResolveImagePromptGenerationModelAsync`.
- If the user wants a non-Pony natural-language realistic render, that is the separate **Juggernaut XL / Big Lust / SDXL** path (`sdxl-juggernaut-prompting.instructions.md`).

## Non-negotiable Pony rules (all validated)

1. **Full quality string, FIRST in the prompt:**
   ```
   score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up
   ```
   The short `score_9` form is documented as "much weaker" (a training quirk — the model learned the whole long string correlates with good images). Using the short form yields low-quality, deformed, collapsed output.
   - **The app constant `PonySceneImagePromptBuilder.PonyQualityTags` is the full 6-tag string** (`score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up`) with the `rating_*` tag chosen separately by content policy. Do not shorten it or hardcode `rating_explicit`.
   - Photoreal Pony merges (Pony Realism) are V6-derived, so they inherit the same full-string training quirk; the full string remains correct for them. (Pony Realism's author page shows a short `score_9, score_8_up, score_7_up, BREAK` example — treat that as a community shortcut, not a replacement for the full V6 string, which is the pod-validated app default.)
2. **Always include a `rating_*` tag** — `rating_safe`, `rating_questionable`, or `rating_explicit` — chosen from the resolved content policy, never hardcoded.
3. **Keep prompts short and tag-like.** Pony is trained on tags + short phrases. Long narrative caption prose (e.g. "Late morning on a small lake beach. Ken sits on a striped towel...") degrades output severely. The Beat Prose must be **converted to dense comma-separated tags**, never pasted verbatim as a sentence.
4. **Use count tags for people** — `1boy`, `1girl`, `2people`, `1girl and 1boy`, etc. Without an explicit count Pony collapses "a man and a woman" into a single figure (validated: "only 1 person" failures).
5. **Say the camera view explicitly** — `front view, eye level`, `from side`, etc. Without it Pony defaults to overhead/top-down angles (validated: "overhead view" failures). Add the unwanted angles (`overhead view, top-down view, aerial view, drone shot, bird's eye view`) to the **negative** only if they keep appearing.
6. **CLIP skip 2 is required** (`CLIPSetLastLayer` with `stop_at_clip_layer: -2` in the ComfyUI workflow). Without it Pony outputs "low quality blobs". All repo workflows already have this.
7. **Sampler: `euler_ancestral` (Euler a), 25 steps, 1024px** — the officially recommended sampler. Plain `euler` is not the recommendation.
8. **Minimal negative.** Pony "is designed to not need negative prompts in most cases". A huge negative list fights the model and causes artifacts. Keep a short guard set (~6-8 terms): `lowres, bad anatomy, bad hands, extra digits, watermark, text, blurry`. Only add terms for problems you actually observe.
9. **Pony ignores "no X" in the positive** — put negations in the negative prompt, never as "no X" in the positive.
10. **Camera/POV words re-compose the scene** — naming the camera/POV repositions subjects. Describe what the POV character watches instead.
11. **Concrete body tokens beat vague ones** — `chubby` works; `fat`/`full figure` don't; `curvy` is too vague.
12. **Attribute position matters** — early tokens shape composition; repeat key attributes (age, hair) or Pony uses its default attractive-adult prior.
13. **Ethnicity**: repeat explicit `caucasian`; avoid conflicting skin descriptors; never put ethnicity names in the negative (over-attends / backfires).
14. **Background people are dropped** unless: (a) count tags are used, and (b) `extra people in foreground` / `crowd` is NOT in the negative.

## How the Beat Prose → Pony conversion works (LLM-primary, B-098/B-098-followup)

**The production path is the LLM preprocessor, not the deterministic builder.** `SceneImagePromptGenerationJobHandler` calls `ISceneImageLLMPromptBuilder.BuildMessages` (full-turn variant), runs the text model, and parses via `ParseOutput`. The system prompt teaches the model to be a **Pony V6 prompt expert** that emits a SHORT, dense tag prompt. The deterministic `BuildDeterministicBeatPrompt` is retired from the primary path (kept only for reference/tests).

The LLM prompt must follow, in order:
- Quality string (full 6 tags) + `rating_*` chosen from the **narrative phase** (see below) → head.
- Danbooru count tag (`1boy`, `1girl`, `2people`, `1girl and 1boy`) — prevents person-collapse.
- Character = 3–6 SHORT visual tags (hair, eyes, body type, age, key clothing). **NEVER** a metadata block (`Age: 51; Height: 5'8"; Weight: 150 lbs`), never `Appearance — ...`, never prose. Use concrete tokens (e.g. `chubby`, not `full figure`).
- Scene folded into a few short tags (location, time of day, lighting, mood) — no repeated facts.
- One explicit camera/view tag (`front view`, `eye level`, `from side`).
- Beat-stated clothing honored; nudity only when the beat implies it.
- Keep the ENTIRE prompt under 800 characters / ~40 tags.
- `{{style}}`, `{{size}}`, and `{{angle}}` placeholders remain injectable at render time.

### Explicitness comes from the narrative phase (theme intensity) — approved mapping
The `rating_*` tag is chosen by `NarrativePhase` in `ResolveRatingTag(phase, policy)`:

| Phase | Rating |
|-------|--------|
| `Opening`, `BuildUp` | `rating_safe` |
| `Committed`, `Approaching` | `rating_questionable` |
| `Climax` | `rating_explicit` |
| `Reset` (after climax) | `rating_questionable` |
| any + `SfwFiltered` provider | `rating_safe` (hard clamp) |

This replaces the old policy-only rating (`settings.AllowExplicitImage`). The user prompt line `Pony rating tag to use: <tag>` carries the resolved rating to the model.

### Negative prompt
`BuildDeterministicBeatNegativePrompt` (kept, short guard set: `lowres, bad anatomy, bad hands, extra digits, watermark, text, blurry` + absent characters). The render handler uses this as the per-scene negative. Do NOT add `score_4/score_5/score_6` drops: the Pony V6 author's own `score_9` explainer states score tags in the negative are weak ("they will not push you away from really bad images") — the minimal guard set is the researched choice for Pony-family checkpoints including Pony Realism.

## Pony Realism v2.3 ULTRA (photorealistic Pony merge) — researched 2026-09-08

Civitai model 372465, version 1920896 (`ponyRealism_V23ULTRA.safetensors`); base **Pony**, CreativeML Open RAIL++-M, NSFW-capable, photoreal-leaning. Author-recommended settings (from the model card, fetched 2026-09-08):

| Setting | Value (author) | Note |
|---|---|---|
| Base | Pony | Same danbooru-tag vocabulary as V6; **not** natural-language |
| CLIP skip | 2 | Same as V6 |
| Sampler | **Euler A** or **DPM2 A** ("best for detail") | Avoid DPM++ 2M Karras (author: not recommended) |
| Steps | ≥ 30 | |
| CFG | 6–7 | |
| Resolution | > 1024px | |
| Vocabulary | Danbooru tags; use `female`/`male` over `woman`/`man` | Individual tag weights ≤ 1.5 |

Generation notes validated from the model's discussion + card:
- Strong on explicit poses/position fit; **explicit count tags are mandatory** ("two female" → single figure reported otherwise) — repeat the count tag and keep `1girl`/`1boy` style tokens.
- Faces skew young; repeat explicit mature-age tokens when the moment calls for an older character.
- v2.3 ULTRA specifically improves natural/balanced lighting and skin detail/realism.
- The app keeps the **full V6 quality string** in the positive and the **short guard negative** (no score drops) for Pony-family models including Pony Realism.

## Verification protocol
- After any change to the prompt builder, run the affected tests (`DreamGenClone.Tests/RolePlay/SceneImagePromptPreprocessorTests.cs`) and the full test suite — repo hard rule.
- To validate output visually: `helpers/runpod/generate-one.ps1 -WorkflowPath helpers/runpod/workflows/<wf>.json -Prefix <name> -OutputDir artifacts/tmp/images/<name>` against the pod, then inspect the PNG.
- Reference workflows: `helpers/runpod/workflows/pony-simple.json` (clean Pony recipe) and `sdxl-beach.json` (SDXL realistic comparison).
