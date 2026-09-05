# Debug Record 011: Beat Production Phase Diagnostics

## Report

- Session ID: current Copilot debug session (transcript `5951258f-e1b9-43c4-84b1-791c8ebf32f5`)
- Interaction ID: Beat Production instrumentation request
- Symptom: Beat Production could remain in an apparently long-running state, but the UI did not distinguish prompt construction, durable queue wait, provider response phases, JSON parsing, or validation.
- User request: measure prompt size, queue time, provider time, response size, and validation time separately; investigate whether long duration is caused by oversized context, model reasoning, provider queueing, or slow JSON generation.

## Analysis

The owning path is the durable Beat Production pipeline:

1. `SceneBeatProductionPipelineService` builds the immutable system and user messages before enqueueing the durable job.
2. `SceneBeatProductionPlanRepository` persists the attempt and computes durable queue wait from created and started timestamps.
3. `SceneBeatProductionPlanJobHandler` invokes the structured completion client and measures strict-output validation.
4. `OpenAiStructuredTextCompletionClient` uses `ResponseHeadersRead`, allowing request-to-response-headers and response-body-read durations to be measured separately. It also captures response bytes, provider usage metadata, and optional `reasoning_content`.
5. `SceneImageStudio.razor` displays the persisted measurements and gives a conservative interpretation.

The measurements can identify whether time is spent before the job starts, before response headers arrive, while reading the body, deserializing JSON, or validating output. Header wait is a combined client-observed interval: without provider-side queue timestamps or equivalent metadata, it cannot conclusively distinguish provider queueing from model startup/reasoning/generation. Provider usage and reasoning content are therefore captured as evidence, not treated as a precise phase timer.

Relevant specification artifacts consulted:

- `specs/001-final-writing-instruction/spec.md`
- `specs/001-final-writing-instruction/tasks.md`
- `specs/001-final-writing-instruction/research.md`
- `specs/001-final-writing-instruction/plan.md`
- `specs/001-final-writing-instruction/data-model.md`
- `specs/001-final-writing-instruction/contracts/slot-17-output-contract.md`
- `specs/001-final-writing-instruction/contracts/terminology-mapping.md`
- `specs/001-rp-prompt-redesign/spec.md`

## Plan

- Add nullable attempt fields for prompt, queue, provider, response, validation, usage, and reasoning diagnostics while preserving existing aggregate duration fields.
- Measure prompt characters, UTF-8 bytes, and prompt-build duration before durable enqueue.
- Measure queue wait from persisted timestamps and validation duration around strict parsing.
- Extend the structured completion result with response-header wait, body-read, response bytes, JSON deserialization, usage JSON, and reasoning content.
- Persist the metrics for both new and upgraded SQLite schemas.
- Surface the measurements in Beat Production Studio with plain-language interpretation.
- Add focused assertions for prompt persistence and provider diagnostic propagation.

Blast radius: Scene Beat Production attempt persistence, OpenRouter structured completion instrumentation, Beat Production status presentation, and focused RolePlay tests. Existing plan status, retry, ownership, and cancellation behavior are unchanged.

## Resolution

Implemented:

- Added nullable diagnostic fields to `SceneBeatAnalysisAttempt` and `StructuredTextCompletionDiagnostics`.
- Instrumented `OpenAiStructuredTextCompletionClient` around response headers, body read, JSON deserialization, response bytes, usage, and reasoning content.
- Added prompt size/build measurements in `SceneBeatProductionPipelineService`.
- Added queue and validation measurements in `SceneBeatProductionPlanJobHandler`.
- Added SQLite persistence, schema upgrade support, read mapping, and initial-insert values in `SceneBeatProductionPlanRepository`.
- Added Beat Production diagnostics display and interpretation in `SceneImageStudio.razor`.
- Added focused test assertions for prompt measurements and provider diagnostics.
- Fixed two positional SQLite insert defects found by the new assertions: an initial value-count mismatch and omitted prompt metric values.
- Removed the nullable warning introduced in the provider client; remaining infrastructure warnings are pre-existing.

## Validated

- [x] Focused Beat Production tests: 6 passed, 0 failed.
- [x] Infrastructure build: passed; 33 pre-existing warnings remain.
- [x] Clean Web build: passed earlier in this investigation.
- [x] Real OpenRouter DeepSeek request observed completing in approximately 26,989 ms.
- [x] Test project build passed.
- [x] Complete RolePlay test filter: 1,415 passed, 0 failed.
- [ ] Fresh post-build Studio run with actual persisted diagnostic values pending: the previously supplied URL now reports "Session or interaction not found".
- [ ] User confirmation pending.
