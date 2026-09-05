# Production Settings Contract Mismatch

## Report

The fresh Scene Image Studio action failed during durable revision creation with `Production compiler setting 'steps' must be an integer of at least 1.`

## Analysis

`SceneImageStudio.CreateInitialDurableRevisionAsync` serialized the mutable `SceneImageStudioSettings` object directly. The UI model uses `Cfg` and `SamplerName`, while `ProductionMediaCompilers` requires the qualified provider contract names `guidance` and `sampler`. Direct serialization also emitted the model's property casing rather than the compiler's explicit lower-case contract. Older restored image settings can additionally contain null values for steps and other fields. The compiler correctly rejected the invalid payload; the bootstrap supplied the wrong payload shape.

## Plan

Add an explicit durable settings projection at the Scene Production boundary. Validate required dimensions, steps, guidance, sampler, and scheduler. Preserve the optional blank-seed behavior by generating the execution seed only when the UI seed is intentionally blank. Keep compiler validation unchanged.

## Resolution

`SceneImageStudio` now calls the reusable `SceneImageProductionSettingsContract.Serialize(...)` projection for durable bootstrap. The projection emits the exact lower-case compiler contract (`width`, `height`, `steps`, `guidance`, `sampler`, `scheduler`, `negativePrompt`, and `seed`) and fails with a field-specific message before persistence when required UI settings are missing or invalid. `SceneImageStudioSettings` explicitly maps its active UI properties to the same `guidance` and `sampler` names when settings are restored by the render and prompt jobs.

## Validated

- [x] Razor diagnostics report no errors after the change.
- [x] Web build succeeds with existing warnings only.
- [x] Focused production compiler and Studio contract tests pass (`19` tests total).
- [x] Fresh Studio runtime loads the current Phase 2 page and resolves the qualified BigLust model without the prior `steps` exception.
- [ ] Fresh revision creation and new workload DB evidence remain blocked by the pre-existing session workload provider-key mismatch (`RunPod Serverless BigLust` endpoint label versus the qualified profile key); this is recorded as a separate runtime readiness issue rather than weakening compiler validation.
