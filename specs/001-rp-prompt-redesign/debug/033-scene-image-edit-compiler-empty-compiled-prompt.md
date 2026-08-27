# 033 — Scene Image Edit Compiler: Empty `compiledPrompt` Fails Compilation

**Status:** Resolved (code fix applied; runtime re-validation pending)
**Date:** 2026-08-26

## Report

In the scene image editor, an edit attempt for the raw intent **"Add pubic hair"** failed to compile.

- RP session: `423a270c-49a6-4cad-9ccf-94d68bdc6e7c`
- Interaction: `36d487e6-58a6-4c7d-8d54-34cb1d050676`
- Source image: `bd83e992-079d-4f83-b45a-0f06c3ce0969`
- Edit session: `e6679587-454f-4c66-91e3-8949d2b0977a`
- Compilation attempt: `294a4219-caf3-49cf-8549-55bca7daf607` (Ordinal 0)
- UI error: **"Compilation failed. Compiler response field 'compiledPrompt' must be a non-empty string or null."**

Attempt status in DB: `Failed`; `Error` = the message above.

## Analysis

Raw model response persisted on the attempt (Qwen VL compiler):

```json
{
  "schemaVersion": "scene-image-edit-compiler-v1",
  "status": "invalid",
  "sourceSummary": "A woman sitting in a chair holding a coffee mug.",
  "targets": [],
  "requestedChanges": ["Add pubic hair"],
  "preserve": ["visible identity", "wardrobe", "unaffected people", "objects", "composition", "lighting", "style"],
  "clarificationQuestion": "Are you sure you want to add pubic hair? This could be considered inappropriate.",
  "invalidReason": "The request to add pubic hair is not feasible due to the nature of the image and the policy constraints.",
  "compiledPrompt": ""
}
```

Two contract violations, both from Qwen VL structured-output sloppiness:

1. `compiledPrompt` is `""` (empty string) instead of `null`. `QwenSceneImageEditPromptCompiler.OptionalString`
   treated a whitespace-only string as invalid and threw
   `Compiler response field 'compiledPrompt' must be a non-empty string or null.` → attempt marked `Failed`.
2. `clarificationQuestion` is populated while `status` is `invalid`; the terminal-state rules require only
   `invalidReason` for `invalid`. Even after removing #1, `ValidateTerminalState` would reject the response.

Note the parser was stricter than the JSON schema handed to the model: the schema declares
`compiledPrompt: {"type": ["string", "null"]}` with no `minLength`, so the model is told `""` is acceptable
while the C# parser rejected it — an internal contract inconsistency.

Spec artifacts consulted:
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/contracts.md`
  (status rules; `Parse` is "strict and deterministic")
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/data-model.md`
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/tasks.md` (P1B-022 strict parser)

## Plan

Forward-only code fix in the compiler parser (no DB mutation, no persistence/UI/model-config changes):

1. `QwenSceneImageEditPromptCompiler.OptionalString`: treat a whitespace-only string as `null` (aligns C# with
   the `["string","null"]` schema). Non-string, non-null values (number/bool/object) still throw.
2. Replace `ValidateTerminalState` with `NormalizeTerminalState`: the declared `status` is authoritative —
   for `ready` require `compiledPrompt` and drop stray `clarificationQuestion`/`invalidReason`; for
   `clarification_required` require the question and drop the others; for `invalid` require `invalidReason`
   and drop the others. If the status's essential field is missing/empty, still fail (nothing fabricated).
3. Regression tests in `QwenSceneImageEditPromptCompilerTests.cs`.
4. Debug record (this file).

Deliberately **not** changed (to avoid desyncing the frozen proof corpus):
- No `minLength` on the response schema fields — `compiler-corpus.json` / `compiler-corpus-rubric.md` pin
  `scene-image-edit-compiler-v1`; changing the runtime schema would diverge from the canonical reproducible proof.
- No system-prompt text change / `SystemPromptVersion` bump — `qwen-edit-rules-v1` is pinned by the same corpus,
  and a bump would invalidate other in-flight v1 attempts via the version check in the job handler.

## Resolution

Files changed:

- `DreamGenClone.Web/Application/RolePlay/QwenSceneImageEditPromptCompiler.cs`
  - `OptionalString`: whitespace-only string → `null`; non-string still throws.
  - `ValidateTerminalState` → `NormalizeTerminalState`: status-authoritative normalization; essential field
    still strictly required.
- `DreamGenClone.Tests/RolePlay/QwenSceneImageEditPromptCompilerTests.cs`
  - Added: `Parse_InvalidWithEmptyCompiledPromptAndStrayQuestion_NormalizesToInvalid`,
    `Parse_ClarificationWithEmptyCompiledPrompt_NormalizesToNull`,
    `Parse_ReadyWithStrayInvalidReason_NormalizesToReady`,
    `Parse_InvalidWithoutReason_StillFails`, `Parse_ClarificationWithoutQuestion_StillFails`,
    `Parse_NonStringCompiledPrompt_StillFails`.
  - Updated: `Parse_ClarificationWithPrompt_Fails` → `Parse_ClarificationWithStrayPrompt_NormalizesToClarification`
    (a stray executable prompt on a `clarification_required` result is now dropped, never executed).

Expected outcome for the reported case: the same request re-runs to a proper `Invalid` result and the UI shows
**"Cannot prepare this edit. The request to add pubic hair is not feasible…"** instead of "Compilation failed."
The failed attempt `294a4219…` is terminal/append-only and is not mutated; a new attempt is created on re-run.

## Validated

- [x] Build green (web: 0 errors; tests: 0 errors).
- [x] Compiler/job/domain tests green (31/31).
- [x] Full suite green (1382 passed, 0 failed).
- [ ] Runtime re-run confirmed by user (re-trigger edit → `Invalid` with reason, not `Failed`).
