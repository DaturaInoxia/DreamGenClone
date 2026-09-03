---
description: "FLUX.2 generation/edit prompting and request-schema rules. Read before implementing or changing any FLUX.2 compiler, provider request, capability profile, or qualification proof."
applyTo: DreamGenClone.Application/RolePlay/**/*ProductionMedia*.cs,DreamGenClone.Web/Application/RolePlay/**/*ProductionMedia*.cs,DreamGenClone.Tests/RolePlay/**/*ProductionMedia*.cs,specs/Planning/B-032-scene-image-generator/**
---

# FLUX.2 Production Compiler Rules

**Research refresh:** 2026-09-02. Official sources are listed in the B-032 provider evidence matrix.
Official capability establishes a candidate schema only; an exact DreamGenClone capability cell still
requires local qualification.

## Family Contract

- Generation and edit are separate compiler identities and capability profiles.
- Use a pinned fixed model endpoint/version for production. Preview endpoints are evidence-only.
- Compile deterministic structured JSON from validated production intent. Do not call prompt
  upsampling or another LLM at runtime.
- Order the prompt as primary subject, action, critical style, essential context, then secondary
  detail. For complex/multi-subject production requests use the documented structured shape:
  `scene`, ordered `subjects`, `style`, `color_palette`, `lighting`, `mood`, `background`,
  `composition`, and `camera`.
- Associate colors and attributes with an exact subject or object. Never emit story-only names,
  relationships, hidden facts, or raw roleplay prose.
- FLUX.2 has no negative prompt. `negative_prompt`, `negativePrompt`, and equivalent fields are
  forbidden at every nesting level. Describe the desired positive state instead.

## Exact Request Envelope

All values come from the selected persisted capability profile/settings. Missing values fail; there
are no code defaults.

- Width and height: each 64 or greater, each divisible by 16, total output at most 4 megapixels.
  BFL recommends at most 2MP for routine use.
- Seed is explicit and snapshotted.
- `[pro]`/`[max]`: do not emit `steps` or `guidance` unless the exact provider schema explicitly
  qualifies them. BFL `[pro]` input plus output is limited to 9MP; at 1MP output this permits up to
  eight references, at 2MP up to seven.
- `[flex]`: guidance is 1.5 through 10 and steps are at most 50; both are required by the selected
  profile when enabled.
- `[dev]`: provider-specific steps/guidance are legal only when declared by the exact profile.
- Reference limits are variant/provider-specific: BFL documents up to eight API references;
  Together uses ordered `reference_images` for FLUX.2 pro/dev/flex. Never apply the playground's
  ten-image allowance to an API profile.
- Every reference is ordered and has an explicit semantic role. The compiled prompt refers to
  `image 1`, `image 2`, etc. with the same ordinals. A missing, duplicate, or unmentioned role fails.
- BFL result URLs expire after ten minutes. Dispatch must persist the provider request ID immediately
  and reconciliation must copy bytes into owned storage; this is a transport rule, not compiler logic.

## Qualification Boundary

- Multi-reference editing and character consistency are not qualified by official examples.
- Composition-first identity editing remains unavailable until the exact actor-count, angle, crop,
  pose, composition, model, workflow, compiler, and reference-layout cell passes the frozen matrix.
- A rejected cell cannot be replaced by a nearby passing cell or generation compiler.
