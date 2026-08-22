# Scene Image Beat Reasoning Response Path

## Report

Scene Image Studio beat generation failed twice for session `6e836089-0505-4b7b-b7d0-53e1ee81f15b`, turn `f4dc579914fb4d6bb04f2b059142189f`. The second error was `'}` is invalid without a matching open` at line 198.

## Analysis

Application logs showed the beat job used `deepseek-ai/DeepSeek-V4-Flash-0731`, reached the configured 8000-token ceiling, and invoked the shared continuation path. The continuation returned a five-character segment before JSON parsing failed.

The beat handler used plain `ICompletionClient.GenerateAsync`. That path substitutes `reasoning_content` when final content is empty. Main role-play continuation, semantic inference, and encounter-summary enrichment instead use the reasoning-aware completion methods so reasoning and final content remain separate. The beat handler was the inconsistent call site.

Failed beat records also did not retain the response because `RawModelResponse` was assigned only after parsing succeeded.

## Plan

Follow the existing semantic-inference pattern: call `StreamGenerateWithReasoningAsync`, parse only final content, persist that content before parsing, log it on parse failure, and retain strict standard JSON validation. Remove the beat-specific malformed-JSON sanitizer.

## Resolution

`SceneImageBeatGenerationJobHandler` now uses `GenerateWithReasoningAsync`, matching main continuation's non-streaming path when there is no chunk consumer. Only final content is passed to `SceneImageBeatAnalysisService`; reasoning is never treated as beat JSON. An initial attempt used `StreamGenerateWithReasoningAsync`, but live TogetherAI output produced no recognized streamed content or reasoning, causing the shared client to fall back to plain generation. The non-streaming reasoning-aware method avoids that fallback and directly separates the provider's content and reasoning fields.

Live validation on 2026-08-21 completed background job `c720eb6592a54018b9d21729ab73c621` successfully. The concrete client logged `Completion (reasoning-aware) done` with `ContentLen=4563` and `ReasoningLen=23414`; it did not enter `Completion request start` or token-limit continuation. The completed job persisted model-derived beats for the Studio workflow.

The handler assigns `RawModelResponse` and `ModelIdentifier` before parsing. The existing failure upsert therefore persists the exact final content and logs it with the exception. `SceneImageBeatAnalysisService` again uses standard JSON parsing without beat-specific repair.

Added handler-level regression tests using real temporary SQLite persistence to prove the reasoning-aware method is used, plain generation is not used, final content is parsed, and malformed final content is persisted verbatim on failure.

## Validated

- [x] Web build succeeded.
- [x] Focused beat parser and handler tests passed: 4/4.
- [x] Full test suite passed: 1190/1190.
- [x] Full solution build succeeded with only the existing AngleSharp advisory warning.
- [ ] Pending user confirmation in a fresh Scene Image Studio run.