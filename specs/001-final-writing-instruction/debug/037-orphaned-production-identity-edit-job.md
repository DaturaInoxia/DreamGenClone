# Orphaned Production Identity Edit Job

## Report

On 2026-09-06, the durable-job view showed `scene-image-editing` job `11cd657a4ad9` as `Processing`, with 1/1 attempts, no error, no resolved model, and a lease held until 6:58 PM. The question was whether the Qwen image edit was working.

## Analysis

The read-only durable-job inspection identified the full job ID as `11cd657a4ad948f58cbb03bb559bbacb`. Its payload targets scene image `12797d62-bc7e-4145-a223-bff7f33dfbad` for session `4f2eec18-b190-4beb-ad35-8d520ae5c800` and interaction `dad139a5-8e98-469b-ab74-8e55404f1732`.

The target is a `Generating` `Edit` at production stage `Identity`, with no completion time, resolved model, provider, output, or application error. The blank payload `EditorModelId` is valid for the Identity branch; it does not use the generic editor-model path.

The job was last renewed at 6:43:35 PM and remains owned by a previous process until 6:58:35 PM. The worker that held that lease was stopped while the handler was in progress, leaving no process able to complete the claimed job. `RecoverExpiredLeasesAsync` marks a processing job with `AttemptCount == MaxAttempts` as `Failed`, using `lease_expired_attempts_exhausted`; it does not retry it.

## Plan

1. Do not change Qwen model configuration, capability gating, or the image-edit handler from this orphaned-job observation.
2. Let the current durable worker recover the expired lease into its explicit terminal failure state.
3. Submit a new Identity edit from the completed source to create a new immutable job if a render is still needed.

## Resolution

- Added `DreamGenClone.DbQuery/queries/inspect-scene-image-edit-job.sql`, a read-only query that correlates a UI-visible durable-job ID prefix with its payload, target scene image, and editor session.
- No production behavior or persisted job state was changed.

## Validated

- [x] The inspection query resolved the exact durable job, payload, and target scene image.
- [x] The target record was confirmed as an Identity-stage edit and remains `Generating` without output or a resolved model.
- [x] Recovery behavior was verified in `DurableBackgroundJobRepository.RecoverExpiredLeasesAsync`.
- [ ] Pending worker recovery after the lease expires, followed by a newly submitted edit if a replacement render is required.