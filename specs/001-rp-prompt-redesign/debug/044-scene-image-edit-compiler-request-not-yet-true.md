# 044 — Scene Image Edit Compiler: Requested Change Validated Against the Source (v3 Prompt)

**Status:** Resolved (prompt change applied, build + full suite green; corpus re-validation pending)
**Date:** 2026-09-11

## Report

Every edit intent that asked for a change the source image did not already show was rejected with
`status: invalid`, so the user could not change a pose or a facial expression at all.

Edit session `e812cfd0-a647-4a89-9a70-ac4c18766ed5` (qwen VL compiler, `qwen-edit-rules-v2`),
`SceneImageEditCompilationAttempts`:

| Ordinal | RawIntent | Status | `invalidReason` |
|---|---|---|---|
| 0 | "the man is looking down, the man has a grimace face" | Invalid | *"The man in the image does not have a visible grimace face or is looking down. The request cannot be fulfilled with the current image content."* |
| 1 | "the man is looking down" | **Ready** | — (source already showed the head tilted down) |
| 2 | "the man is looking down, he is mid orgasm" | Invalid | *"The request describes a specific state (mid orgasm) that cannot be visually confirmed in the image."* |
| 3 | "the man is looking down, change his facial expression so he looks mid orgasm" | Invalid | *"The man is not looking down; he is turned slightly to his right. The request specifies 'looking down', which does not match the current pose of the man in the image."* |
| 4 | "change to that the man is looking down, change his facial expression so he looks mid orgasm" | Invalid | *"The request is impossible because it asks for a specific pose (looking down) that cannot be accurately determined from the image."* |

A second session (`b2b1a6cd-cd86-4527-ae21-aa61c410eaa3`, ordinals 0-3) shows the same pattern for
"update face to be orgasm face" / "the woman is having an orgasm, her expression should reflect this."

Ordinal 1 is the tell: it passed **only because the requested state already existed in the source**.
The compiler was verifying the request against the image instead of planning a transformation.

## Analysis

The `qwen-edit-rules-v2` system prompt in `QwenSceneImageEditPromptCompiler.BuildSystemMessage()` told
the model:

> "Return invalid only when the request is genuinely impossible or self-contradictory ... **the thing
> to change is not visible in the source** ..."

The model reads *"the thing to change"* as the **new state** ("looking down", "mid orgasm"), finds it
absent from the source, and returns `invalid`. That is the inverse of an editor's job: a requested
change is, by definition, not yet present — its absence is the reason to edit.

Two supporting clauses pushed the same way:

- **"or a visible detail is uncertain"** (clarification rule) let the model treat a subjective state
  as an unconfirmable visible detail and fold it into a refusal (*"cannot be visually confirmed"*,
  *"no visible signs of him being mid orgasm"*).
- Nothing in the prompt ever said that a requested change is a *future* state, or that the model must
  check only that the **target** is visible.

This is a different failure from v2's over-preservation fix (debug record 035): 035 stopped the model
rejecting changes that touch a *preserve* category; 044 stops it rejecting changes that are simply not
in the source yet.

Spec artifacts consulted:
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/contracts.md`
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/data-model.md`
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/compiler-corpus.json`
  (pinned `qwen-edit-rules-v2`)
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/compiler-corpus-rubric.md`
- `specs/001-rp-prompt-redesign/debug/033-*`, `034-*`, `035-*`, `038-*` (compiler prompt history)

## Plan (approved)

Rewrite the compiler system prompt to `qwen-edit-rules-v3`:
- **Change vs. target** — a requested change is the state the image must have *after* the edit; verify
  only that the target is visible; its absence from the source is never a reason to reject. Include the
  explicit counter-example ("the man is looking down" with a visible man currently looking aside →
  `ready`).
- **Narrow `invalid`** — "the target the request refers to is not visible in the source **at all**",
  plus the existing impossible/self-contradictory/harmful cases. Add: "The source not already showing
  the requested change is never a reason for clarification_required or invalid."
- **Narrow clarification** — only target ambiguity (more than one visible candidate) or two readings
  that would change different visible things. Drop "a visible detail is uncertain".
- **Subjective states** — add explicit guidance that emotional/physiological states (happy, angry,
  surprised, in pain, aroused, ecstatic, mid orgasm) are valid expression/pose edits that compile to
  concrete visible cues (brow, eyes, gaze, mouth, jaw, head angle, flush, sweat, muscle tension).
- Keep the authoritative-request, surgical-preserve, adult-scene, and JSON-only clauses.
- Bump `SystemPromptVersion` → `qwen-edit-rules-v3`; sync the frozen corpus (JSON, rubric, Python
  `SYSTEM_MESSAGE`); update tests.

## Resolution

Files changed:

- `DreamGenClone.Web/Application/RolePlay/QwenSceneImageEditPromptCompiler.cs`
  - `SystemPromptVersion` → `qwen-edit-rules-v3`.
  - `BuildSystemMessage()`: two new paragraphs (change-vs-target with the worked counter-example;
    subjective/emotional/physiological states) and rewrites of the clarification/invalid paragraphs
    (removed "the thing to change is not visible in the source" and "a visible detail is uncertain").
- `helpers/runpod/qwen-vl-edit-compiler/run-compiler-corpus.py` — `SYSTEM_MESSAGE` synced to v3.
- `specs/.../phase-1b-vision-aware-image-editing/compiler-corpus.json` — `systemPromptVersion` → v3.
- `specs/.../phase-1b-vision-aware-image-editing/compiler-corpus-rubric.md` — version + a 2026-09-11
  note explaining the change and the pending corpus re-run.
- `DreamGenClone.Tests/RolePlay/QwenSceneImageEditPromptCompilerTests.cs` — new test
  `BuildMessages_RequestedChangeIsNeverValidatedAgainstTheSource` pinning the new rules and asserting
  the two removed v2 clauses are gone.
- `DreamGenClone.Tests/RolePlay/SceneImageServiceJobTests.cs` — hardcoded `"qwen-edit-rules-v2"`
  literal replaced with `QwenSceneImageEditPromptCompiler.SystemPromptVersion` so the "compiler prompt
  contract changed after this attempt was queued" guard cannot desync from a future bump.

No schema, parse, persistence, repository, job-handler, UI, or model-configuration changes.

## Validated

- [x] Web build green (0 errors) — `dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj -o artifacts/build-check/tests`
      (scratch output so the running web app's `bin` lock was left alone).
- [x] Affected tests green (68/68: `QwenSceneImageEditPromptCompilerTests`, `SceneImageServiceJobTests`,
      `SceneImageEditCompilationJobTests`, `SceneImageEditDomainTests`); full suite green (1862/1862).
- [ ] Corpus proof re-run pending (classification may shift; version pins updated so the proof stays honest).
- [ ] User runtime check: "the man is looking down" + "mid orgasm" expression intents now compile to
      `ready` instead of `invalid`.
