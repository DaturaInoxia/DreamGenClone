# B-100 Implementation Plan

## Summary

Deliver the progressive pipeline in vertical slices while keeping the existing schema-v3 flow usable until cutover. Reliability primitives land before the new LLM path so the feature never introduces another stale-write or restart-loss window.

## Architectural Boundaries

| Concern | Owner |
|---|---|
| Catalogue/enrichment records and neutral visual contract | `DreamGenClone.Domain/RolePlay` |
| Repository and queue abstractions | `DreamGenClone.Application` |
| SQLite repositories and structured completion transport | `DreamGenClone.Infrastructure` |
| Model resolution, handlers, orchestration, compiler registry, UI | `DreamGenClone.Web` |
| Contract, concurrency, migration, and UI tests | `DreamGenClone.Tests/RolePlay` |

The feature does not modify RP continuation, narrative gates, phase transitions, or prompt slots.

## Phase 0 - Freeze Corpus and Baseline

1. Build a sanitized acceptance corpus of representative authoritative turns: solo, ensemble, parallel viewpoints, remote observer, location transition, clothing transition, long explicit turn, and malformed/missing Narrative cases.
2. Record current one-shot latency, response validity, beat count, and semantic acceptance.
3. Define human-reviewed expected catalogue boundaries and evidence mappings.
4. Add a repeatable benchmark command that reports p50/p95 and validity separately for catalogue and enrichment.

**Exit:** corpus and baseline are committed without session secrets or raw production-only data.

## Phase 1 - Durable Job and Concurrency Foundation

1. Add `DurableBackgroundJob` domain/application contracts and SQLite repository.
2. Implement transactional enqueue, lane claim, lease renewal/expiry, terminal transitions, cancellation, and startup recovery.
3. Add required UI-backed lane concurrency and retry policy fields to canonical configuration.
4. Introduce a `TextAnalysis` lane without migrating every existing generic job immediately.
5. Add compare-and-set repository APIs for catalogue/enrichment ownership.
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

## Phase 2 - Analyzer Configuration and Structured Transport

1. Add `AppFunction.RolePlaySceneBeatAnalyzer`.
2. Add explicit registered-model structured-output, context, and output-limit capabilities.
3. Add Model Manager fields for analyzer assignment, thinking mode, limits, concurrency, retry policy, and diagnostics retention.
4. Implement a dedicated resolver that does not accept `RolePlaySession.SessionModelId`.
5. Generalize or add a text structured-completion client that sends strict JSON Schema for supported configured providers.
6. Validate capability compatibility before job acceptance.
7. Preserve exact resolved configuration in execution snapshots.

**Important:** no hardcoded model, retry values, thinking mode, or hidden provider inference. Missing values fail with UI guidance.

**Exit:** a contract test proves provider request JSON includes the exact schema, and incompatible models fail before enqueue.

## Phase 3 - Beat Catalogue Vertical Slice

1. Add catalogue, entry, and attempt domain models.
2. Add additive SQLite schema and repository operations.
3. Implement immutable source snapshot creation from `RolePlayV2Turn`, Narrative, supporting interactions, and relevant character identities.
4. Assign compact evidence/profile keys in application code.
5. Implement a short catalogue prompt and versioned JSON Schema.
6. Parse schema-valid output and resolve compact keys to authoritative IDs.
7. Add `SceneBeatCatalogueJobHandler` with durable execution and compare-and-set promotion.
8. Add catalogue query/cancel/replace service methods.
9. Adapt Studio to display catalogue states and compact selectable cards.
10. Keep the legacy schema-v3 display path behind an explicit compatibility branch during migration.

**Primary replacements/additions near:**

- `SceneImageBeatAnalysisService.cs` becomes legacy-only or delegates to new catalogue/enrichment services; do not grow it into both stages.
- `SceneImageBeatGenerationJobHandler.cs` remains legacy during transition, then is retired.
- `SceneImageService.cs` delegates progressive operations to a dedicated scene-beat pipeline service.
- `SceneImageStudio.razor` receives the progressive UI in micro-edits under Razor rules.

**Exit:** Generate Beats produces only compact entries and meets the catalogue corpus validity gate.

## Phase 4 - Selected-Beat Enrichment Vertical Slice

1. Add enrichment records, attempts, repository methods, and current-revision rules.
2. Define the versioned neutral visual contract and strict JSON Schema.
3. Build enrichment input from the selected compact entry, authoritative Narrative, cited evidence, and relevant character profile snapshots.
4. Implement durable enrichment handler and compare-and-set promotion.
5. On Studio selection, enqueue only when no completed current enrichment exists.
6. Show per-selected-beat progress/error and disable prompt generation until complete.
7. Reuse current enrichment without another model call.
8. Update prompt enqueue to snapshot the complete enrichment and reject stale catalogue versions.

**Exit:** selecting one of several entries enriches exactly one; switching back reuses it; replacing the catalogue invalidates all older enrichments for new prompts.

## Phase 5 - Image-Family Compiler Registry

1. Add explicit image model family and prompt dialect metadata to registered image models.
2. Seed/migrate existing known Pony and SDXL/Juggernaut models explicitly; require review for unknown rows.
3. Introduce `ISceneImagePromptCompiler` registration and exact-match resolver.
4. Adapt existing Pony and SDXL builders behind compiler strategies.
5. Remove checkpoint-name classification from the active execution path only after every configured image model has explicit metadata.
6. Prove the same enriched beat compiles through both existing families without changing persisted enrichment.

**Exit:** adding a family requires a compiler registration, workflow/client support, UI metadata, and tests, but no catalogue/enrichment schema change.

## Phase 6 - Migration and Cleanup

1. Run dual-read, new-write migration: existing complete schema-v3 analyses stay viewable; new Generate Beats writes catalogues.
2. Gate new prompt creation on current enrichment after rollout activation.
3. Preserve old prompt/image provenance and do not rewrite legacy records.
4. Remove the legacy one-shot enqueue command and handler after the compatibility window and data/reference audit.
5. Remove obsolete filename classifier use only when no call sites remain.
6. Add diagnostics retention/pruning for raw model responses and reasoning.
7. Update B-032 handoff and phase documentation with the new source-of-truth hierarchy.

## Validation Strategy

### Unit and contract tests

- Catalogue and enrichment schema parsing.
- Evidence/profile key resolution.
- Semantic invariants and bounded field lengths.
- Model capability and function-default resolution.
- Structured provider request body.
- Image compiler exact-match resolution.

### Persistence and concurrency tests

- Reverse-order completion.
- Replace while processing.
- Duplicate selection/enqueue.
- Compare-and-set affects zero rows for stale attempts.
- Lease expiry and startup recovery.
- Cancellation before call, during call, and after response.
- Transient retry versus permanent failure.

### Component/service tests

- Catalogue-only generation.
- Lazy selected-beat enrichment.
- Completed enrichment reuse.
- Stale enrichment rejection during prompt enqueue.
- Legacy display compatibility.
- Pending/failed/cancelled state presentation.

### Full validation

1. Focused B-100 tests.
2. Full `SceneImage` tests.
3. Full RolePlay tests.
4. Full solution build.
5. Full test suite.
6. Frozen corpus benchmark and semantic review.
7. Fresh Studio end-to-end run through catalogue, selection, enrichment, Pony compile/render, and SDXL/Juggernaut compile/render.

## Rollout Gates

| Gate | Requirement |
|---|---|
| G1 Reliability | Durable queue and stale-write tests pass before model integration. |
| G2 Contract | >=99% schema-valid output on frozen corpus. |
| G3 Performance | Catalogue and enrichment p50/p95 targets met. |
| G4 Semantic | Human-reviewed catalogue boundaries and enriched facts meet acceptance matrix. |
| G5 Migration | Existing prompt/image records remain readable and reproducible. |
| G6 Configuration | Exactly one analyzer source and one compiler source; no fallbacks. |

## Blast Radius

Large but contained to scene-image/model-management/background-processing surfaces. The highest-risk areas are shared job infrastructure, Model Manager persistence, SQLite migration, and Scene Image Studio state transitions. RP continuation behavior is outside scope.

## Recommended Delivery Shape

Use small pull requests in phase order. Do not combine durable queue infrastructure, the new model protocol, Razor redesign, and legacy cleanup into one change. Each phase has an independently executable acceptance check and leaves the current user path operational until the progressive replacement reaches its cutover gate.
