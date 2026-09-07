# Qwen Image Edit Native Reference Conditioning

## Report

On 2026-09-06, the user stated that Qwen Image Edit supports IP-Adapter and expects the scene-image editor to offer identity/reference conditioning when the selected Qwen edit endpoint supports it.

## Analysis

The configured Qwen Image Edit 2511 model is `f264400b-39f1-40c9-a740-c16b44ecd343` on the enabled `RunPod Qwen Image Edit` serverless endpoint. Its live Model Manager fields currently declare no visual strategies and contain no qualification entries.

The actual shipped Qwen editing client supports multiple reference images: `ComfyUIImageEditingClient.EditWithReferencesAsync` uploads ordered reference files, adds `image2...imageN` inputs to `TextEncodeQwenImageEditPlus`, and passes its conditioning through `FluxKontextMultiReferenceLatentMethod`. The portable Qwen workflow confirms the same graph. This is Qwen's native multi-reference conditioning mechanism; it is not ComfyUI IP-Adapter (no IP-Adapter loader, adapter weights, or CLIP-Vision node exists in the Qwen runtime/provisioning scripts).

The current scene-image editing handler requires the generic strategy name `ReferenceConditioning` and then checks that it resolves to `ReferenceConditioning`, but the Qwen workflow has a different implemented mechanism. Therefore Qwen's real native reference support cannot be truthfully enabled in the UI today. Declaring it as IP-Adapter would be inaccurate and would assert an unsupported graph implementation.

Prior evidence exists for Qwen's actual native mechanism. B-032 P2-044 records a six-cell composition-to-identity Qwen matrix run on 2026-09-02 against `img-qwen-edit-serverless`; the user confirmed that ordered Dean/Becky face references were applied. The code also has workflow-structure tests proving ordered references bind to `image2...imageN` on both Qwen encoders. The produced matrix artifacts and request/reference hashes are under ignored `artifacts/tmp/qwen-composition-identity/20260902-182349/`, and the per-cell gate review/scorecard was never completed. B-111 therefore correctly classifies the endpoint as unqualified, not untested.

## Plan

1. Add a distinct persisted visual strategy, `NativeMultiReference`, for the Qwen Image Edit model and qualify it against the existing Qwen endpoint after the retained 2026-09-02 matrix is reviewed or reproduced through the current B-111 scorer and records a passing proof ID.
2. Extend the capability resolver and `ReferenceApplyPanel` strategy choices to offer `NativeMultiReference` only for the Identity element when the selected editor model declares and qualifies it. Continue to offer TextOnly everywhere.
3. Update `SceneImageEditingJobHandler` to execute Qwen native references when the qualified selected strategy is `NativeMultiReference`. Keep `ReferenceConditioning` reserved for actual IP-Adapter graph endpoints. Unsupported/unqualified strategies must fail explicitly; no fallback or capability relabeling.
4. Add focused tests for strategy resolution and the Qwen native-reference dispatch route, then run the documented portable proof and update Model Manager only with the actual proof ID.

## Blast Radius

This affects the editor capability vocabulary, the reference selector, Model Manager metadata, the scene edit handler, and focused tests. It does not modify the Qwen sampler/settings, Composition rendering, IP-Adapter endpoints, or default model resolution. The Qwen model will remain TextOnly in the UI until a real native-reference qualification proof is recorded.

## Resolution

Implemented the native Qwen reference capability as an explicit `NativeMultiReference` visual
strategy. `ReferenceStrategyResolver` recognizes it but continues to require the exact persisted
model declaration plus a qualified proof entry. `ReferenceApplyPanel` offers it only for Identity
references, and `SceneImageEditor` supplies it to that panel alongside TextOnly. The normal
scene-image editing worker accepts only this strategy for the existing Qwen ordered-reference
transport; `ReferenceConditioning` remains reserved for an actual IP-Adapter implementation.

`ModelDetailsEditor` now exposes a Qwen native multi-reference declaration checkbox and preserves
the value in `SupportedVisualStrategiesJson`. The live dev Model Manager record for Qwen Image
Edit 2511 now declares `["NativeMultiReference"]`; its qualification array remains `[]`, so the
editor visibly explains that the strategy cannot be submitted until endpoint-specific proof is
recorded. No qualification was fabricated.

The live composition-first proof was submitted on 2026-09-06 with the frozen C1 source and the
ordered Dean then Becky reference fixtures, but its first RunPod job did not leave the queue before
the runner's 900-second deadline. No output image or `run-manifest.json` was written. Subsequent
endpoint health reported one queued job with one idle/ready worker. This is an endpoint queue or
dispatch investigation, not a failed visual proof. The frozen Becky fixture hash matches the sole
currently approved canonical face asset in the dev DB. The frozen Dean fixture has no corresponding
approved identity pack in the current dev DB, so a rerun using this fixture set cannot qualify the
current app identity-pack workflow until Dean has an approved pack and the runner uses both live
canonical assets.

The composition-first runner now draws its references from the supplied five-angle pack at
`specs/image-generator-tests/identity-two-character/refs/multiangle`. C1 uses `dean_front` and
`becky_front`; the inward-facing C2/C3 compositions use `dean_34r` and `becky_34l`. The immutable
transport order remains source composition as image1, Dean as image2, and Becky as image3.

## Validated

- [x] Live Qwen model metadata inspected: `SupportedVisualStrategiesJson = []`, `CapabilityQualificationsJson = []`.
- [x] Qwen client workflow inspected: ordered multi-reference images are consumed by `TextEncodeQwenImageEditPlus` / `FluxKontextMultiReferenceLatentMethod`.
- [x] Qwen runtime provisioner inspected: no IP-Adapter assets or graph nodes are installed.
- [x] Prior native-reference evidence traced: 2026-09-02 six-cell Qwen composition-to-identity matrix, with user-confirmed ordered Dean/Becky faces and retained request/reference hashes.
- [x] Qwen native strategy wired through resolver, Model Manager, scene-image editor, and durable Qwen reference dispatch.
- [x] Qwen Model Manager declaration persisted in the live development DB; qualification intentionally remains empty.
- [x] Release Web build completed without errors after the resolver/worker/UI changes.
- [x] Canonical Qwen proof package integrity verified: six covered non-explicit edits pass recorded hash checks.
- [x] Frozen composition-first Qwen graph validated for six cases: source, Dean, and Becky bind to image1, image2, and image3 respectively.
- [ ] Current-endpoint proof pending: first 2026-09-06 job remained queued through the 900-second client deadline; no rendered output exists.
- [ ] Current-app asset proof pending: approve Dean's identity pack, then replay against the exact currently approved Dean and Becky canonical faces.
- [ ] Pending formal B-111 per-cell qualification review or reproduction before live submission is enabled.
