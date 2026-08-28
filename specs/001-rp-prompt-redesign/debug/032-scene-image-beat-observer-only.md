# 032 — Scene image beat analysis emitted an observer-only beat (zero active characters)

## Report

- Studio route: `/roleplay/studio/423a270c-49a6-4cad-9ccf-94d68bdc6e7c/36d487e6-58a6-4c7d-8d54-34cb1d050676`
- Session: `423a270c-49a6-4cad-9ccf-94d68bdc6e7c`
- Anchor interaction: `36d487e6-58a6-4c7d-8d54-34cb1d050676`
- Turn: `230703fd4cce47448a4989a8881012dc`
- Beat analysis record: `5af29c96-9a01-49e8-84d0-e3066d79a0f9`
- Error shown in Studio: `Beat analysis failed: Scene image beat 'b3' must include at least one active character.`
- Model: `deepseek-v4-flash` · Occurred: 2026-08-25 20:07 UTC.

## Analysis

`SceneImageBeatAnalysisService.ParseOutput` (line 114–116) enforces the schema-v3 contract that every beat includes ≥1 active character. The model's raw response (7,592 chars) contained an observer-only establishing/contrast beat:

- `beatId: "b3"`, `label: "Ken reading outside the trailer"`
- `location: "inside the trailer"` — a location 20 yards from the active-event shed
- single character `Ken` with `involvement: "observer"` — **zero active characters**

The beat depicts the oblivious husband alone in the trailer while the sexual activity (beats b1/b2/b4) happens in the shed. Per the schema-v3 contract, a remote observer alone is not a material state change, so the beat is invalid. The parser correctly rejected it (fail-fast) and `SceneImageBeatGenerationJobHandler` marked the record Failed and surfaced the message.

This is the **intended safety net working** — not a validator defect. The system prompt already contained anti-observer-beat guidance ("Attach that observer … instead of emitting an observer-only establishing beat"), but `deepseek-v4-flash` still chose the cinematic "oblivious husband vs. hidden affair" contrast shot. The contrast/establishing shot is a natural temptation for the model, so the failure was expected to recur on re-runs without prompt hardening.

## Plan (approved)

1. **Prompt hardening (forward code change):** in `SceneImageBeatAnalysisService.BuildMessages`, add an explicit hard rule — every beat must include at least one active character; a beat whose characters are all observers (a lone figure in a separate location not physically causing/undergoing the state change) is invalid; never emit establishing, transition, or contrast shots with zero active characters; attach such observers to the simultaneous active-event beat or omit them. Reinforce the "A beat may have…" line to state that at least one active character remains mandatory. Keep the strict parser unchanged (no JSON repair, no invented content).
2. **Regression tests:** add a parser test asserting an observer-only beat is rejected, and extend the prompt-content test to assert the new hard-rule wording is present.
3. **Build + test** (Scene Image suite and full suite).
4. Create this debug record.

## Resolution

- `DreamGenClone.Web/Application/RolePlay/SceneImageBeatAnalysisService.cs` — added one system-prompt line after the active/observer classification line:
  > "Every beat must include at least one active character. A beat whose characters are all observers — a lone figure in a separate location who is not physically causing or undergoing the state change — is invalid; never emit establishing, transition, or contrast shots with zero active characters. Attach such observers to the simultaneous active-event beat or omit them."
- Same file — reinforced the beat composition line:
  > "A beat may have additional active characters and zero or many observers. Include every character actively involved in or observing that moment. Every beat still requires at least one active character."
- `DreamGenClone.Tests/RolePlay/SceneImageBeatAnalysisServiceTests.cs`:
  - `ParseOutput_RejectsObserverOnlyBeat` — converts the solo beat's involvement to `observer` and asserts `InvalidOperationException` with "at least one active character".
  - Extended `BuildMessages_DefinesNarrativeFirstEnsembleGrouping` to assert both new hard-rule phrases are in the system prompt.
- Added reusable query files under `DreamGenClone.DbQuery/queries/` (`scene-image-beat-analyses-session.sql`, `scene-image-beat-failed-raw.sql`) for inspecting beat-analysis records.

## Validated

- [x] Full test suite: 1328 passed, 0 failed.
- [x] All Scene Image tests (`FullyQualifiedName~SceneImage`): 199 passed, 0 failed.
- [x] `SceneImageBeatAnalysisServiceTests`: 12 passed, 0 failed (includes 2 new/updated tests).
- [x] No editor diagnostics in touched implementation or test files.
- [ ] Fresh Scene Image Studio run on session `423a270c...` (turn `230703fd...`): regenerate beats — confirm beat set is all-active-compliant and `Status=Complete` (record `5af29c96...` is Failed and will be superseded on regeneration).
