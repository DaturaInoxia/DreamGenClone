# B-100 Phase 13 Validation

**Recorded:** 2026-08-31

This record maps Phase 13 acceptance tasks to executable evidence. It does not close the configured-model corpus gates, final full-suite acceptance, legacy-removal audit, or backlog advancement.

## Associated Backlog Items

Backlog items tied to B-100's Phase 13 (Final Validation). This table is the anchor for
session tracking; more B-100-related items are expected to be filed and appended here this
session, and a plan update is pending once those items are added.

| Backlog Item | State | Relationship to Phase 13 |
|---|---|---|
| B-100 | `planned` | Owning item. Phase 13 is its Final Validation; T177 records acceptance and advances this item's state only after T172 closes. |
| B-101 | `designed` | Consumes B-100 production contracts. In-scope Phase 13 tasks: T175 (imports production facts without semantic rediscovery) and T154 (B-101 source-of-truth hierarchy in operator docs). |
| B-103 | `new` | Production Studio Composition stage follow-on. Surfaces the B-100 Composition stage (Phase 9A) exact prompt / render controls / model selector. Not a Phase 13 gate; tracked alongside Phase 13 because it extends the same production surface. Related: B-100, B-097, B-102, B-032. |
| B-104 | `new` | Scene image prompt compiler standards — formal external research + model settings + change governance. Canonical doc: `.github/instructions/scene-image-prompt-compiler-standards.instructions.md`. Research/governance prerequisite for B-103 part B (the `BuildCanonicalSystemPrompt` fix); not a Phase 13 gate. Related: B-100, B-103. |
| B-102 | `debugging` | Explicitly **not** B-100-owned ("does not own B-100 canonical media semantics or B-101 presentation"). Listed for disambiguation only — out of Phase 13 scope. |

Not Phase-13-associated (B-100 image-generation prerequisites/relatives, tracked in their
own streams): B-032, B-097, B-098, B-099.

## Validation Matrix

| Task | Status | Executable evidence | Result |
|---|---|---|---|
| T001 | Proven | `fixtures/corpus.json`, eight frozen case files, and `CorpusRunnerTests.FrozenCorpus_LoadsEightSanitizedCategoriesWithStableChecksum`. | Eight invented, stable, non-explicit cases load under strict JSON contracts; no production RP prose or secrets are present. |
| T002 | Proven | Frozen expectations plus `CorpusRunnerTests.FrozenCorpus_LoadsEightSanitizedCategoriesWithStableChecksum` and `CorpusRunnerTests.MissingNarrative_IsRejectedBeforeExecutionAndExcludedFromValidityDenominator`. | Every valid case has reviewed Beat evidence, 2-4 Moments, recommendation, required roles, and source-fact keys; malformed Narrative is a preflight rejection. |
| T003 | Open | `baseline.md` separates historical diagnostics from the required reproducible baseline and records the attempted run. | The 2026-08-31 run failed before model execution because no `RolePlaySceneBeatAnalyzer` function-default row exists. No baseline metrics were fabricated. |
| T004 | Proven | `DreamGenClone.CorpusRunner`, PowerShell wrapper, and `CorpusRunnerTests`. | Standalone .NET 9 runner uses exact configured production stages, read-only live configuration, isolated temp stage persistence, sanitized JSON/Markdown reporting, case/stage/iteration selectors, output-size metrics, fixed gates, and deterministic nearest-rank percentiles. Focused tests passed 10/10. |
| T170 | Proven | Focused B-100/B-032/RolePlay/Processing test command listed below. | Affected-area run passed: 1,335/1,335. |
| T171 | Proven | Full `DreamGenClone.sln` build and full `DreamGenClone.Tests` suite commands listed below. | Fresh post-runner full suite passed: 1,632/1,632. Solution build passed with the known AngleSharp NU1902 warning. |
| T172 | Open | Actual configured-model corpus validity and latency gates for Catalogue, Beat Production, Moment Discovery, and Moment Enrichment. | The 2026-08-31 full eight-case command produced `configuration_resolution_failed` before model execution because the required function-default row is absent. Zero model calls were made; deterministic fixtures are not a substitute. |
| T173 | Proven | `SceneBeatPipelineEndToEndTests.EnqueueAndHandle_AllFourStagesThenCompileStill_PersistsExactCurrentLineage` | One SQLite database; real enqueue services, exact durable handlers, strict parsers, repositories, current-record reloads, full ID/version lineage, and persisted StillImage brief. 1/1 passed. |
| T174 | Proven | `MultimodalMediaCompilerTests.RepresentativeCanonicalLineage_CompilesAllSevenMediaKindsWithoutMutationOrRawProse`; `EveryVideoCoverageKind_CompilesForVideoAndNativeAudioVideo` | Seven media kinds plus all five `SceneVideoCoverageKind` values for `Video` and `VideoWithAudio`. Compiler class run: 16/16 passed. |
| T175 | Proven | `StoryPresentationImportServiceTests` | Explicit ordered plan IDs; exact current Catalogue/Plan/Moment Set/Enrichment facts; deterministic canonical JSON and SHA-256; typed missing/stale results; no model/session/interaction/prompt/provider dependency. 4/4 passed. |
| T176 | Proven | Repository, stale-descendant, and durable-job tests listed below. | Focused matrix: 52/52 passed. |
| T177 | Open | Final acceptance record and backlog transition. | Intentionally not advanced; final suite/build and T172 remain outside this closure. |
| T178 | Proven | `MultimodalMediaCompilerTests.RepresentativeCanonicalLineage_PreservesCrossModalSemanticInvariantsInStructuredProjections` plus immutable-source fixture | Structured JSON assertions cover identity, wardrobe/state, action, dialogue, speaker, timing, camera/motion, ambience, effects, and music without source mutation. |

## T176 Evidence

Cancellation compare-and-set:

- `SceneBeatCatalogueRepositoryTests.GetNextVersion_IncludesTerminalAndSupersededHistory`
- `SceneMomentEnrichmentRepositoryTests.ReverseOrderCompletion_SupersededAttemptCannotOverwriteCurrentEnrichment_AndCancellationUsesCas`
- `DurableBackgroundJobRepositoryTests.Cancellation_PreventsStaleWorkerCompletion`

Reverse completion cannot overwrite current:

- `SceneBeatCatalogueRepositoryTests.ReverseOrderCompletion_SupersededOlderAttemptCannotOverwriteNewerVersion`
- `SceneBeatProductionPlanRepositoryTests.ReverseOrderCompletion_OlderSupersededAttemptCannotOverwriteCurrentPlan`
- `SceneMomentSetRepositoryTests.ReverseOrderCompletion_SupersededAttemptCannotPromoteMoments`
- `SceneMomentEnrichmentRepositoryTests.ReverseOrderCompletion_SupersededAttemptCannotOverwriteCurrentEnrichment_AndCancellationUsesCas`

Stale descendants are rejected:

- `SceneBeatProductionPipelineServiceTests.Enqueue_RejectsActiveDuplicateAndSupersededCatalogue`
- `SceneMomentDiscoveryPipelineServiceTests.Enqueue_RequiresExactCurrentCompletedPlanAndRejectsActiveDuplicate`
- `SceneMomentEnrichmentPipelineServiceTests.EnqueueRecommended_UsesPersistedRecommendationAndRejectsStaleParentPlan`
- `SceneImageProductionGroupRepositoryTests.Create_SupersededEnrichment_RejectsStaleLineage`
- `MultimodalMediaCompilerTests.Service_RejectsStalePlanBeforeCreatingBrief`
- `MultimodalMediaCompilerTests.Service_RejectsMismatchedMomentSetBeforeCreatingBrief`
- `MultimodalMediaCompilerTests.Service_RejectsMismatchedEnrichmentBeforeCreatingBrief`

Restart and lease recovery:

- `DurableBackgroundJobQueueTests.StartupRecovery_RecoversExpiredProcessingLeaseAtCurrentUtc`
- `DurableBackgroundJobRepositoryTests.Recovery_RequeuesOnlyExpiredLeasesAndPreservesAttemptCount`
- `DurableBackgroundJobRepositoryTests.ExpiredLeaseOwner_CannotComplete`

## Commands

Frozen-corpus runner tests without model calls:

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore --filter "FullyQualifiedName~CorpusRunnerTests"
```

Configured-model benchmark:

```powershell
.\helpers\run-b100-corpus-benchmark.ps1 -Iterations 1
```

Focused import:

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~StoryPresentationImportServiceTests"
```

Focused end to end:

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore --filter "FullyQualifiedName~SceneBeatPipelineEndToEndTests"
```

Focused compiler and concurrency evidence:

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore --filter "FullyQualifiedName~MultimodalMediaCompilerTests|FullyQualifiedName~SceneMomentEnrichmentRepositoryTests"
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore --filter "FullyQualifiedName~SceneBeatCatalogueRepositoryTests|FullyQualifiedName~SceneBeatProductionPlanRepositoryTests|FullyQualifiedName~SceneMomentSetRepositoryTests|FullyQualifiedName~SceneMomentEnrichmentRepositoryTests|FullyQualifiedName~SceneBeatProductionPipelineServiceTests|FullyQualifiedName~SceneMomentDiscoveryPipelineServiceTests|FullyQualifiedName~SceneMomentEnrichmentPipelineServiceTests|FullyQualifiedName~SceneImageProductionGroupRepositoryTests|FullyQualifiedName~MultimodalMediaCompilerTests|FullyQualifiedName~DurableBackgroundJobQueueTests|FullyQualifiedName~DurableBackgroundJobRepositoryTests"
```

Final affected areas, suite, and build:

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore --filter "FullyQualifiedName~RolePlay|FullyQualifiedName~Processing"
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore
dotnet build DreamGenClone.sln --no-restore
```

Observed local results:

- Corpus runner tests: 10 passed, 0 failed, 0 skipped. The file-level VS Code adapter found no tests; the documented focused `dotnet test` command performed the successful run.
- Post-runner full test suite: 1,632 passed, 0 failed, 0 skipped.
- PowerShell wrapper: parsed successfully under Windows PowerShell 5.1 without executing the runner.
- Configured-model calls during T001/T002/T004 implementation: none.
- Configured-model benchmark attempted after implementation: `helpers/run-b100-corpus-benchmark.ps1 -Iterations 1 -Output artifacts/tmp/b100-corpus/b100-corpus-report-20260831.json` failed before any model request with `configuration_resolution_failed`; sanitized JSON and Markdown reports exist at that path and its `.md` companion.
- Read-only DbQuery diagnosis: no `FunctionModelDefaults` row exists for `RolePlaySceneBeatAnalyzer`; no encrypted credential was selected or printed.
- Affected RolePlay/Processing run: 1,335 passed, 0 failed, 0 skipped.
- Full test suite: 1,632 passed, 0 failed, 0 skipped in 212.1 seconds. The VS Code adapter found zero tests when given the project directory; the documented `dotnet test` command performed the successful full run.
- Full solution build: succeeded in 2.6 seconds with one known `AngleSharp` NU1902 warning.
- Changed-file editor diagnostics: no errors.
- `git diff --check`: passed with no output.

## Open Blockers

- T003 remains open until an explicit `RolePlaySceneBeatAnalyzer` configuration is saved in Model Manager and a genuine frozen-corpus baseline report is produced.
- T172 remains open until that configured model runs the corpus and passes validity and latency gates for all four analysis stages.
- T153 remains separately deferred until the canonical Still brief drives Composition prompt compilation and render execution without the schema-v3 one-shot path. It does not prevent B-100 acceptance under the spec's separate migration criterion, but it remains unchecked.
- T177 remains open because final acceptance and backlog advancement require T172.