# B-111 Face-Only Identity Edit From Any Attempt

## Report

On 2026-09-06, Production Studio Identity edits could only use a completed Composition attempt. The requested behavior is to apply identity again to any completed image in the same Moment production group, including a Finish or prior Identity attempt, while creating a new immutable Identity-stage attempt.

## Analysis

`SceneImageService.EnqueueIdentityAsync` already preserved production lineage by copying parent fields, setting `SourceImageId`, and creating `ProductionStage = Identity`. Its completed-Composition-only parent check was the blocking condition.

The old Studio instruction asked to preserve the composition, but it did not impose a face-local edit boundary. Pack assets already record an approved face view, while Qwen native multi-reference conditioning accepts ordered immutable image references. No capability qualification was changed or bypassed.

## Plan

1. Accept only a completed source that belongs to the exact production group.
2. Build the face-only preservation instruction in the service, not the UI.
3. Let the Studio select individual visible characters and one approved angle-tagged face asset for each.
4. Validate IDs server-side against the frozen Moment character, the exact approved pack, and immutable asset metadata.

## Resolution

- `SceneImageService.EnqueueIdentityAsync` now accepts a completed Composition, Identity, or Finish source only when it belongs to the requested production group.
- The persisted identity prompt is service-owned and limits the requested change to selected character faces, preserving all other faces, bodies, pose, clothing, framing, background, lighting, objects, and composition.
- Production Studio offers `Edit identity` from any completed attempt and provides a per-character inclusion control plus a selector containing only approved, angle-tagged face assets from the resolved pack.
- The request carries character and reference asset IDs only. `SceneImageProductionService` resolves the assets and verifies ownership, approval, face kind, and frozen-Moment membership before persisting the binding, including the selected `FaceView`.

## Validated

- [x] `SceneImageStudioUiContractTests` passed: 19 total, 19 passed.
- [x] `SceneImageIdentityReadinessTests` passed: 3 total, 3 passed.
- [x] Combined focused run passed: 22 total, 22 passed, 0 failed, 0 skipped.
- [x] `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore --nologo -c Release` passed with 0 errors.
- [ ] The broader `FullyQualifiedName~SceneImage` slice has 12 existing `SceneImageServiceJobTests` failures because its fixture omits the now-required exact render/editor model resolvers and fails before reaching each asserted behavior. This feature did not change those resolver requirements.
- [ ] Pending browser acceptance against a completed production attempt and the configured editor endpoint.
- [ ] Native Qwen multi-reference capability remains unqualified; no qualification record was created.