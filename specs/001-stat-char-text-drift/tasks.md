# Tasks: Stat-Driven Character Instruction Text & Encounter Dimension Drift

**Branch**: `001-stat-char-text-drift` | **Date**: 2026-05-30 | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)
**Backlog**: B-043

---

## Phase 1: Setup

**Goal**: Confirm the branch and build baseline before any changes.

**Independent test criteria**: `dotnet build DreamGenClone.sln -v minimal` produces zero errors.

- [X] T001 Verify branch 001-stat-char-text-drift is checked out and solution builds clean with `dotnet build DreamGenClone.sln -v minimal`

---

## Phase 2: User Story 3 — Stat Reduction [US3] (P1)

**Goal**: Remove Tension and Connection from every layer so the codebase reflects the 5-stat design. This phase is a prerequisite for all other phases.

**Independent test criteria**: After this phase, `dotnet build` produces zero errors. `grep -r '"Tension"\|"Connection"' --include="*.cs"` returns zero matches in non-spec, non-test source files. All existing tests pass.

- [X] T002 [US3] Remove Tension and Connection entries from CanonicalStats array in `DreamGenClone.Application/StoryAnalysis/AdaptiveStatCatalog.cs`
- [X] T003 [US3] Remove Tension and Connection properties; add nullable RuntimeEncounterStats dictionary in `DreamGenClone.Domain/RolePlay/CharacterStatProfileV2.cs`
- [X] T004 [US3] Remove AverageTension and AverageConnection from ScenarioGuidanceInput record; add CharacterRuntimeStats parameter; add CharacterStatStateTexts to ScenarioGuidanceContext record in `DreamGenClone.Application/StoryAnalysis/Models/ScenarioEngineContracts.cs`
- [X] T005 [US3] Remove AverageTension and AverageConnection argument lines from ScenarioGuidanceInput construction; pass CharacterRuntimeStats: null; fix all compiler errors in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`
- [X] T006 [US3] Simplify cheating pressure formula to three terms (Loyalty - Desire/2 + Restraint/2); remove averageTension parameter from method signature and all callers in `DreamGenClone.Infrastructure/RolePlay/ScenarioGuidanceGenerator.cs`
- [X] T007 [US3] Remove Tension and Connection keyword category entries and any hardcoded Tension/Connection average variable computations in `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs`
- [X] T008 [US3] Remove local tension and connection average variable computations (lines ~2087–2104) in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T009 [US3] Run `dotnet build DreamGenClone.sln -v minimal` and `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj`; fix all remaining compile errors and update any test referencing Tension/Connection stat fields

---

## Phase 3: User Story 1 — Prude Wife Transforms Into Highly Sexual Character [US1] (P1)

**Goal**: Implement the full Wife transformation arc: stat text catalog, encounter dimension drift, runtime frame generation, and synthesized stat state text injection.

**Independent test criteria**: Given a Wife character snapshot with Desire=80 and Restraint=20 (both outside neutral band), the continuation prompt contains (1) a behavioral frame reflecting drifted Exhibitionism/DiscoveryCaution tier text and (2) a `HARD CONSTRAINT — enforce in this response: {label} current state:` line synthesizing Desire=80 and Restraint=20 texts. Session save/load preserves drifted RuntimeEncounterStats so the first continuation of a resumed session uses drifted values, not the original profile.

- [X] T010 [P] [US1] Create CharacterStatTextCatalog static class with 15 entries (5 stats × 3 roles, 4 bands each), ResolveText(), and IsNeutralBand() in `DreamGenClone.Domain/StoryAnalysis/CharacterStatTextCatalog.cs`
- [X] T011 [P] [US1] Create StatToDimensionMappings static class with all 8 Wife rules and 6 Husband rules, GetRules(), and ApplyDelta() in `DreamGenClone.Domain/StoryAnalysis/StatToDimensionMappings.cs`
- [X] T012 [P] [US1] Create unit tests covering band boundary resolution, all 15 catalog combinations, IsNeutralBand boundaries, and case-insensitive lookup in `DreamGenClone.Tests/RolePlay/StatTextBandResolutionTests.cs`
- [X] T013 [P] [US1] Create unit tests covering Wife drift rules (Desire +10, Restraint +10), clamp behavior at 0 and 100, zero delta no-op, and OtherMan empty rules in `DreamGenClone.Tests/RolePlay/EncounterDimensionDriftTests.cs`
- [X] T014 [US1] Add RuntimeEncounterStats initialization helper and call StatToDimensionMappings.ApplyDelta() inside ApplyTrackedDelta after every stat mutation in `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs`
- [X] T015 [P] [US1] Update IBehavioralFrameGenerator interface to accept optional IReadOnlyDictionary\<string, CharacterStatProfileV2\>? characterRuntimeStats parameter in `DreamGenClone.Application/StoryAnalysis/Abstractions/IBehavioralFrameGenerator.cs`
- [X] T016 [P] [US1] Update GenerateFramesAsync and BuildFrameText to use RuntimeEncounterStats values for dimension tier resolution when present; fall back to static EncounterStats when null or empty in `DreamGenClone.Infrastructure/StoryAnalysis/CharacterBehavioralFrameGenerator.cs`
- [X] T017 [US1] Update CreateFromGeneratorAsync to forward CharacterRuntimeStats to frame generator and build CharacterStatStateTexts by collecting out-of-neutral stats per character and concatenating band texts with "; " separator; update CreateFallbackAsync to return empty CharacterStatStateTexts in `DreamGenClone.Infrastructure/StoryAnalysis/ScenarioGuidanceContextFactory.cs`
- [X] T018 [US1] Pass session.AdaptiveState.CharacterStats as CharacterRuntimeStats in ScenarioGuidanceInput (keyed by character display label matching CharacterBehavioralFrames keys) in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`
- [X] T019 [P] [US1] Inject CharacterStatStateTexts immediately after behavioral frame line at site 2 (per-turn constraints block) with format `HARD CONSTRAINT — enforce in this response: {label} current state: {statStateText}` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`
- [X] T020 [P] [US1] Inject CharacterStatStateTexts immediately after behavioral frame line at site 1 (AppendScenarioGuidance) with format `HARD CONSTRAINT — {label} current state (authoritative, overrides all theme notes and guidance): {statStateText}` in `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs`
- [X] T021 [US1] Create unit tests: RuntimeEncounterStats null uses static profile, drifted values change tier text, empty RuntimeEncounterStats falls back to static, neutral band produces empty CharacterStatStateTexts in `DreamGenClone.Tests/RolePlay/BehavioralFrameWithRuntimeStatsTests.cs`

---

## Phase 4: User Story 2 — Protective Husband Transforms Into Submissive Cuck [US2] (P2)

**Goal**: Wire drift into all remaining stat mutation paths (DecisionPointService, UI manual edit) and implement profile rebind reset of RuntimeEncounterStats so the Husband arc works end-to-end.

**Independent test criteria**: After 6 sequential Dominance -8 deltas on a Husband character, RuntimeEncounterStats shows Acceptance and Voyeurism values higher than the bound profile's initial values. After mid-session profile rebind, RuntimeEncounterStats matches the new profile's EncounterStats exactly (prior drift discarded).

- [X] T022 [US2] Call StatToDimensionMappings.ApplyDelta() with initialization guard after each CharacterStatProfileV2Accessor.ApplyDelta() in DecisionPointService.ApplyDeltas() in `DreamGenClone.Infrastructure/RolePlay/DecisionPointService.cs`
- [X] T023 [US2] Compute delta = newValue - oldValue and call StatToDimensionMappings.ApplyDelta() with initialization guard after SetStat manual edit calls in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T024 [US2] Implement RebindEncounterProfile(characterId, profileId) method that assigns CharacterEncounterProfileIds and resets RuntimeEncounterStats to the new profile's EncounterStats in `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs`
- [X] T025 [US2] Update session-creation profile binding in RolePlayEngineService to call RebindEncounterProfile instead of direct dict assignment in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [X] T026 [US2] Update mid-session profile picker onChange handler to call RebindEncounterProfile instead of direct dict assignment in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T027 [US2] Add Husband drift rule tests (Dominance -8 → Acceptance and Voyeurism increase, SelfRespect rules) and profile rebind reset test to `DreamGenClone.Tests/RolePlay/EncounterDimensionDriftTests.cs`

---

## Phase 5: User Story 4 — All Active Out-of-Neutral Stats Produce Coherent Prompt Text [US4] (P2)

**Goal**: Verify the synthesis handles multiple simultaneous out-of-neutral stats as a single coherent sentence and correctly omits injection when all stats are in the neutral band.

**Independent test criteria**: Wife character with Desire=82, Restraint=12, Loyalty=15 produces exactly one stat state text line in the prompt. A character with all stats between 35 and 65 produces zero stat state text lines.

- [X] T028 [US4] Add integration test: Wife with Desire=82, Restraint=12, Loyalty=15 produces a single non-empty synthesized sentence covering all three stat signals in `DreamGenClone.Tests/RolePlay/BehavioralFrameWithRuntimeStatsTests.cs`
- [X] T029 [US4] Add boundary tests: all stats in 35–65 → empty CharacterStatStateTexts for that character; single out-of-neutral stat → one stat text; OtherMan with Dominance=10 → stat state text but no drift in `DreamGenClone.Tests/RolePlay/BehavioralFrameWithRuntimeStatsTests.cs`

---

## Final Phase: Polish & Cross-Cutting Concerns

**Goal**: Cheating formula test coverage, DB inspection tooling, and final validation.

- [X] T030 [P] Create cheating formula tests: Loyalty=70/Desire=60/Restraint=50 → 65 (moderate-high), zero Tension/Connection references in formula in `DreamGenClone.Tests/RolePlay/CheatingFormulaSimplificationTests.cs`
- [X] T031 [P] Create DB query script for inspecting RuntimeEncounterStats per character from CharacterSnapshotsJson in `artifacts/tmp/dbquery/queries/inspect_runtime_encounter_stats.sql`
- [X] T032 Run `dotnet build DreamGenClone.sln -v minimal` and `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj`; confirm zero errors, zero failures, zero remaining Tension/Connection stat field references in non-spec source files

---

## Dependencies

```
Phase 1 (Setup)
  └── T001

Phase 2 [US3] — must complete before Phases 3, 4, 5
  T002 → T003 → T004 → T005 (compiler-driven cascade)
  T006 (independent of T002–T005)
  T007 (independent of T002–T006)
  T008 (independent of T002–T007)
  T009 (depends on T002–T008 complete)

Phase 3 [US1] — starts after T009
  T010 [P] ─────────────────────────────────────────────┐
  T011 [P] ────────────────────┐                         │
  T012 [P] (after T010)        │                         │
  T013 [P] (after T011)        │                         │
  T014 (after T011) ───────────┤                         │
  T015 [P] (after T004) ───────┤                         │
  T016 [P] (after T015) ───────┤                         │
  T017 (after T010, T015, T016)┘                         │
  T018 (after T004) ───────────────────────────────┐     │
  T019 [P] (after T018, T017) ─────────────────────┤     │
  T020 [P] (after T017) ───────────────────────────┘     │
  T021 (after T015–T020) ──────────────────────────────--┘

Phase 4 [US2] — starts after T014, T021
  T022, T023 (parallel, different files)
  T024 (independent)
  T025 (after T024)
  T026 (after T024)
  T027 (after T013, T024)

Phase 5 [US4] — starts after T017, T021
  T028 [P], T029 [P] (parallel, same file)

Final Phase — starts after all above complete
  T030 [P], T031 [P] (parallel)
  T032 (after T030–T031)
```

## Parallel Execution Examples

**Phase 2 inner parallelism**: T006 and T007 can be done simultaneously with T002–T004 since they touch different files.

**Phase 3 initial burst**: T010 and T011 are the first two tasks — implement both catalogs simultaneously. Then T012 and T013 (tests for each) simultaneously. Then T015 and T016 simultaneously.

**Phase 3 injection tasks**: T019 and T020 touch different files (`RolePlayContinuationService.cs` vs `RolePlayAssistantPrompts.cs`) — do them in parallel after T017 and T018 are complete.

**Phase 4**: T022 and T023 touch different files — parallel.

**Final phase**: T030 and T031 touch different files — parallel.

## Implementation Strategy

**MVP scope**: Phase 1 + Phase 2 + Phase 3 = US3 (stat reduction) + US1 (prude wife arc). This delivers the primary P1 value: stat-driven frame drift and synthesized stat state text injection for Wife characters. Husband arc (US2) and multi-stat coherence validation (US4) are P2 follow-ons implementable after MVP ships.

**Incremental delivery order**: T001–T009 (clean break: stat reduction verified), T010–T021 (complete US1 end-to-end), T022–T027 (US2), T028–T029 (US4), T030–T032 (polish).
