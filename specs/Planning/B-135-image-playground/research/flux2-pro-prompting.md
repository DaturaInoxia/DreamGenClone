# FLUX.2-pro — prompting + settings (research)

**Checkpoint:** `black-forest-labs/FLUX.2-pro` · **Family:** API · **Dialect:** natural language / structured JSON.
**Profile:** `ImageCompilerProfiles.CheckpointIdentifier = "black-forest-labs/FLUX.2-pro"`.

## Sources (consolidated in `scene-image-prompt-compiler-standards.instructions.md` §4.1 + `flux2-prompting.instructions.md`)
- Black Forest Labs — FLUX.2 API documentation (dimensions, MP limits, negative prompt, `[flex]`/`[pro]` boundaries).

## Production boundary (§4.1)
- Dimensions ≥ 64px and **divisible by 16**; at most **4MP** output.
- **No negative prompt** — the compiler rejects the field at any nesting level.
- `[flex]` guidance 1.5–10; **steps at most 50**. `[pro]`/`[max]` must not inherit those fields
  unless the selected provider profile documents them.
- `[pro]`: at most 9MP total (input + output) and up to eight API references at 1MP output.

## Prompt shape
- `ordered-subjects, camera` required; natural language or deterministic structured JSON.
- Forbidden: story names, relationships, ownership, **negative-prompt-field**, POV-character-in-frame.
- Production uses pinned fixed endpoints, ordered role-bearing references, and deterministic
  structured prompts. Prompt upsampling is not part of compilation.

## Pose in text: Full (API natural-language family).
