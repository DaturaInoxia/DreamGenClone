# B-100 Implementation Plan

## Summary

First prove the canonical production ontology against real model inputs and golden cross-modal requests. Then deliver the progressive pipeline in vertical slices while keeping the existing schema-v3 flow usable until cutover. Reliability primitives still land before the new LLM execution path so the feature never introduces another stale-write or restart-loss window.

## Architectural Boundaries

| Concern | Owner |
|---|---|
| Catalogue/moment/enrichment records and neutral visual contract | `DreamGenClone.Domain/RolePlay` |
| Repository and queue abstractions | `DreamGenClone.Application` |
| SQLite repositories and structured completion transport | `DreamGenClone.Infrastructure` |
| Model resolution, handlers, orchestration, compiler registry, UI | `DreamGenClone.Web` |
| Contract, concurrency, migration, and UI tests | `DreamGenClone.Tests/RolePlay` |

The feature does not modify RP continuation, narrative gates, phase transitions, or prompt slots.

## Phase 0 - Provider Research and Evidence Matrix

1. Record representative official input contracts for still image, TTS, sound effects, ambience, music, generated video, native-video audio, and lip-sync/performance.
2. Classify each field as canonical semantic data, compiler/profile configuration, realized derivative metadata, or unsupported capability.
3. Record required/optional/unsupported status, source URL, verification date, and confidence per representative model/version.
4. Reject canonical fields with no semantic consumer and reject generic prose containers where documented consumers require typed timing, identity, reference, or ownership data.

**Exit:** the provider evidence matrix covers every planned projection and clearly separates canonical meaning from provider syntax.

## Phase 1 - Canonical Ontology and Consistency Invariants

1. Define shared Beat-relative time, typed windows, stable subject/location/prop identities, dual dialogue text, performance intent, visual state sequences, typed references, audio ownership, music sections, and realized alignment.
2. Define required cross-modal invariants for identity, appearance, wardrobe, location, props, frozen state, action order, dialogue, speaker, emotion, timing, camera, ambience, effects, and music.
3. Define capability validation: unsupported required intent fails; omitted optional intent is reported.
4. Review the ontology against existing Pony and SDXL builders and every representative provider row.

**Exit:** each canonical field has evidence, ownership, validation rules, and at least one compiler consumer.

## Phase 2 - Golden Compiler Fixtures

1. Author one immutable representative Beat/Moment lineage using the proposed ontology.
2. Define expected Pony, SDXL/Juggernaut, FLUX-like, TTS, ambience/effect, music, video coverage, native-audio video, and lip-sync/performance request snapshots.
3. Add semantic normalization and cross-modal assertion rules that ignore provider syntax while detecting contradictions.
4. Revise the ontology until every request compiles without RP-text rereading or semantic invention.

**Exit:** all representative requests compile from one lineage and pass consistency assertions.

## Phase 3 - Freeze Corpus and Baseline

1. Build a sanitized acceptance corpus of representative authoritative turns: solo, ensemble, parallel viewpoints, remote observer, location transition, clothing transition, long explicit turn, and malformed/missing Narrative cases.
2. Record current one-shot latency, response validity, beat count, and semantic acceptance.
3. Define human-reviewed expected beat boundaries, moment candidates, recommended moments, and evidence mappings.
4. Add a repeatable benchmark command that reports p50/p95 and validity separately for catalogue, moment discovery, and enrichment.

**Exit:** corpus and baseline are committed without session secrets or raw production-only data.

## Phase 4 - Durable Job and Concurrency Foundation

1. Add `DurableBackgroundJob` domain/application contracts and SQLite repository.
2. Implement transactional enqueue, lane claim, lease renewal/expiry, terminal transitions, cancellation, and startup recovery.
3. Add required UI-backed lane concurrency and retry policy fields to canonical configuration.
4. Introduce a `TextAnalysis` lane without migrating every existing generic job immediately.
5. Add compare-and-set repository APIs for catalogue, moment-set, and enrichment ownership.
6. Test reverse completion order, duplicate request, cancellation, shutdown recovery, expired lease, and retry classification.

**Likely files:**

- `DreamGenClone.Domain/Processing/` new durable job model
- `DreamGenClone.Application/Processing/` queue/repository contracts
- `DreamGenClone.Infrastructure/Processing/` SQLite implementation
- `DreamGenClone.Web/Application/BackgroundJobs/` durable worker and handler adapter
- `DreamGenClone.Web/Program.cs`
- Model Manager/configuration UI and persistence surfaces selected during implementation discovery
- `DreamGenClone.Tests/RolePlay/` and/or `Processing/`

**Exit:** a synthetic long-running handler cannot overwrite superseding state, survives restart, and does not block a separate render lane.

## Phase 5 - Analyzer Configuration and Structured Transport

1. Add `AppFunction.RolePlaySceneBeatAnalyzer`.
2. Add explicit registered-model structured-output, context, and output-limit capabilities.
3. Add Model Manager fields for analyzer assignment, thinking mode, limits, concurrency, retry policy, and diagnostics retention.
4. Implement a dedicated resolver that does not accept `RolePlaySession.SessionModelId`.
5. Generalize or add a text structured-completion client that sends strict JSON Schema for supported configured providers.
6. Validate capability compatibility before job acceptance.
7. Preserve exact resolved configuration in execution snapshots.

**Important:** no hardcoded model, retry values, thinking mode, or hidden provider inference. Missing values fail with UI guidance.

**Exit:** a contract test proves provider request JSON includes the exact schema, and incompatible models fail before enqueue.

## Phase 6 - Beat Catalogue Vertical Slice

1. Add catalogue, entry, and attempt domain models.
2. Add additive SQLite schema and repository operations.
3. Implement immutable source snapshot creation from `RolePlayV2Turn`, Narrative, supporting interactions, and relevant character identities.
4. Assign compact evidence/profile keys in application code.
5. Implement a short catalogue prompt and versioned JSON Schema.
6. Parse schema-valid output and resolve compact keys to authoritative IDs.
7. Add `SceneBeatCatalogueJobHandler` with durable execution and compare-and-set promotion.
8. Add catalogue query/cancel/replace service methods.
9. Adapt Studio to display catalogue states and compact selectable beat cards. A beat synopsis may describe progression; it must not masquerade as a frozen image.
10. Keep the legacy schema-v3 display path behind an explicit compatibility branch during migration.

**Primary replacements/additions near:**

- `SceneImageBeatAnalysisService.cs` becomes legacy-only or delegates to the new catalogue, moment-discovery, and enrichment services; do not grow it across all three stages.
- `SceneImageBeatGenerationJobHandler.cs` remains legacy during transition, then is retired.
- `SceneImageService.cs` delegates progressive operations to a dedicated scene-beat pipeline service.
- `SceneImageStudio.razor` receives the progressive UI in micro-edits under Razor rules.

**Exit:** Generate Beats produces only compact entries and meets the catalogue corpus validity gate.

## Phase 9A - Production Studio and Attempt Backbone

1. Add B-032-owned production-group, attempt-stage/disposition, and append-only approval contracts.
2. Add additive persistence and exact B-100 lineage from a group to one Moment enrichment and POV.
3. Adapt existing generation into `Composition` attempts without changing provider behavior.
4. Add guarded shortlist, reject, approve, supersede, archive, and byte-purge operations.
5. Add explicit approved-frame-to-Scene-Asset promotion with safe file-reference accounting.
6. Replace the all-at-once POC controls with the Production Studio shell described in
	`production-studio-image-workflow.md`; preserve legacy records in a separate read-only section.

**Exit:** one selected Moment owns a branchable attempt group, one exact image can be approved, and
eligible rejected bytes can be removed without deleting provenance or protected lineage.

Identity-reference editing is the next B-032 slice. The current identity-conditioned text-to-image
path is not substituted for identity-after-composition.

## Phase 7 - Selected-Beat Multimodal Production Vertical Slice

1. Add versioned `SceneBeatProductionPlan`, timeline, dialogue, sound, music, typed-reference, video-coverage, and attempt records.
2. Define the strict provider-neutral Beat-production JSON Schema.
3. Snapshot the selected Beat, authoritative Turn/interactions, characters, location, and cited evidence.
4. Resolve exact dialogue/narration spans and speaker/addressee keys in application code.
5. Validate ordered events/windows, dual dialogue text, performance, ambience, sound events, music sections, action arc, typed references, start/end continuity, video coverage, key-state requirements, and audio ownership.
6. Implement durable `SceneBeatProductionPlanJobHandler` and compare-and-set promotion.
7. Show Beat production data and review-required attribution/continuity issues in Studio.
8. Expose the complete current plan through one read contract for image, audio, video, and B-101 consumers.

**Exit:** one selected Beat has source-grounded, provider-neutral dialogue, narration, ambience, effects, action, state, and video metadata sufficient to drive Moment planning and downstream media briefs.

## Phase 8 - Moment Discovery and Key-State Planning Vertical Slice

1. Add versioned Moment Set, Moment, production-role, and attempt records.
2. Define a compact schema returning 2–4 candidates, one recommended still, and all key states mandated by Beat video/audio plans.
3. Build input from the current Beat Production Plan and authoritative evidence.
4. Require one frozen state per Moment with temporal anchor, visible action, participant summary, composition rationale, and production roles.
5. Implement durable `SceneBeatMomentDiscoveryJobHandler` and compare-and-set promotion.
6. Render choices and production roles; allow additional explicit Moment generation when a video plan requires it.
7. Reuse current completed sets and reject stale Beat-plan versions.

**Exit:** selected Beat production requirements resolve to ordered frozen Moments suitable for stills, sound anchors, and video key states.

## Phase 9 - Selected-Moment Enrichment Vertical Slice

1. Add moment-enrichment records, attempts, repository methods, and current-revision rules.
2. Define the versioned provider-neutral frozen-state contract and strict JSON Schema.
3. Build enrichment input from the selected Moment, parent Beat Production Plan, authoritative evidence, and character/location state.
4. Implement durable `SceneMomentEnrichmentJobHandler` and compare-and-set promotion.
5. On moment selection, enqueue only when no completed current enrichment exists.
6. Show per-selected-Moment progress/error and disable dependent media generation until complete.
7. Reuse a current enrichment without another model call.
8. Implement **Generate from suggested moment** by selecting the persisted recommendation and using the same enrichment path.
9. Persist visual constraints, instantaneous sound anchors, and video key-state constraints with complete lineage.

**Exit:** selected Moments provide exact frozen-state metadata for image and video compilers; stale descendants are rejected.

## Phase 10 - Multimodal Compiler Contracts

1. Implement the provider-neutral compiler input projections proven by Phase 2 for still image, speech, ambience/effects, music, video, video-with-audio, and lip-sync/performance.
2. Require all projections to consume current Beat/Moment production records, never raw RP semantic reinterpretation.
3. Define explicit media kind/family/capability/compiler metadata in Model Manager.
4. Define exact-match compiler resolution and fail-fast capability validation.
5. Define full derivative lineage and immutable semantic-brief snapshots.
6. Persist required-intent coverage reports and fail before enqueue when a target cannot honor required canonical intent.
7. Re-run the golden fixtures against executable compiler contracts and immutable request snapshots.

**Exit:** independent generator epics can consume complete semantic inputs without changing B-100 or adding another analysis model.

## Phase 11 - Existing Image-Family Compiler Registry

1. Add explicit image model family and prompt dialect metadata to registered image models.
2. Seed/migrate existing known Pony and SDXL/Juggernaut models explicitly; require review for unknown rows.
3. Introduce `ISceneImagePromptCompiler` registration and exact-match resolver.
4. Adapt existing Pony and SDXL builders behind compiler strategies.
5. Remove checkpoint-name classification from the active execution path only after every configured image model has explicit metadata.
6. Prove the same enriched Moment compiles through both existing families without changing persisted production data.

**Exit:** adding a family requires a compiler registration, workflow/client support, UI metadata, and tests, but no catalogue/enrichment schema change.

## Phase 12 - Migration and Cleanup

1. Run dual-read, new-write migration: existing complete schema-v3 analyses stay viewable; new Generate Beats writes catalogues.
2. Gate new image creation on current Beat/Moment production lineage after rollout activation.
3. Preserve old prompt/image provenance and do not rewrite legacy records.
4. Remove the legacy one-shot enqueue command and handler after the compatibility window and data/reference audit.
5. Remove obsolete filename classifier use only when no call sites remain.
6. Add diagnostics retention/pruning for raw model responses and reasoning.
7. Update B-032 and B-101 handoffs with the multimodal source-of-truth hierarchy.

## Phase 13 - Final Validation

### Unit and contract tests

- Catalogue, Beat-production, Moment-discovery, and Moment-enrichment schema parsing.
- Exact dialogue/narration span and speaker attribution validation.
- Ambience, sound-event, action-arc, continuity, and video-coverage validation.
- Beat progression versus frozen-moment invariants.
- Recommended-moment uniqueness.
- Evidence/profile key resolution.
- Semantic invariants and bounded field lengths.
- Model capability and function-default resolution.
- Structured provider request body.
- Image compiler exact-match resolution.

### Persistence and concurrency tests

- Reverse-order completion.
- Replace while processing.
- Duplicate beat/moment selection and enqueue.
- Compare-and-set affects zero rows for stale attempts.
- Lease expiry and startup recovery.
- Cancellation before call, during call, and after response.
- Transient retry versus permanent failure.

### Component/service tests

- Catalogue-only generation.
- Lazy selected-beat moment discovery.
- Lazy selected-Beat production enrichment.
- Lazy selected-moment enrichment.
- Suggested-moment fast path uses the persisted selected moment.
- Completed enrichment reuse.
- Stale Beat-plan, Moment-set, and enrichment rejection during media-brief creation.
- Image/audio/video compiler fixtures consume no raw RP prose.
- Golden request snapshots preserve cross-modal identity, state, action, dialogue, timing, performance, camera, ambience, effects, and music invariants.
- Lip-sync uses approved realized speech alignment and exact visual/audio windows.
- Unsupported required intent fails compatibility validation before enqueue.
- Legacy display compatibility.
- Pending/failed/cancelled state presentation.

### Full validation

1. Focused B-100 tests.
2. Full `SceneImage` tests.
3. Full RolePlay tests.
4. Full solution build.
5. Full test suite.
6. Frozen corpus benchmark and semantic review.
7. Fresh Studio run through Catalogue, Beat production, Moment planning/enrichment, existing image compile/render, and audio/video contract fixture compilation.

## Rollout Gates

| Gate | Requirement |
|---|---|
| G1 Evidence | Every canonical field has documented ownership, representative consumers, and current source evidence. |
| G2 Consistency | Golden requests for every modality compile from one lineage and pass cross-modal assertions. |
| G3 Reliability | Durable queue and stale-write tests pass before model integration. |
| G4 Contract | >=99% schema-valid output for Catalogue, Beat production, Moment discovery, and enrichment. |
| G5 Performance | All four analysis-stage p50/p95 targets met. |
| G6 Semantic | Beat events/dialogue/audio/video and frozen Moment facts meet the reviewed acceptance matrix. |
| G7 Generator readiness | Image, speech, sound, music, video, native-audio video, and lip-sync fixtures compile entirely from production records. |
| G8 Migration | Existing prompt/image records remain readable and reproducible. |
| G9 Configuration | Exactly one analyzer source and one compiler source per request; no fallbacks. |

## Blast Radius

Large but contained to scene-image/model-management/background-processing surfaces. The highest-risk areas are shared job infrastructure, Model Manager persistence, SQLite migration, and Scene Image Studio state transitions. RP continuation behavior is outside scope.

## Recommended Delivery Shape

Use small pull requests in phase order. Do not combine durable queue infrastructure, the new model protocol, Razor redesign, and legacy cleanup into one change. Each phase has an independently executable acceptance check and leaves the current user path operational until the progressive replacement reaches its cutover gate.
