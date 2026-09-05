# OpenRouter scene-beat structured-output configuration

## Report

- Session: `4f2eec18-b190-4beb-ad35-8d520ae5c800`
- Interaction: `dad139a5-8e98-469b-ab74-8e55404f1732`
- Request: `roleplay/studio/... generate beets`
- Error: `Model 'OP-deepseek-v4-flash-0731' requires an explicit supported structured-output mode.`

## Analysis

The persisted `RolePlaySceneBeatAnalyzer` function default selected registered model `OP-deepseek-v4-flash-0731`. Its `StructuredOutputMode` was `None`, while `SceneBeatAnalyzerResolver.ResolveAsync` accepts only `StrictJsonSchema` or `JsonObject`. The same row had unset context/output capability limits and `SupportsThinkingControl=0`; the function default requested an explicit thinking mode. OpenRouter model metadata reports a context window of 1,310,720 tokens and reasoning-related support, but no published maximum completion limit.

Authoritative references consulted:

- `specs/Planning/B-100-progressive-scene-beat-pipeline/IMPLEMENTATION-HANDOFF.md`
- `specs/001-rp-prompt-redesign/spec.md`
- `specs/001-final-writing-instruction/spec.md`
- `DreamGenClone.Web/Application/RolePlay/SceneBeatAnalyzerResolver.cs`

## Plan

Update only persisted Model Manager configuration through a transactional DbQuery command:

- Set the replacement model to `StructuredOutputMode=JsonObject`.
- Record the verified OpenRouter context capability `1310720`.
- Record the existing function output ceiling `64000` as the explicit model output capability.
- Set `RolePlaySceneBeatAnalyzer.ThinkingMode=Disabled` because the model does not declare thinking control.

No resolver fallback or alternate RP decision path is introduced.

## Resolution

Added `b100-analyzer-openrouter-configure` to the permanent DbQuery dispatcher and applied it to the live development database. The command transactionally targeted the enabled `OP-deepseek-v4-flash-0731` registration and `RolePlaySceneBeatAnalyzer` function default, setting `StructuredOutputMode=JsonObject`, `MaximumContextTokens=1310720`, `MaximumOutputTokens=64000`, and `ThinkingMode=Disabled`. A subsequent DB-only update set `SupportsThinkingControl=1` to match the model's reasoning capability declaration and clear the resolver's capability mismatch.

## Validated

- [x] Persisted model and function settings verified through DbQuery.
- [x] `SceneBeatAnalyzerResolverTests`: 5 succeeded, 0 failed, 0 skipped.
- [x] Follow-up capability mismatch verified resolved through DbQuery; app restart/rebuild was not performed.
- [ ] Pending fresh scene-beat generation confirmation.
