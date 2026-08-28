# Debug 005: Semantic Analysis — Missing Prompt/Raw-Response Persistence

**Created:** 2026-08-04
**Session:** `2586da8f-1c19-41c1-9984-b29d392d9ef3`

## Report

Two related issues:

1. **UI shows `Raw response data is not available for this record`** on semantic-analysis error rows. The user could see the error message (`semantic_confidence_out_of_range: confidence 0.85 for event 'emotional-surrender' is outside configured stat range [0.9, 1]`) but could not inspect the prompt sent to the model or the raw model output that produced the rejected confidence value.

2. **`semantic_confidence_out_of_range` is intentional validation, not a bug.** The configured `RPSemanticStatMapping` for `emotional-surrender` has `ConfidenceMin=0.9, ConfidenceMax=1.0`. The model returned confidence `0.85`, which is legitimately outside range. The system correctly rejects it without silently clamping or accepting it. The issue is purely diagnostic: no prompt/raw-response is persisted when validation fails in `ApplyInferredSemanticEvidenceAsync`.

## Analysis

### Root cause: two persistence gaps

**Gap A — Catch block drops diagnostics.** When `ApplyInferredSemanticEvidenceAsync` throws (e.g., confidence out of range), the outer `catch` block saves only `ErrorMessage` to `RolePlaySemanticInteractionAnalysisState`. The `ResultJson`, `PromptSystem`, `PromptUser`, and `RawModelOutput` are all `null`. The UI had no fallback to read these from elsewhere.

**Gap B — No direct fields on state.** `SemanticInteractionAnalysisState` only had `ResultJson` as a catch-all JSON blob. Prompt and raw output were embedded inside `ResultJson` (via `SemanticInteractionAnalysisResult`), but only on the success path and the `!inferenceResult.Success` path. The catch path had no access to `inferenceResult` and wrote nothing.

### Affected code paths

| Path | Before fix | After fix |
|------|-----------|-----------|
| Primary inference succeeds, `ApplyInferredSemanticEvidenceAsync` succeeds | `ResultJson` has prompt+output | Same + direct fields populated |
| Primary inference succeeds, `ApplyInferredSemanticEvidenceAsync` throws | `ResultJson=null`, no prompt/output | Direct fields `PromptSystem`/`PromptUser`/`RawModelOutput` populated from stashed values |
| Primary inference `!Success` (model resolution/parse failure) | `ResultJson` has prompt+output | Same + direct fields populated |
| Outer catch (session load, interaction lookup, etc.) | `ResultJson=null`, no prompt/output | Direct fields null (no inference to stash) |

### DB state confirmation

Two error rows in session `2586da8f`:
- `bf5aa09e`: `emotional-surrender` confidence `0.8` outside `[0.9, 1]` — `ResultJson=(null)`
- `b8768425`: `emotional-surrender` confidence `0.85` outside `[0.9, 1]` — `ResultJson=(null)`

The LM Studio log excerpt the user provided was for a *different* interaction (`active-in-encounter` with `0.9`), which succeeded. The error rows are from later interactions where the model returned lower-confidence `emotional-surrender` events.

## Plan

1. Add `PromptSystem`, `PromptUser`, `RawModelOutput` fields to `SemanticInteractionAnalysisState`
2. Add columns to SQLite schema with migration for existing databases
3. Stash inference diagnostics before `ApplyInferredSemanticEvidenceAsync` call
4. Populate direct fields on all upsert paths (success, `!Success`, catch)
5. Update UI to fall back to direct state fields when `ResultJson` is null
6. No confidence clamping or range widening — configured ranges remain authoritative

## Resolution

### Files changed

| File | Change |
|------|--------|
| `DreamGenClone.Application/RolePlay/SemanticInteractionAnalysisState.cs` | Added `PromptSystem`, `PromptUser`, `RawModelOutput` properties |
| `DreamGenClone.Infrastructure/RolePlay/SemanticInteractionAnalysisRepository.cs` | Added columns to schema, migration for existing tables, read/write in all queries |
| `DreamGenClone.Web/Application/RolePlay/SemanticInteractionAnalysisJobHandler.cs` | Stash diagnostics before `ApplyInferredSemanticEvidenceAsync`; populate direct fields on all upsert paths; persist stashed values in catch block |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` | UI falls back to `sdState.PromptSystem`/`PromptUser`/`RawModelOutput` when `sdDetail` (parsed `ResultJson`) is null |

## Validated

- [ ] Build: web + tests pass
- [ ] Fresh RP session: semantic analysis error rows show prompt + raw response in UI
- [ ] Regression: existing Complete rows still show full detail modal
