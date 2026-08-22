# 009 — Scene image beat analysis receives no final answer after reasoning

**Report**

- Session: `6e836089-0505-4b7b-b7d0-53e1ee81f15b`
- Interaction: `4eed9d72-6c87-4f23-bada-d34f372e8d48`
- Studio route: `/roleplay/studio/6e836089-0505-4b7b-b7d0-53e1ee81f15b/4eed9d72-6c87-4f23-bada-d34f372e8d48`
- Symptom: Image Beats reported `Scene image beat analysis returned empty output.`
- Job: `ed9d4b4d0854427db2ba5630b6730b92`, started 2026-08-21 13:27:52 -04:00 and failed at 13:30:36 -04:00.

**Analysis**

- The canonical `helpers/dbq-session.ps1` command could not inspect persisted state because the required `artifacts/tmp/dbquery` project is absent. No ad-hoc database query was used.
- Application log evidence in `DreamGenClone.Web/logs/dreamgenclone-20260821.log` is conclusive:
  - The configured scene-image preprocessor model was `deepseek-ai/DeepSeek-V4-Flash-0731` via TogetherAI.
  - The reasoning-aware completion extracted 34,246 reasoning characters.
  - The forced-answer continuation also returned no final content: `Force-answer call still empty after length`.
  - Completion then returned `ContentLen=0, ReasoningLen=34246, DurationMs=164100`.
  - `SceneImageBeatAnalysisService.ParseOutput` correctly rejected the empty final answer. It did not parse reasoning as JSON and did not silently manufacture a beat.
  - A subsequent beat request for anchor interaction `b3e2caf1-cc18-437f-a6c1-7b549e054cc8` failed after returning 43,248 characters: the response began as JSON, then contained the model's continuation deliberation, followed by another partial JSON fragment. The strict parser rejected it at line 18 with `'W' is an invalid start of a value.`
  - `CompletionClient.ContinueTruncatedResponseAsync` is the corruption point: it calls `ParseContent`, whose fallback assigns `reasoning_content` to `content`, then concatenates that reasoning into a partial structured response. This behavior is invalid for any JSON contract.
  - Current schema-v2 POV prompt records correctly omit the camera-holder and characters outside the selected POV's visible set. However, the fixed-identity block is separately resolved from the selected interaction and does not receive the selected beat. A beat-visible character can therefore be requested by the authoritative render brief without that character's appearance profile being available to the preprocessor. In addition, `SceneImagePromptGenerationJobHandler` silently falls back to a single-interaction path when full-turn resolution fails, dropping the render brief entirely.
  - Root cause: generic completion recovery is unsafe for structured output, and the image prompt's appearance context is not derived from its canonical frozen beat.

**Plan (approved)**

1. Correct the completion-client continuation boundary so structured callers never append `reasoning_content` as final output; retain reasoning only as diagnostics.
2. Persist the reasoning returned by the single configured-model beat-analysis call beside its final response. A malformed or empty final answer must fail explicitly with no scene-image retry, parser repair, or invented beat content.
3. Derive the preprocessor's character-appearance and clothing context from the selected canonical beat and selected POV, including every character the render brief can depict and excluding only characters guaranteed to remain outside the frame.
4. Remove the single-interaction fallback for beat-backed image prompts. Full-turn/beat resolution failure must leave the prompt record failed with a diagnostic rather than generate an incomplete prompt.
5. Add targeted tests for: length-truncated structured responses containing reasoning; persistence of returned reasoning; beat-visible identities in POV prompts; and full-turn resolution failure.
6. Build and run the affected Scene Image / RolePlay tests, then inspect a fresh failed or successful beat analysis for its persisted reasoning before changing prompt/model behavior further.

**Blast Radius**

- `DreamGenClone.Infrastructure/Models/CompletionClient.cs` and completion tests.
- `DreamGenClone.Web/Application/RolePlay/SceneImageBeatGenerationJobHandler.cs`, `SceneImagePromptGenerationJobHandler.cs`, `SceneImagePromptPreprocessor.cs`, participant/context helpers, and focused tests.
- No database migration, no persisted-contract change, no gate threshold path, no prompt-slot redesign, no alternate model selection, and no fallback or guessed beat content.

**Resolution**

- `CompletionClient` no longer substitutes reasoning for final content. Beat generation performs one configured-model call, persists final content and reasoning separately, and fails strict parsing without a handler retry.
- `SceneImagePromptGenerationJobHandler` no longer asks a text model to rewrite beat-backed image prompts. It persists a deterministic projection of the frozen schema-v2 beat, selected POV, configured image settings, canonical identity, and canonical wardrobe.
- `SceneImageRenderBriefBuilder.ResolveVisibleCharacters` is the single active POV-to-visible-cast decision path. Omniscient includes the complete beat cast; character POV includes exactly that camera holder's configured `VisibleCharacterNames` and fails explicitly for a missing POV, unknown visible character, or self-visible camera holder.
- Character POV camera language now specifies strict first-person eye-position geometry and excludes external, mirror, selfie, and over-the-shoulder views. The camera holder is absent from the positive cast and explicitly excluded from the frame.
- Positive cast/action sections omit characters outside the selected POV's visible set, including free-text spatial facts that name an excluded beat character.
- Canonical identity and wardrobe lines are assembled without LLM paraphrasing and remain byte-identical wherever the same person is visible across POVs.
- Removed the duplicate `Body type` field from visual identity formatting.
- Added focused tests for cross-POV identity/wardrobe equality, positive visible-cast filtering, camera-holder exclusion, missing-POV failure, first-person geometry, and non-duplicated body type.
- Repaired the missing `DreamGenClone.Application.ModelManager` import in `CompletionClientReasoningTests`, which independently blocked test-project compilation.

**Validated**

- [x] Failure reproduced and traced from the specified Studio route through the background-job log.
- [x] Root cause distinguishes an empty final answer from invalid JSON or parser behavior.
- [x] Focused Scene Image prompt/POV tests: 38 passed, 0 failed.
- [x] All Scene Image tests: 99 passed, 0 failed.
- [x] Full solution tests: 1205 passed, 0 failed (`Release`; Debug output was held by the running web process).
- [x] No editor diagnostics in touched implementation or test files.
- [ ] Fresh Studio generation from the reported session confirms Seedream follows the projected POV and identity contract.
