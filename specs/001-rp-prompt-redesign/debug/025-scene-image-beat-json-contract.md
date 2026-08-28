# 025 - Scene Image Beat JSON Contract

## Report

On 2026-08-21, Studio displayed eight generated beat cards as `0.` with empty labels and descriptions reduced to `.`. The model job itself completed successfully for session `6e836089-0505-4b7b-b7d0-53e1ee81f15b`.

## Analysis

`SceneImageBeatGenerationJobHandler` persisted `BeatsJson` with `JsonSerializerDefaults.Web`, producing camelCase property names. `SceneImageStudio.LoadBeatAnalysisAsync` and `SceneImageService.EnqueuePromptAsync` deserialized that JSON with the default case-sensitive serializer. The JSON was syntactically valid, but all PascalCase `SceneImageBeat` properties remained at their default values. Studio therefore rendered `Order` as zero and strings as empty, and prompt generation could not reliably match the selected beat.

The beat-analysis parser and model output were not responsible for this display failure.

## Plan

- Use `JsonSerializerDefaults.Web` for beat JSON deserialization and serialization in `SceneImageStudio.razor`.
- Use the same contract for canonical beat validation and snapshots in `SceneImageService.cs`.
- Extend `SceneImageServiceJobTests` to verify a fully populated persisted camelCase beat survives into the prompt record.

Blast radius is limited to scene-image beat persistence consumers and prompt snapshot serialization.

## Resolution

- Added `JsonSerializerDefaults.Web` options to `SceneImageStudio` and used them when loading persisted beats and creating selected-beat snapshots.
- Added the same options to `SceneImageService` for selected-beat deserialization, canonical beat-list deserialization, and persisted prompt snapshots.
- Expanded `SceneImageServiceJobTests.EnqueuePromptAsync_CreatesPendingRecordAndEnqueuesJob` with a complete camelCase beat and assertions for its order, label, description, character, clothing, location, and time of day.

## Validated

- [x] All 10 `SceneImageServiceJobTests` pass.
- [x] Web project builds with no errors.
- [x] Full suite result: 1189 passed; the unrelated fixed-delay `ModelProcessingWorkerTests.Worker_ProcessesMultipleTasks_Sequentially` failed in the full parallel run and passed when rerun alone.
- [ ] Existing persisted beats render with populated order, label, description, characters, location, and time of day.