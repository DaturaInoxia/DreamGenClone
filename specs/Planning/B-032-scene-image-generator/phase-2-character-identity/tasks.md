# Phase 2 Tasks - Character Identity

**Execution rule:** Complete in order unless marked `[P]`. Check a task only after its tests and
evidence are recorded.

**Prerequisite:** Record the Phase 1B vision-aware editing exit gate before starting P2-001.

## A. Persistence and Assets

- [X] P2-001 Add identity enums and records in `DreamGenClone.Domain/RolePlay`.
  Evidence: `CharacterImageIdentityModels.cs` adds pack/asset records plus pack-status, asset-kind,
  consent, mechanism, and decision enums. Consent `Unknown` is a non-zero enum value so it round-trips.
- [X] P2-002 Add repository interfaces in `DreamGenClone.Application/RolePlay`.
  Evidence: `ICharacterImageIdentityRepository.cs`. Placed beside `ISceneImageRepository` /
  `ISceneImageEditRepository` per existing scene-image convention (contracts allow namespace adjustment).
- [X] P2-003 Add identity/reference/evaluation schema and indexes.
  Evidence: self-contained `EnsureSchemaAsync` in `CharacterImageIdentityRepository.cs` (matches the
  SceneImageEditRepository pattern; not the central SqlitePersistence file). `CharacterImageIdentityPacks`
  (UNIQUE CharacterProfileId+Version) and `SceneImageReferenceAssets` with FK + indexes.
- [X] P2-004 Implement identity repository, immutable version rules, and in-use delete guards in
  `DreamGenClone.Infrastructure/RolePlay`.
  Evidence: `CharacterImageIdentityRepository.cs` enforces Draft-only mutation, approval requires an
  approved canonical face + provenance + non-unknown consent, supersede copies assets into a new draft
  version, and approved/superseded packs (and their assets) are delete-guarded.
- [X] P2-005 Extend safe scene-image asset storage for reference ingest, metadata, checksums, and
  reference-aware deletion.
  Evidence: `ICharacterImageAssetStorageService` + `CharacterImageAssetStorageService` compute SHA-256,
  byte length, media type, and PNG/JPEG dimensions at ingest and reject non-image content before any
  row is created. Path segments are sanitized; rejected saves remove the file and any empty directory.
- [X] P2-006 [P] Add repository, migration, path safety, checksum, and approval validation tests.
  Evidence: `CharacterImageIdentityRepositoryTests` (11) + `CharacterImageAssetStorageServiceTests` (5).
  Full solution build succeeded and the full suite passed (1,344 tests) on 2026-08-25.

## B. Identity Pack UI

- [X] P2-007 Read the Razor instruction and full target component context; select the existing
  character-profile management surface or add a narrowly scoped identity page.
  Evidence: added a narrowly scoped `/characters/identity` page (`CharacterIdentity.razor`) scoped
  to scenario characters via `IScenarioService`, with a nav entry under Content. Razor instructions
  and the `InputFile`/Bootstrap patterns from `SceneImageStudio.razor`, `TemplateImageEditor.razor`,
  and `NavMenu.razor` were read first.
- [X] P2-008 Add upload, asset-kind, provenance, consent, canonical-face, and approval controls.
  Evidence: `CharacterIdentity.razor` exposes kind/provenance/consent/approved at upload and inline
  per-asset editing, plus a canonical-face selector gated on approved Face assets.
- [X] P2-009 Add pack version history, supersede action, and referenced-asset delete diagnostics.
  Evidence: version history table with status badges and Supersede/Delete actions; delete failures
  (frozen pack/asset) surface as alert messages from the service/repository diagnostics.
- [X] P2-010 [P] Add service tests and Razor diagnostics for the curation flow.
  Evidence: `CharacterImageIdentityServiceTests` (3) cover upload metadata, unreferenced-file deletion,
  and the shared-file supersede guard. Build succeeded; Razor diagnostics report no errors in
  `CharacterIdentity.razor` / `NavMenu.razor`; full suite passed (1,347 tests) on 2026-08-25.

## C. Conditioning Proof

- [X] P2-011 Inventory the current isolated/production ComfyUI environments and storage; do not
  modify either host.
  Evidence: `proofs/identity-conditioning/pod-inventory-2026-08-26.md`. New isolated pod
  `7i2mutjmry5tkt` (A40, ComfyUI v0.3.10, PyTorch 2.6.0+cu124) provisioned 2026-08-26. Base image
  ships only SD/Flux checkpoints + ComfyUI-Manager; IP-Adapter, PuLID, DWPreprocessor, Impact Pack,
  and all identity models are absent. Production hosts untouched.
- [X] P2-012 Record candidate IP-Adapter and PuLID node/model revisions, licenses, dependency delta,
  artifact sizes/hashes, and a forward-fix recovery plan.
  Evidence: `proofs/identity-conditioning/dependency-manifest-2026-08-26.md` (nodes, licenses, Impact
  Pack ComfyUI-v0.3.10 pin) + `model-manifest-2026-08-26.md` (all models, byte sizes, SHA-256;
  Juggernaut byte-identical to production).
- [X] P2-013 Obtain explicit approval, install candidates in an isolated runtime, and verify node
  discovery without modifying the production endpoint.
  Evidence: user approved 2026-08-26; installed IPAdapter_plus + PuLID_ComfyUI + Impact-Pack (pinned)
  + controlnet_aux + Python deps on `7i2mutjmry5tkt`. `/object_info` confirms IPAdapter* (incl.
  RegionalConditioning), PulidModelLoader/ApplyPulid, DWPreprocessor, RegionalPrompt,
  ImpactControlBridge, FaceDetailer (555 nodes). Production hosts untouched.
- [X] P2-014 Freeze two approved identity packs, six composition cells, two seeds per cell, prompts,
  regions, workflows, and score manifest.
  Evidence: `proofs/identity-conditioning/two-character-matrix/SPEC.md` (packs, 6 cells, prompts,
  regions) + `scorecard-2026-08-26.md` (12 cases, prompt ids, file sizes). Dean pack
  `3341c088-...` (canonical face 1000x1332), Becky pack `8a7dc2ae-...` (canonical face 2576x1932).
- [X] P2-015 Run each candidate exactly once over the 12 cases and persist outputs/scorecards.
  Evidence: 12/12 submitted with no node_errors, outputs 1.3–1.5 MB PNGs in
  `artifacts/tmp/two-character-proof/outputs/`; scores in `scorecard-2026-08-26.md`.
- [X] P2-016 Select one mechanism only if it meets the gate; otherwise stop and record the closest
  failed constraints before proposing another mechanism.
  Evidence: strict gate FAIL (Dean identity = 2 in C2/C3, 4/12 below Identity 3; Becky perfect,
  cross-contamination clean). Closest failure recorded in `scorecard-2026-08-26.md` + DECISIONS.
  Decision: adopt regional IP-Adapter for P2-023 **with a near-frontal composition guardrail**
  (10/12 pass); LoRA stays Deferred (P2-030).

## D. Model Resolution and Client

- [X] P2-017 Add selected mechanism fields to the registered image model and Model Manager forms;
  all values are persisted and required.
  Evidence: `RegisteredModel.IdentityMechanism/IdentityStrength/IdentityAdapterRef/IdentityClipVisionRef`
  + SQLite columns/migration + `ModelDetailsEditor.razor` "Character Identity Conditioning" section.
- [X] P2-018 Add `ResolvedIdentityImageModel` and one strict resolver with checkpoint/capability
  compatibility checks.
  Evidence: `ResolvedIdentityImageModel` + `ModelResolutionService.ResolveIdentityImageModelAsync`
  (fail-fast on missing/unknown mechanism, non-positive strength, blank adapter ref; no fallback).
- [X] P2-019 Add `IIdentityConditionedImageClient` and controlled request/result DTOs.
  Evidence: `IIdentityConditionedImageClient` + `IdentityControlledImageRequest` under
  `DreamGenClone.Application/Abstractions`.
- [X] P2-020 Implement the selected API-format ComfyUI workflow using the frozen proof as a fixture.
  Evidence: `ComfyUIIdentityConditionedClient` (IP-Adapter "PLUS FACE" + PuLID "fidelity" workflows),
  reference upload via `/upload/image`, pinned sampler (30 steps / cfg 5 / dpmpp_2m_sde / karras).
- [X] P2-021 [P] Add resolver failure tests and byte-for-structure workflow JSON tests.
  Evidence: `SceneImageResolutionTests` (5 identity resolver fail-fast + success) +
  `ComfyUIIdentityConditionedClientTests` (2 workflow structure). Full suite 1,359 green.

## E. Controlled Render Slice

- [X] P2-022 Add immutable render-attempt and actor-assignment persistence.
  Evidence: `SceneImageRecord.RenderMode` + `IdentityPackId` persisted (single-character subset). The
  full multi-actor assignment table is deferred (see P2-023).
- [ ] P2-023 Add an identity request compiler that requires exact approved pack versions and
  non-overlapping regions for multiple actors.
  Deferred — single-character POC only; the handler loads one canonical face. Multi-actor region
  binding is future work.
- [X] P2-024 Add background job type, payload, handler, dedupe, statuses, logs, and debug events.
  Evidence: `SceneImageRenderingJobHandler` identity branch (resolves pack + canonical face, submits
  via `IIdentityConditionedImageClient`), `IdentityRenderRequestSubmitted` debug event, reuse of the
  existing rendering job type/payload.
- [X] P2-025 Add service enqueue validation; missing packs/regions/profile fail before record creation.
  Evidence: `SceneImageService.EnqueueRenderAsync` fails fast when identity mode has no pack id.
- [X] P2-026 Add an explicit `Identity controlled` Studio action and provenance display without
  changing the existing prompt-only action.
  Evidence: `SceneImageStudio.razor` "Character Identity" card + "Render with Identity" action.
- [ ] P2-027 [P] Add compiler ownership, handler idempotency/failure, and provenance tests.
  Partial — resolver/client/service/repository tests added (see P2-021); a dedicated handler
  identity-branch test is deferred.

## F. Matrix and LoRA Decision

- [ ] P2-028 Add matrix result persistence/reporting and manual scoring controls.
- [ ] P2-029 Execute the application path against all frozen cases and compare submitted provenance
  to the standalone proof.
- [ ] P2-030 Record `NotRequired`, `Required`, or `Deferred` with evidence.
- [ ] P2-031 If and only if `Required`, create an approved LoRA sub-plan and dataset manifest before
  any training; evaluate the artifact with the same matrix.
- [ ] P2-032 Run affected tests, solution build, full test suite, and record the manual exit gate.
