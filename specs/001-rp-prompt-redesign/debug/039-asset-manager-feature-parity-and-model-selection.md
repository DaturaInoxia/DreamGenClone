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
- Asset creation, upload, source editing, identity-pack commands, LoRA commands, and production
  approval were incorrectly accumulated on the Asset Manager catalog page.
- generic asset work used an in-memory channel, so a host restart left persisted assets permanently
  `Pending` with no work available to resume.

## Analysis

The production workload durability work was present, but generic scene assets still used the
process-local queue. Persisting only `Pending` asset state without the exact request made restart
recovery impossible. The UI also treated catalog browsing, creation, editing, identity management,
and governance review as one page instead of separate user workflows. The nearest controlling paths
were `SceneAssetService`, the three asset handlers, the durable lane worker/executor, and the Asset
Studio route components.

## Plan

1. Keep `/asset-studio` inventory-only and move creation, immutable editing, and production review
  to dedicated routes composed from reusable asset components.
2. Require asset name/type and pin exact generation/editor model IDs in persisted durable payloads.
3. Dispatch generation, editing, and profile-pack work through the existing durable image lanes.
4. Reconcile interrupted Pending assets at startup without guessing missing request values.
5. Preserve the dedicated Character Identity, LoRA, and session Production Studio boundaries.

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
- Asset Manager is now catalog-only: browse, filter, preview, picker return, versions, provenance,
  and lineage inspection. It contains no asset mutation controls.
- `/assets/create` owns exact-model prompt generation and upload. `/assets/{id}/edit` creates one to
  eight immutable children. `/assets/{id}/review` owns explicit production governance approval.
- Shared preview, metadata, prompt generation, upload, editing, and approval components keep those
  routes consistent without collapsing their responsibilities.
- Generation, editing, and profile-pack requests are persisted before enqueue and run on the
  existing durable `ImageRender` and `ImageEdit` lanes. Asset handlers have exactly one active
  scheduling path.
- Startup recovery first recovers expired durable leases, then reconciles Pending assets, then
  starts lane workers. Exact persisted requests are re-enqueued; terminal jobs are reflected on the
  asset; legacy rows without exact requests fail visibly instead of receiving inferred settings.
- Asset detail polling marshals reload and render updates through `InvokeAsync`.
- Text-to-image model choices exclude models configured for source-image editing. Exact generation
  resolution rejects those editor-only models explicitly, while compatible API models such as
  TogetherAI remain selectable.

## Validation

- Focused Scene Image Studio UI contracts: 9 passed.
- Focused final Asset Manager route/component/service contracts: 16 passed on 2026-09-03.
- Full RolePlay area: 1,411 passed on 2026-09-03.
- Final solution build succeeded in 2.2 seconds. The full suite passed 1,742/1,742 with no failures
  or skips in 178.9 seconds.
- Development startup changed historical orphan asset `0ca5d78b007d4a5b9e3d2d10c4fc9749`
  from `Pending` to explicit `Failed` with interruption guidance; no request value was inferred.
- Playwright covered catalog, create, detail, edit, and review at 1440 x 1000 and 390 x 844 with no
  horizontal overflow or page errors. Asset Creator exposed prompt/upload paths and exact configured
  model selection; failed/incomplete assets were blocked from edit and review.
- No generation/edit request, provider endpoint, Model Manager data, or pod was changed during
  browser acceptance.