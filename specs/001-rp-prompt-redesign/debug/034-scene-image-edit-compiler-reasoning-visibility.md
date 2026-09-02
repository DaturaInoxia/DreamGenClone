# 034 — Scene Image Edit Compiler: Reasoning Details Not Shown to User

**Status:** Resolved (UI now always surfaces the compiler's reasoning)
**Date:** 2026-08-27

## Report

After the parser fix (033), the scene image editor began correctly surfacing `Invalid` results, but the
user saw only an opaque reason and could not counter-argue it:

- UI message: **"Cannot prepare this edit. The request is contradictory and unsafe."**
- RP session: `423a270c-49a6-4cad-9ccf-94d68bdc6e7c`
- Source image: `89a58c41-3422-4d8b-a99a-ad2b45abd028`
- Compilation attempt: `d852deff-f6c7-4cc0-98cc-e4e090c8f41e` (status `invalid`)

The user's intent was "The women open her mouth". The compiler's `sourceSummary` falsely claimed
**"A woman holding an object in her mouth."** and rejected the request as contradictory/unsafe. The
user states the woman has **no** object in her mouth — the edit was rejected on a hallucinated premise,
and the UI never showed that premise, so it could not be counter-argued.

## Analysis

Two issues:

1. **UI hid the reasoning.** The `Invalid` state in `SceneImageEditor.razor` only rendered
   `InvalidReason`. It never rendered `SourceSummary` (the model's claimed observation), the parsed
   `RequestedChanges`, or `Preserve`. The user could not see *why* the model rejected the request, so
   they could not challenge a false observation.
2. **Model perception is unreliable.** Qwen VL (the compiler model) hallucinated "holding an object in
   her mouth" and jumped to a dead-end `invalid` instead of `clarification_required`, where the user
   could correct it ("she has nothing in her mouth"). This is a model-behavior/prompt concern.

Spec artifacts consulted:
- `specs/Planning/B-032-scene-image-generator/phase-1b-vision-aware-image-editing/contracts.md`
  (status rules: `invalid` = explicit reason, no executable prompt)
- `DreamGenClone.Domain/RolePlay/SceneImageEditCompilationModels.cs`
  (`SceneImageEditCompilationResult` fields: SourceSummary, Targets, RequestedChanges, Preserve,
  ClarificationQuestion, InvalidReason, CompiledPrompt)

## Plan

- **Part 1 (this record):** Always surface the compiler's reasoning details to the user in the editor
  UI for `Invalid` and `ClarificationRequired`, so the model's claims can be examined and counter-argued.
  Pure presentational change in `SceneImageEditor.razor` (no data/logic changes).
- **Part 2 (deferred, NOT done):** Compiler prompt hardening — only assert a visible object when clearly
  present, and prefer `clarification_required` over `invalid` when the only basis is an uncertain or
  misread visible detail. Requires bumping `SystemPromptVersion` to `qwen-edit-rules-v2` and re-syncing /
  re-validating the frozen proof corpus (`compiler-corpus.json`, `compiler-corpus-rubric.md`, Python
  `SYSTEM_MESSAGE`). Held out because it changes the canonical proof and needs corpus re-run.

## Resolution

File changed:

- `DreamGenClone.Web/Components/Pages/SceneImageEditor.razor`
  - **`Invalid` state:** now shows the reason plus "What the model observed" (`SourceSummary`),
    "Requested changes", "Preserve", and a hint: "If the model's observation does not match the image,
    rephrase the intent to describe what is actually visible, then prepare the edit again."
  - **`ClarificationRequired` state:** now shows a "Model observed: …" context line.

No model, persistence, or handler changes. Razor self-validation: all 6 checks passed (balanced blocks,
no invented Tag Helpers, no Blazor syntax leakage, all bound properties exist on
`SceneImageEditCompilationResult`, layout/directives preserved).

## Validated

- [x] Web build green (0 errors; warnings pre-existing).
- [x] Scene image edit tests green (36/36).
- [x] Webapp rebuilt and running (PID changed, HTTP 200 on :5177).
- [ ] User runtime check: `Invalid`/`ClarificationRequired` states now display the model's reasoning
      details every time.
