# 035 — Scene Image Edit Compiler: Over-Rejection From Preservation Strictness (v2 Prompt)

**Status:** Resolved (code + prompt change applied; corpus re-validation pending)
**Date:** 2026-08-27

## Report

The compiler rejected almost every requested edit, blocking legitimate changes. Representative records
(RP session `423a270c-49a6-4cad-9ccf-94d68bdc6e7c`, source image `074956e3-69ca-4e57-a442-ddeec3723992`):

- "Zoom out making more of the women and object visible" → `invalid`: *"The request is contradictory
  because zooming out would make less of the woman and object visible, not more."*
  (preserve: identity, wardrobe, unaffected people, **objects, composition, lighting, style**)
- "put the object in her mouth" → rejected as *"would change the composition"*.
- "lowers her head / opens her mouth wider / closes her eyes / moves closer to the object" →
  `invalid`: *"sensitive topic…"*, *"could be interpreted as inappropriate or unsafe"*, *"not directly
  observable."*

The user wants: remove glasses, remove a coat, look left, stand up, zoom out, move an object — all
compilable, with the location/surroundings preserved.

## Analysis

Two over-rejection patterns, both from the `qwen-edit-rules-v1` system prompt:

1. **Preservation treated as absolute.** The prompt said *"Preserve visible identity, wardrobe unless
   requested, unaffected people, objects, composition, lighting, and style."* The **"unless requested"**
   qualifier only applied to *wardrobe*. `objects`, `composition`, `lighting`, `style` were absolute, so
   any requested change touching them was classified "contradictory" → `invalid`.
2. **Model's own safety filter over-gating.** The prompt's *"unsafe under the configured policy, return
   clarification_required or invalid"* let the model apply its training-based safety classifier, refusing
   feasible face/pose/mouth edits as "sensitive" — even though the app is for private, consensual adult
   fictional editing.

Spec artifacts consulted:
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/contracts.md`
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/data-model.md`
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/compiler-corpus.json`
  (pinned `qwen-edit-rules-v1`)

## Plan (approved)

Rewrite the compiler system prompt to `qwen-edit-rules-v2`:
- **The user's request is authoritative** — explicit changes to any visible thing (clothing, accessory,
  object incl. moving/repositioning, pose, framing/zoom, expression) compile directly; never reject just
  because it changes a preservation category.
- **Surgical preservation** — preserve only what was not requested: the location/surroundings, the
  subject's identity, unaffected people.
- **Narrow `invalid`** — only genuinely impossible/self-contradictory, target not visible at all, or
  clearly harmful/illegal. **Prefer `clarification_required`** for ambiguity/uncertain perception.
- **Adult-scene note** — do not refuse an edit merely because it is sexual/adult when visible & feasible.
- Bump `SystemPromptVersion` → `qwen-edit-rules-v2`; sync frozen corpus (json, rubric, Python
  `SYSTEM_MESSAGE`); update tests.

## Resolution

Files changed:

- `DreamGenClone.Web/Application/RolePlay/QwenSceneImageEditPromptCompiler.cs`
  - `SystemPromptVersion` → `qwen-edit-rules-v2`
  - `BuildSystemMessage()` rewritten (authoritative request, surgical preserve, narrow invalid,
    clarification-first, adult-scene note).
- `helpers/runpod/qwen-vl-edit-compiler/run-compiler-corpus.py` — `SYSTEM_MESSAGE` synced to v2.
- `specs/.../phase-1b-vision-aware-image-editing/compiler-corpus.json` — `systemPromptVersion` → v2.
- `specs/.../phase-1b-vision-aware-image-editing/compiler-corpus-rubric.md` — version + human-review
  wording + re-validation note.
- `DreamGenClone.Tests/RolePlay/QwenSceneImageEditPromptCompilerTests.cs` — asserts v2 prompt contains
  "The user's request is authoritative" and the never-reject preservation guidance.
- `DreamGenClone.Tests/RolePlay/SceneImageServiceJobTests.cs` — version literal → v2.

No persistence, repository, handler, UI, or model-config changes.

## Validated

- [x] Web build green (0 errors).
- [x] Affected tests green (49/49); full suite green (1382/1382).
- [x] Webapp rebuilt with v2 and running (HTTP 200 on :5177).
- [ ] Corpus proof re-run pending (classification may shift; version pins updated so the proof stays honest).
- [ ] User runtime check: previously-rejected edits ("zoom out", "put the object in her mouth", pose/head
      changes) now compile to `Ready`/`clarification_required` instead of `invalid`.
