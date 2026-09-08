# 039 — OP-glm-4.7 Structured-Output Mode Reset Blocks Beat Analyser

## Report

- Reported: Scene Image Studio (`/roleplay/studio/8bc36efb-…/6946a8a6-…`), beat generation flow.
- Error: `Model 'OP-glm-4.7' requires an explicit supported structured-output mode. GLM is set as the Beat Analyser`
- Configuration at failure: `RolePlaySceneBeatAnalyzer` function default assigned to `OP-glm-4.7`, thinking disabled, `MaxTokens=64000`; model row `StructuredOutputMode` was `None`.

## Analysis

`SceneBeatAnalyzerResolver.ResolveAsync` is the single active source-resolution path. It loads the `RolePlaySceneBeatAnalyzer` function default, validates it, then requires the resolved model row itself to declare a supported structured-output transport — `StrictJsonSchema` or `JsonObject` — and throws the reported error when the row is `None`.

Live dev DB state (before fix):

- `RegisteredModels` `OP-glm-4.7` (`0c350f6f-de3f-43c9-b35c-ef925967797e`, `z-ai/glm-4.7`, OpenRouter) → `StructuredOutputMode = 0 (None)` ← the blocker.
- Every other resolver check passed: function default valid, `ThinkingMode=Disabled`, model enabled + text, token limits unset (optional constraints only), provider valid.

This is a recurrence, not a code defect. Debug records `013`/`014` show `OP-glm-4.7` was proven working as the Beat Analyser with **Strict JSON Schema** + disabled thinking on 2026-09-04 (beat production succeeded, provider `OpenRouter / z-ai/glm-4.7`). The model row was later reset to `None`, matching the state carried by the git-tracked `model-manager.export.json` mirror (`StructuredOutputMode: "None"`), which reintroduces the failure on any `modelmanager-import`. `SupportsStructuredJsonSchema` is a legacy flag that startup migration backfills into `StructuredOutputMode`; it is not read by the resolver.

No resolver fallback or alternate decision path was introduced. The Model Manager UI remains the persisted configuration surface.

## Plan

- Data-only persisted-config fix (no code change):
  - Update the live dev DB `OP-glm-4.7` model row to `StructuredOutputMode = StrictJsonSchema (1)` — the previously proven mode for this model on the Beat Analyser.
  - Leave optional context/output token limits unset (the resolver treats them as constraints only when populated).
- Record this issue per debug-session protocol.
- Durability of `model-manager.export.json` deferred (not in approved scope).

## Resolution

- Applied targeted transactional update against `dreamgenclone.dev.db`:
  `UPDATE RegisteredModels SET StructuredOutputMode = 1 WHERE Id = '0c350f6f-…' AND DisplayName='OP-glm-4.7' AND ModelIdentifier='z-ai/glm-4.7'` → `Rows affected: 1`.
- Verified via DbQuery: `OP-glm-4.7` now reports `StructuredOutputMode = 1 (StrictJsonSchema)`; function default `RolePlaySceneBeatAnalyzer` unchanged (thinking disabled, `MaxTokens=64000`).

## Validated

- [x] Persisted model row verified through DbQuery (mode = StrictJsonSchema).
- [x] All remaining resolver checks already satisfied (function default, thinking mode, model kind, provider).
- [ ] Fresh beat generation in Scene Image Studio pending user confirmation. App restart not required for a DB-only change.
