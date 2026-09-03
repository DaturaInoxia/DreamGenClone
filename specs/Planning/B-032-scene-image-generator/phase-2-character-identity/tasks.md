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
- [X] P2-023 Add an identity request compiler that requires exact approved pack versions and
  non-overlapping regions for multiple actors.
  Evidence: `IdentityControlledRequestCompiler` strictly parses persisted selections, resolves each
  exact approved pack version and its approved owned canonical face, preserves ordered actor labels,
  strengths, hashes, and explicit normalized regions, and fails on malformed or ambiguous input.
  `SceneImageService` rejects missing, invalid, duplicate, or overlapping multi-actor ownership
  before record creation. Both ComfyUI transports now require approved mask bytes or the explicit
  region; the hidden synthesized-band fallback was removed.
- [X] P2-024 Add background job type, payload, handler, dedupe, statuses, logs, and debug events.
  Evidence: `SceneImageRenderingJobHandler` identity branch (resolves pack + canonical face, submits
  via `IIdentityConditionedImageClient`), `IdentityRenderRequestSubmitted` debug event, reuse of the
  existing rendering job type/payload.
- [X] P2-025 Add service enqueue validation; missing packs/regions/profile fail before record creation.
  Evidence: `SceneImageService.EnqueueRenderAsync` fails fast when identity mode has no pack id.
- [X] P2-026 Add an explicit `Identity controlled` Studio action and provenance display without
  changing the existing prompt-only action.
  Evidence: `SceneImageStudio.razor` "Character Identity" card + "Render with Identity" action.
- [X] P2-027 [P] Add compiler ownership, handler idempotency/failure, and provenance tests.
  Evidence: direct compiler tests cover exact two-actor ordering, pack versions, asset ownership,
  hashes, strengths, regions, malformed JSON failure, and wrong-pack canonical-face failure;
  `SceneImageRenderingJobHandlerTests` proves completed records return without invoking compilation
  or transport. Direct compiler/handler tests passed 4/4, transport tests 6/6, and combined focused
  identity tests 11/11 on 2026-09-02.

## F. Historical Reference-Conditioning Matrix

- [X] P2-028 Add matrix result persistence/reporting and manual scoring controls.
  Evidence: `SceneIdentityEvaluationRepository` atomically persists immutable ordered matrix cases,
  append-only per-attempt manual score results, reviewer/output provenance, and evidence-backed
  per-pack decisions. Scores accept only `Pass`, `Fail`, or `NotScored`; duplicate pack/run
  decisions and recursively nested secret fields are rejected. Real-SQLite evaluation tests passed
  4/4 and the combined identity persistence suites passed 20/20 on 2026-09-02.
- [ ] P2-029 Execute the application path against all frozen cases and compare submitted provenance
  to the standalone proof.
- [X] P2-030 Preserve the dated `NotRequired`/`Required`/`Deferred` result as historical
  reference-conditioning evidence only; it does not control whether the product supports LoRA.
  Evidence: `DECISIONS-2026-08-26.md` retains the original `Deferred` disposition and is superseded
  for product architecture by `DECISIONS-2026-09-03-LORA.md`.
- [X] P2-031 Supersede the conditional LoRA branch before any training occurs.
  Evidence: no LoRA training/artifact was created under the rejected conditional design. The
  approved first-class implementation begins at P2-060 with synthetic Asset Manager datasets.
- [ ] P2-032 Run affected tests, solution build, full test suite, and record the manual exit gate.

## G. Clean Production Foundation

- [X] P2-033 Reconcile completed POC records with FR2-001 through FR2-045 and record the forward-only
  replacement map; preserve proof history and add no legacy adapter.
  Evidence: `poc-production-reconciliation.md` maps the identity POC, shared Scene Asset catalog,
  production groups/attempts/approval, configuration, proof evidence, and UI to their production
  owners and follow-up tasks. It keeps existing rows as historical evidence, selects `SceneAsset`
  as the shared byte/metadata catalog, preserves explicit approved-frame promotion, and forbids
  backfill, dual paths, synthetic lineage, or one-off fallback for new production sessions.
- [X] P2-034 Add session production-schema generation and reject older sessions with create-new-session
  guidance before any production mutation.
  Evidence: `SceneImageProductionSchema.CurrentGeneration` is the one domain protocol source;
  canonical create/clone/fork paths explicitly persist it on `RolePlaySession`, while absent legacy
  JSON and imports remain unstamped. `SceneImageProductionSessionGuard` loads the exact session and
  rejects missing/mismatched generations with create-new-session guidance. The guard runs before
  every session-scoped `SceneImageProductionService` mutation; there is no backfill, default-on-read,
  compatibility adapter, or legacy one-off fallback. Focused guard/production tests passed 21/21 and
  the canonical creation-stamp test passed 1/1 on 2026-09-02.
- [X] P2-035 Add body-profile and wardrobe-look version aggregates, repositories, approval rules,
  provenance, consent/license, and in-use deletion guards.
  Evidence: `CharacterAppearanceVersionModels.cs`, `ICharacterAppearanceVersionRepository.cs`, and
  `CharacterAppearanceVersionRepository.cs` add independently versioned body/wardrobe aggregates,
  exact shared-asset bindings, draft-only mutation, immutable approval, copied supersession lineage,
  and typed retention guards. `CharacterAppearanceVersionRepositoryTests` passed 7/7 on 2026-09-02.
- [X] P2-036 Add media capability profile/cell, production intent, compiled request, workload/item,
  attempt, derivative, review, and ordered-reference-binding records and schema.
  Evidence: `ProductionMediaModels.cs`, `IProductionMediaRepository.cs`, and
  `ProductionMediaRepository.cs` add immutable payload snapshots plus normalized lineage/hash/state
  columns, transactional request/binding and workload/item graphs, ordered shared-asset references,
  append-only reviews, approved derivatives, and compare-and-swap execution state. Infrastructure
  build succeeded on 2026-09-02.
- [X] P2-037 [P] Add clean-baseline, migration/schema, immutability, state-transition, concurrency,
  content-hash, and retention tests.
  Evidence: `CharacterAppearanceVersionRepositoryTests` (7) and
  `ProductionMediaRepositoryTests` (5) exercise clean SQLite schema creation, exact hash rejection,
  immutable graph round-trip, legal/illegal transitions, stale-version rejection, provider-submit
  idempotency, late-result rejection, secret filtering, shared-asset checksums, and retention FKs.
  Focused suites passed 12/12 on 2026-09-02.

## H. Model-Native Compilation And Qualification

- [X] P2-038 Reconcile the provider evidence ledger with the canonical compiler standards and
  family instruction files before coding: add researched exact settings/rules for FLUX.2 and Qwen
  generation, preserve current Pony/SDXL/Qwen Edit load-bearing rules, and record any deviation.
  Evidence: refreshed official BFL, Together, and Qwen sources on 2026-09-02; added dedicated
  `flux2-prompting.instructions.md` and `qwen-image-generation.instructions.md`; reconciled exact
  dimensions, steps/guidance, negative-field, ordered-reference, fixed-endpoint, and generation/edit
  separation rules into the canonical standards and `provider-evidence-matrix.md`. No existing
  Pony, SDXL/Juggernaut/BigLust, or Qwen Edit rule was weakened.
- [X] P2-039 [P] Implement Pony and SDXL/Juggernaut/BigLust compilers using the canonical model-family
  standards and exact capability schemas.
  Evidence: `PonyProductionMediaCompiler` and `SdxlProductionMediaCompiler` deterministically compile
  structured production intent into family-native prompts and enforce exact Pony and SDXL/BigLust
  sampler, step, guidance, rating/count, CLIP-skip, resolution, and negative-prompt envelopes.
- [X] P2-040 [P] Implement FLUX.2 generation/edit compilers with structured reference roles,
  variant-specific validation, and no negative-prompt field.
  Evidence: separate `Flux2GenerationProductionMediaCompiler` and
  `Flux2EditProductionMediaCompiler` implementations enforce fixed non-preview endpoints, exact
  dimension/variant limits, ordered reference roles, and recursively reject negative-prompt fields.
- [X] P2-041 [P] Implement separate Qwen generation and Qwen Edit 2511 compilers with ordered
  multi-image preservation/change contracts.
  Evidence: separate Qwen Image 2512 generation and Qwen Image Edit 2511 compilers emit the exact
  pipeline names and native settings, enforce official generation dimensions, and preserve ordered
  edit-image bindings without operation substitution.
- [X] P2-042 Add compiler fixtures/golden request tests proving deterministic output, legal fields,
  explicit ownership, no secret leakage, and fail-fast missing data.
  Evidence: `ProductionMediaCompilerTests` covers six family/operation compilers, deterministic
  canonical JSON/hash output, legal native fields, FLUX negative-field absence at every depth,
  ordered references, family separation, strict settings, ambiguity, qualification, and recursive
  secret rejection. The focused suite passed 7/7 on 2026-09-02.
- [X] P2-043 Add the strict compiler registry plus capability-cell persistence,
  qualification/reporting, and dispatch guard; ambiguity or absence fails.
  Evidence: `ProductionMediaCompilerRegistry` resolves exactly one compiler by persisted id,
  version, and operation; `ProductionMediaCompilationService` requires an enabled qualified profile
  and its exact qualified cell before atomically persisting the immutable request and bindings.
  Compiler plus real-SQLite repository/service tests passed 13/13 on 2026-09-02.
- [ ] P2-044 Freeze and execute composition-first Qwen Edit/FLUX identity matrices against the
  failed angled cells; retain all outputs and qualify only passing cells.
  Partial evidence: the frozen Qwen C1/C2/C3 matrix ran 6/6 on 2026-09-02 against the existing
  `img-qwen-edit-serverless` endpoint. The committed older matrix images were intentionally reused
  as source compositions; the user confirmed that editing worked and the ordered Dean/Becky faces
  were applied. Outputs and request/reference hashes are retained in
  `artifacts/tmp/qwen-composition-identity/20260902-182349/`. Full per-cell gate review and the
  separate FLUX.2 matrix remain pending, so no broader capability qualification is recorded yet.
- [ ] P2-045 Record matrix outcomes per exact identity strategy and capability cell. These outcomes
  qualify or reject reference-only, LoRA-only, or combined use; they do not disable LoRA support.

## I. Durable Workloads And Provider Dispatch

- [X] P2-046 Add workload draft/readiness/submission service with source-version, policy, endpoint,
  grouping, item/output-count, and cost diagnostics.
- [X] P2-047 Add compatible dispatch grouping and separate provider adapters; use Together image
  variations only where supported and do not use Together JSONL Batch without official image support.
- [X] P2-048 Add RunPod queued dispatch with immediate provider-ID persistence and B-102 transport
  contract integration.
- [X] P2-049 Add result reconciliation, transient URL capture, owned storage, stale/late result
  handling, cancellation, and retry-as-new-attempt.
- [X] P2-050 [P] Add crash/restart, duplicate-suppression, late-result, multi-variation, partial-group,
  timeout, and cost/accounting tests.
  Evidence: `ProductionWorkloadService` persists readiness, exact source/endpoint/policy/cost
  snapshots, workload items, and immutable attempts before transport. Separate RunPod Serverless
  and Together Images adapters enforce their persisted protocols; RunPod job IDs are stored before
  polling, Together native variations remain one attempt per output, and JSONL Batch is not used.
  `ProductionReconciliationService` resumes from persisted IDs, captures transient/base64 outputs
  into owned storage, preserves late results without replacing newer retries, and handles timeout,
  cancellation, partial completion, and cost snapshots. Focused workload/adapter tests passed 9/9
  on 2026-09-02.

## J. Asset Manager And Production Studio

- [X] P2-051 Read Razor instructions and full existing Asset/Scene Image Studio context; freeze the
  new shared shell, responsive constraints, keyboard/focus, and context-state contract.
  Evidence: `production-ui-contract.md` records route ownership, the shared workbench shell,
  desktop/tablet/mobile constraints, keyboard and focus rules, exact Asset Manager and Production
  Studio state keys, ancestor/refresh invalidation behavior, legacy cutoff, and repository/service
  gaps for P2-052 through P2-054. The complete existing Asset Studio, Asset Studio View, Scene Image
  Studio, scoped CSS, global imports, Razor rules, and style reference were read on 2026-09-02.
- [ ] P2-052 Build shared Asset Manager browse/filter/preview/picker/provenance/lineage/approval
  surfaces for face, body, and wardrobe assets.
  Partial evidence: `/asset-studio` is now labeled Asset Manager and provides one shared catalog
  search plus type/approval filters, stable selected-asset preview, checksum/source/supersession
  lineage, production provenance inspection, and an explicit no-default approval form for consent,
  license, use scope, content policy, and compatibility metadata. `ISceneAssetService` owns the
  approval boundary; focused service tests passed 12/12 and Razor/web build succeeded on
  2026-09-02. Create Asset now requires a semantic type and supports upload or compiled prompt
  generation with exact model, size, and output count; Create Identity Pack and Create LoRA are
  explicit commands. Typed identity/body/wardrobe version workflows and reusable picker return
  semantics remain before completion.
- [ ] P2-053 Build Production Studio context rail/media pool/canvas-inspector/attempt strip/queue
  workspace with stable Moment/request/attempt switching.
  Partial evidence: `IProductionWorkloadService.LoadSessionAsync` now returns session-scoped durable
  workload snapshots with ordered items and attempts through the application boundary. The
  Production Studio embeds a responsive `ProductionWorkspace` with workload context rail, item
  media pool, stable 16:10 output canvas, exact durable inspector, and fixed-width attempt strip.
  Manual refresh preserves valid workload/item/attempt IDs and clears only invalid descendants.
  Real-SQLite repository tests passed 7/7, workload service tests passed 8/8, the Razor/web build
  succeeded, and touched-file diagnostics were clean on 2026-09-02. Moment/intent/request switching,
  automatic polling, and comparison state remain before completion.
- [ ] P2-054 Add semantic intent editing, reference-role selection, readiness/cost/group preview,
  prepare/submit/cancel/retry/review/approve actions, and exact request inspection.
  Partial evidence: Asset detail supports iterative source editing with an exact editor model and
  one to eight immutable outputs. Durable generation, editing, and identity-pack payloads pin exact
  selected models, and generation compiles semantic text for the selected model family. Production
  readiness/cost/group orchestration and complete exact-request review remain before completion.
- [ ] P2-055 Remove the old one-off generation action from new-session production navigation after
  feature parity; do not retain it as a fallback.
- [ ] P2-056 [P] Add service/component tests, Razor diagnostics, accessibility checks, and Playwright
  desktop/mobile workflow screenshots with no overlap or context loss.
  Partial evidence: focused Asset Manager UI/service contracts passed 17/17. Playwright acceptance
  at 1440 x 900 and 390 x 844 found no horizontal overflow, preserved all three creation commands
  and both Create Asset input paths, kept TogetherAI selectable, visibly labeled the configured
  default, and excluded the Qwen editor-only model from generation choices. Persisted screenshot
  artifacts and broader workflow accessibility coverage remain before completion.

## K. Phase 2 Release Gate

- [ ] P2-057 Run all historical and new qualification cells through the application path and verify
  snapshots against standalone proofs without reclassifying failures.
- [ ] P2-058 Run affected tests, solution build, full suite, Razor diagnostics, provider smoke tests,
  restart recovery, and security/retention checks; record exact current results.
- [ ] P2-059 Record the Phase 2 release decision, qualified/rejected identity-strategy cells,
  residual risks, cost observations, and Phase 3 handoff.

## L. First-Class Synthetic Character LoRA

- [X] P2-060 Add synthetic LoRA dataset/member, training job/attempt, artifact, and identity-strategy
  domain records with strict state and invariant validation.
  Evidence: `CharacterLoraModels.cs` defines explicit dataset/member, training job/attempt,
  artifact, and per-request identity-strategy records, state enums, exact asset-version lineage,
  and deterministic manifest hashing. Repository validation rejects incomplete state rather than
  supplying hidden defaults.
- [X] P2-061 Add repositories and additive SQLite schema for immutable dataset versions, shared
  `SceneAsset` membership, durable training attempts, artifacts, and exact strategy bindings.
  Evidence: `ICharacterLoraRepository` and `CharacterLoraRepository` add self-contained additive
  SQLite schema, draft-only dataset mutation, exact approved-asset freeze validation, optimistic
  training transitions, append-only attempts, artifact lineage/qualification, and qualified exact
  request-cell strategy bindings. `SceneAssetRepository` retains dataset-member assets, and the
  repository is registered in `Program.cs`.
- [X] P2-062 [P] Add real-SQLite tests for version uniqueness, freeze immutability, manifest hashing,
  membership/asset checksum integrity, append-only attempts, artifact lineage, and retention guards.
  Evidence: `CharacterLoraRepositoryTests` covers all listed invariants against clean temporary
  SQLite databases and passed 7/7 on 2026-09-02. The full solution built successfully and the full
  suite passed 1711/1711 before the final two focused cases were added.
- [X] P2-063 Add configured training profiles for exact base family/model/checksum, trainer/version,
  complete recipe, environment requirements, checkpoint/sample cadence, and no-default validation.
  Evidence: `CharacterLoraTrainingProfile`, `ICharacterLoraRepository`, and
  `CharacterLoraRepository` persist versioned draft/qualified profiles and make the selected
  enabled qualified profile the sole source for exact job base, trainer, recipe, environment,
  cadence, version, and immutable snapshot lineage. Invalid, incomplete, or secret-bearing values
  fail explicitly. `LoraTrainingProfiles.razor` exposes every required value in Asset Manager.
  Focused real-SQLite profile and LoRA lifecycle coverage passed 10/10 on 2026-09-02. The full
  solution built successfully and the full suite passed 1716/1716. Live browser checks at
  1440x900 and 390x844 found no horizontal overflow, console errors, page errors, or overlapping
  controls.
- [X] P2-064 Add Asset Manager identity-seed and coverage-batch generation using exact qualified
  production capabilities; persist every candidate's request/attempt/asset provenance.
  Evidence: `CharacterAssetGenerationService` creates one exact CharacterAsset production graph
  per identity-seed or coverage candidate, with candidate-specific semantics, settings, references,
  qualified profile/cell, immutable request, workload, item, and attempt lineage. Reconciliation
  registers successful owned outputs as Draft Scene Assets carrying dataset, character, identity
  pack, request, workload, item, attempt, model, and checksum provenance. The operational
  `LoraDatasetGeneration.razor` page captures the explicit dataset plan, qualified capability,
  candidate plan, provider endpoint, policy, cost, source, and retry snapshots and submits each
  workload without auto-approval. Focused production repository coverage passed 10/10 and the
  character candidate batch test passed on 2026-09-02; the Web build succeeded on 2026-09-03.
  Live browser checks at 1440x900 and 390x844 found no horizontal overflow, console errors, page
  errors, or overlapping controls.
- [X] P2-065 Add Asset Manager curation for coverage, captions, train/validation roles, identity
  drift, duplicate/near-duplicate, anatomy, leakage, permanent traits, approval, and dataset freeze.
  Evidence: `CharacterLoraRepository.CurateDatasetMemberAsync` provides optimistic, draft-only
  curation with exact caption revisions and immutable asset/checksum/generation-attempt/ordinal
  lineage. `LoraDatasetCuration.razor` exposes candidate images, captions, roles, splits, all five
  required finding classes, explicit accept/reject decisions, reviewer identity, production-asset
  inspection, and exact-manifest freeze; every mutation is disabled after freeze. Asset Manager now
  permits generated Draft assets to enter its existing explicit production-approval workflow.
  Real-SQLite LoRA repository tests passed 11/11 and the Web build succeeded on 2026-09-03. The
  curation route's real SQLite not-found state passed 1440x900 and 390x844 browser checks with no
  horizontal overflow, console errors, or page errors.
- [X] P2-066 Add durable training preparation, dispatch, provider-ID persistence, reconciliation,
  logs/samples/checkpoints, retry-as-new-attempt, and artifact registration.
  Evidence: `CharacterLoraTrainingService` prepares jobs exclusively from frozen dataset manifests
  and exact qualified profile snapshots, persists provider request IDs on append-only attempts,
  reconciles queued/running/terminal states, records worker-owned logs/sample/checkpoint manifests,
  retries failures as new attempts, and registers immutable artifact lineage. The concrete
  `RunPodCharacterLoraTrainingDispatchAdapter` submits and polls the persisted endpoint/request
  contract without changing pod infrastructure. `LoraDatasetTraining.razor` exposes explicit
  provider/path/seed/artifact-version preparation and durable recovery actions. Combined LoRA
  lifecycle and transport tests passed 16/16, all RolePlay tests passed 1391/1391, and the Web build
  succeeded on 2026-09-03. The missing-dataset route passed 1440x900 and 390x844 browser checks with
  no horizontal overflow, console errors, or page errors; the live database contained no dataset
  fixture, so frozen-form browser acceptance remains part of P2-069 manual acceptance.
- [X] P2-067 Replace global model identity selection with declared strategy capabilities and explicit
  per-request `ReferenceConditioning`, `Lora`, or qualified `Combined` bindings without fallback.
  Evidence: Model Manager now persists independent `ReferenceConditioning`, `Lora`, and `Combined`
  declarations without modifying legacy POC rendering metadata. Each identity capability profile links
  to one enabled registered model, while exact qualification remains on its selected profile/cell.
  `ProductionMediaCompilationService` validates the linked model identifier, model/profile strategy
  declarations, exact qualified cell, per-actor strategy ownership, and qualified LoRA artifact
  model/version/checksum before persistence; missing capability is an explicit configuration error
  and never substitutes a model or strategy. Compiled request, ordered references, and strategy
  bindings persist in one transaction. Real-SQLite selection, fail-fast, and rollback tests passed
  23/23; all RolePlay tests passed 1398/1398 and the Web build succeeded on 2026-09-03.
  End-to-end workflow completion: Character Identity now links each approved pack directly into
  LoRA dataset generation with character/pack context. Dataset generation uses an approved-pack
  picker, revalidates exact pack ownership/status before creating a dataset, lists qualified
  artifacts for the freely selected exact model/version, and emits explicit typed strategy,
  actor, artifact-checksum, and strength bindings for coverage requests. Candidate artifacts are
  listed in the durable training workflow and require secret-free decision evidence before
  qualification/rejection. Curation links back to the exact source pack. The character-generation
  service independently enforces approved pack ownership before its first production write and
  routes supplied bindings through atomic identity-aware compilation; no strategy fallback exists.
- [ ] P2-068 Qualify LoRA-only and combined inference cells at explicit artifact versions/strengths
  against frozen prompts, seeds, held-out compositions, and leakage/diversity gates.
  Operational validation pending 2026-09-03: the complete application workflow now supports
  creating/approving the identity pack, generating and curating a synthetic dataset, freezing and
  training it, recording evidence-backed artifact decisions, and selecting a qualified artifact in
  an exact LoRA or Combined coverage request. The development database currently has no completed
  artifact to score; that is expected operator-created state and is not an implementation blocker.
  Complete this task by exercising the workflow with a real fictional character and recording the
  frozen output evidence; do not seed or fabricate qualification rows.
- [ ] P2-069 [P] Add domain/repository/service/compiler/Razor/Playwright tests and run solution build,
  full test suite, training-provider smoke test, restart recovery, and manual dataset/qualification
  acceptance before completing the Phase 2 release gate.
  Partial evidence 2026-09-03: approved-pack ownership and identity-aware character generation
  tests passed 5/5; artifact lineage/query/evidence tests passed; the full solution build passed;
  and the complete suite passed 1732/1732. Character Identity, LoRA dataset generation, and LoRA
  training states passed 1440x900 and 390x844 browser checks with no horizontal overflow, console
  errors, or page errors. Training-provider smoke, restart recovery, and manual frozen-dataset/
  qualification acceptance remain operational P2-068/P2-069 execution work.
