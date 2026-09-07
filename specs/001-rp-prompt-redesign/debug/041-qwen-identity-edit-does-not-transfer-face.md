# 041 - Qwen identity edit does not transfer the approved face identity

**Date:** 2026-09-07
**Area:** Scene Image Studio identity editing / Qwen Image Edit 2511
**Related:** 038 (scene image edit compiler pixel regions), Qwen Image Edit 2511 instruction

## Report

For roleplay session `4f2eec18-b190-4beb-ad35-8d520ae5c800`, the Becky identity edit completed,
but the result did not visibly transfer Becky from the approved identity reference.

- Source image: `12c5a039-43ec-4de2-a471-e64df66ed075`
- Completed child: `f8be21de-c739-4965-b3c3-2c1f34ff187b`
- Selected character: Becky
- User symptom: the image appeared effectively unchanged as an identity correction

## Analysis

The source and child had different SHA-256 hashes, and the logs show both edits dispatched to
Qwen with the correct source image, model, and approved Becky face reference. The child used the
source image as image 1 and the approved face as image 2. The Qwen graph matches the accepted
multi-reference proof workflow (`TextEncodeQwenImageEditPlus` plus
`FluxKontextMultiReferenceLatentMethod`). This rules out a queue, storage, lineage, or UI-cache
failure. The provider made a small face-local change, but the identity instruction did not state
the distinguishing facial features that must transfer strongly enough.

## Plan

Strengthen the identity-only instruction at its owning source, without changing Qwen artifacts or
persisted sampler settings. Require transfer of distinguishing facial geometry, eye color and
shape, eyebrows, nose, lips, freckles, complexion markers, and hairline-adjacent details while
preserving the existing scene face angle, expression, lighting, body harmonization, and all
unselected content. Add a pure prompt regression test.

## Resolution

- Updated `SceneImageService.BuildFaceOnlyIdentityInstruction` with explicit identity-feature
  transfer requirements.
- Added `BuildFaceOnlyIdentityInstructionRequiresIdentityFeatureTransfer`.

## Validated

- [x] Source/output lineage and dispatch metadata verified from the development database and log.
- [x] Prompt regression test added.
- [ ] Fresh in-app Becky identity edit pending.