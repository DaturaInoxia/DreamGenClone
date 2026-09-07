# Generic Edit Routed To Production Finish

## Report

On 2026-09-06, durable job `cc04e1922ffc4dbbb1b5e25f999c3f2b` failed for a generic `/roleplay/image-editor` edit of a production-derived image. The dashboard showed `durable_handler_unclassified_failure`; the target image persisted the actual error: `'m' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 0.`

## Analysis

The completed edit-prompt compilation proved the generic editor received and compiled the user intent. The durable payload pinned editor model `f264400b-39f1-40c9-a740-c16b44ecd343` and targeted image `3e5a6903-d822-48b2-a321-8965e88e37ef`.

`SceneImageService.EnqueueEditAsync` copied the production group from its source and incorrectly assigned `ProductionStage = Finish`. That routed the record into `SceneImageEditingJobHandler.ExecuteFinishAsync`, which expects `EditIntentSnapshot` to be the JSON metadata written only by the dedicated `EnqueueFinishAsync` action. Generic edits correctly store plain `attempt.RawIntent` in that field. Parsing a natural-language intent beginning with `m` as JSON caused the terminal failure before the Qwen image-edit transport could run.

The dedicated Finish path remains distinct: it writes `EditIntentSnapshot = JsonSerializer.Serialize(new { request.RequestAdultContent })`, sets its stage to `Finish`, and resolves the production editor configuration. The generic editor pins the exact user-selected editor model and must use the generic editing branch.

## Plan

1. Preserve canonical source and Moment lineage on generic edits of production-derived images.
2. Do not assign a production stage or stage disposition to a generic edit record.
3. Update the stale generic-edit test fixture with explicit exact-model and durable-queue dependencies.
4. Add a contract assertion guarding the generic/Finish branch boundary.

## Resolution

- `SceneImageService.EnqueueEditAsync` now retains `ProductionGroupId` and canonical source lineage but does not set `ProductionStage` or `Disposition`.
- `SceneImageServiceJobTests` now provides an explicit test editor model resolver and durable queue, and verifies a production-derived generic edit has null stage/disposition while retaining lineage.
- `SceneImageStudioUiContractTests` verifies the generic edit method cannot assign the Production Finish stage.

## Validated

- [x] The failed durable job, payload, target image stage, and handler stack trace were inspected from the development database and application log.
- [x] Focused production-derived generic-edit regression test passed: 1 total, 1 passed.
- [x] All generic `EnqueueEditAsync` tests passed: 5 total, 5 passed, 0 failed.
- [x] `SceneImageStudioUiContractTests` passed: 21 total, 21 passed, 0 failed.
- [ ] Pending browser acceptance: use `/roleplay/image-editor` on a production-derived source, select an exact editor model, and verify the resulting record has no production stage and reaches the configured Qwen edit transport.