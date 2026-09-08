# Debug 041 — Beat Production Audio Ownership and Pass Progress

## Report

- Reported from Scene Image Studio on 2026-09-08 while monitoring Beat Production.
- Session: `4f2eec18-b190-4beb-ad35-8d520ae5c800`.
- Studio route: `/roleplay/studio/4f2eec18-b190-4beb-ad35-8d520ae5c800/5fce8da8-6df9-47b7-8b63-abc84f19fc25`.
- Beat Production plan: `c15bcf03-e2ee-4fae-bb3d-c811b1b5232a`.
- Attempt: `f81ec552-8f90-478e-b87e-8ad65d75a550`.
- The attempt ran with `z-ai/glm-4.7` through OpenRouter and failed after 107047 ms.
- Validation error: `Video coverage 'vc_whole_beat' requires exactly one audio ownership entry per referenced cue.`
- The browser also had a stale Blazor connection, so the UI did not expose live progress reliably.
- The user could not see whether the individual decomposition prompts were executing.

## Analysis

- `SceneBeatProductionParser.ParseVideo` intentionally requires exact set equality between referenced dialogue, sound, and music cue keys and `audioOwnership` entries. Missing ownership is a semantic contract failure and must remain a hard failure.
- The decomposition handler executed structure, spoken, soundscape, and assembly as separate provider calls, but only the aggregate attempt was persisted. Individual prompt execution and pass timing were therefore invisible while the job was running.
- Assembly guidance did not explicitly require the complete union of all audio cue keys with exactly one ownership entry per key, allowing a structurally valid but incomplete assembly response.

## Plan

- Strengthen the assembly prompt with complete-union, exact-spelling, exactly-one-entry guidance and bump the production contract version.
- Persist pass-level progress in the existing attempt validation-details field, guarded by the current processing attempt and plan ownership.
- Track concurrent `spoken` and `soundscape` passes as a collection of active passes.
- Display current pass and completed pass trace in Scene Image Studio.
- Keep progress persistence best effort so telemetry cannot mask provider, parser, or cancellation failures.
- Add focused contract, repository, and production tests.

## Resolution

- Updated the assembly prompt and production contract from `scene-beat-production-v2` to `scene-beat-production-v3`.
- Added exact audio ownership guidance covering the complete union of dialogue, soundscape, and music keys, including the empty-union case.
- Added `TryUpdateProgressAsync` to the repository abstraction and SQLite implementation with ownership, current-attempt, and processing-status guards.
- Added `PassProgressTracker` to persist current passes, prompt character counts, output sizes, durations, completion state, and pass-specific errors.
- Added Scene Image Studio rendering for current pass names and pass trace status.
- Added repository progress persistence coverage and updated test doubles.

## Validated

- [x] Focused Beat Production tests: 43 passed, 0 failed.
- [x] Web project build: succeeded with 0 errors.
- [x] Razor diagnostics for Scene Image Studio: no errors.
- [ ] Full solution build pending.
- [ ] Fresh runtime Beat Production generation with the v3 contract pending.
- [ ] Live DB confirmation that `ValidationDetailsJson` contains pass progress and the assembly response contains complete audio ownership pending.
