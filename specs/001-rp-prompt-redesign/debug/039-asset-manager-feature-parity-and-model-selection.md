# 039 - Asset Manager feature parity and exact model selection

**Date:** 2026-09-03
**Area:** Asset Manager / Scene Image Studio / durable asset jobs
**Related:** B-032 Phase 2 P2-052 through P2-056

## Report

The Phase 2 durable production workspace was mounted for current-generation sessions before it
reached feature parity with the existing Scene Image Studio. Current sessions therefore lost
access to working model selection, generation, source editing, and revision controls. Asset Manager
also exposed separate prompt/upload/profile cards without the required Create Asset, Create
Identity Pack, and Create LoRA workflow, and its durable jobs selected function defaults only after
enqueue.

Observed defects:

- prompt assets had no required semantic type and upload hardcoded `CharacterFace`;
- generation payload camelCase was deserialized with incompatible serializer defaults;
- generation and editing workers resolved hidden function defaults instead of exact selected IDs;
- profile-pack front generation and angle editing also hid their model decisions;
- prompt assets sent ordinary descriptions directly to every model family;
- current sessions hid the prior Studio controls;
- Asset Studio detail polling called `StateHasChanged` outside the Blazor dispatcher.

## Analysis

The backend durability work was present, but the UI replacement crossed the feature-parity cutoff
too early. The nearest controlling paths were `SceneAssetService` job creation, the three asset job
handlers, and current-session visibility gates in `SceneImageStudio.razor`.

## Plan

1. Preserve the durable Production Workspace while restoring access to existing Studio controls.
2. Require asset name/type and pin exact generation/editor model IDs in durable payloads.
3. Compile plain semantic descriptions deterministically for the selected model family.
4. Expose explicit Asset, Identity Pack, and LoRA commands with multiple immutable outputs.
5. Repair detail-page dispatcher polling and add source/service regression coverage.

No provider, endpoint, pod, or Model Manager database configuration is changed.

## Resolution

- Current sessions retain `ProductionWorkspace` and can again use existing production and legacy
  generation/edit/model-selection controls.
- Generated and uploaded assets persist their selected `SceneAssetType`.
- Generation jobs pin exact model ID and size; edit jobs pin exact editor model ID; profile-pack
  jobs pin exact front-generator and angle-editor model IDs.
- Asset descriptions remain semantic source text. The worker records a separate deterministic
  family-specific compilation and compiler identity; Pony receives the qualified quality/rating/
  count prefix while SDXL and API natural-language families preserve semantic prose.
- Asset Manager now exposes Create Asset, Create Identity Pack, and Create LoRA commands. Generate
  and edit workflows support one to eight separately persisted outputs without overwriting source
  assets.
- Asset detail polling marshals reload and render updates through `InvokeAsync`.
- Text-to-image model choices exclude models configured for source-image editing. Exact generation
  resolution rejects those editor-only models explicitly, while compatible API models such as
  TogetherAI remain selectable.

## Validation

- Focused Scene Image Studio UI contracts: 9 passed.
- Focused Asset Manager UI and asset service contracts: 17 passed on 2026-09-03.
- Full RolePlay area: 1,409 passed on 2026-09-03 before the final catalog-filter correction.
- Final post-fix full solution: 1,742 passed, 0 failed or skipped on 2026-09-03; build succeeded
  in 199.7 seconds.
- Playwright at 1440 x 900 and 390 x 844 found no horizontal overflow. Create Asset exposed prompt
  and upload paths at both sizes. TogetherAI remained selectable, the configured default remained
  visibly labeled, and Qwen Image Edit was absent from generation choices.
- No generation/edit request, provider endpoint, Model Manager data, or pod was changed during
  browser acceptance.