# Krea 2 Turbo — prompting + settings (research)

**Checkpoint:** `krea2_turbo_fp8_scaled.safetensors` · **Family:** Krea2 · **Dialect:** Krea2 natural-language photographic brief.
**Profile:** `ImageCompilerProfiles.CheckpointIdentifier = "krea2_turbo_fp8_scaled.safetensors"`.

## Source (measured)
- `helpers/local-comfyui-host/run-krea2-proof.ps1` — the **59-cell proof matrix** on this checkpoint,
  reviewed 2026-10-01 (`specs/Planning/B-137-krea2-local-generation`). All renders reviewed.
- `specs/Planning/B-137-krea2-local-generation/plan.md` — the locked B-137 decisions (text-only,
  photorealistic default; no reference conditioning, no edit, no ControlNet).

## What the model is
- 12B dense DiT trained from scratch; **Qwen3-VL 4B text encoder** (NOT CLIP — the SDXL 75-token
  window is a different architecture and does not apply); Qwen Image VAE.

## Settings envelope (read from the model's qualified TextToImage entry, `Krea2Refs`)
`steps 8 · cfg 1.0 · sampler euler · scheduler simple · denoise 1.0 · 1024×1024`.
This is a cfg-1 distilled model; the profile row's `SettingsEnvelopeJson` is documentation only —
the graph builder reads the model row, not the profile.

## Prompt shape (the single biggest quality lever — measured)
- A **photographic brief** returns a complete figure; a body noun-list returns a torso crop
  (loses the head).
- An explicit **framing demand never once worked** in any cell.
- A **face-facing clause on an act suppresses the act** — never add "faces toward the lens" to an
  act prompt (no act exists in the distance catalog, which is why those rungs may state it).
- Forbidden tokens: story names, relationships, ownership, negation, POV-character-in-frame,
  Pony tags, framing-demand, body-noun-list, face-facing-clause.

## Negative
- **None, and there is nowhere to put one**: the graph feeds the sampler a `ConditioningZeroOut` of
  the POSITIVE. Empty is the architecture, not a style choice.

## Capabilities (measured)
- Stock weights + stock text encoder render **full nudity with NO LoRA** (the "TE is the censor"
  claim was falsified).
- Explicit acts render from prose via grounded per-act LoKrs; fellatio-class oral needs an act LoRA
  on base weights.
- **Pose in text: Full** — acts ARE described successfully in text (the opposite of BigLust/Juggernaut/FLUX).
