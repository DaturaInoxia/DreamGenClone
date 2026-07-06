---
description: "Task list for B-056 Wife-Husband Aftermath Closure"
---

# Tasks: Wife-Husband Aftermath Closure

**Input**: Design documents from `/specs/001-husband-aftermath/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests ARE included — the spec mandates a regression baseline (`SC-005`/`MultiEncounterTimeSkipTests`) and the B-056 design plan Phase G lists an 18-test matrix. Pure-unit tests following `MultiEncounterTimeSkipTests.cs` patterns.

**Organization**: Tasks are grouped by user story. US1 is the MVP (closure turn sequencing). US2 (marker opt-in) and US3 (actor focus) build on US1's state-machine foundation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- All paths are repo-relative

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Extend the `TimeSkipPhase` enum and add the `LastEncounterEvidenceSpan` state field — shared infrastructure that every user story depends on. No new project; the existing layered .NET solution absorbs the changes at natural extension points.

- [x] T001 Extend `TimeSkipPhase` enum with `AftermathCoupleInteraction = 3` and update the enum summary XML doc to describe the optional third leg in `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs:293`
- [x] T002 Add `LastEncounterEvidenceSpan` string? property to `AdaptiveScenarioState` with XML doc describing lifecycle (capture at detection, persist, clear on None) and the `IsStateDirty` contract in `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` (sibling of `CurrentTimeSkipPhase` at line 161)
- [x] T003 Update the `IsStateDirty` docstring at `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs:219` to list `LastEncounterEvidenceSpan` as part of the dirty-flag set

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Persistence migration, state hydration, and the marker helper + mapping contract — these MUST be complete before any user story's state-machine or injector work can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T004 Add `LastEncounterEvidenceSpan TEXT` column migration to `RolePlayStateRepository.EnsureSchemaAsync` using the existing `HasColumnAsync` + `ALTER TABLE` pattern in `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs:1030`
- [x] T005 Update INSERT/UPDATE SQL in `RolePlayStateRepository.cs:222-288` to include `LastEncounterEvidenceSpan = excluded.LastEncounterEvidenceSpan` and add `$lastEncounterEvidenceSpan` parameter binding (write `state.LastEncounterEvidenceSpan ?? (object)DBNull.Value`) at the sibling line of `:327`
- [x] T006 Update SELECT SQL and reader mapping in `RolePlayStateRepository.cs:505-598` to project `LastEncounterEvidenceSpan` and map it as `reader.IsDBNull(36) ? null : reader.GetString(36)` mirroring the `CurrentTimeSkipPhase` read pattern at line 595
- [x] T007 Restore `LastEncounterEvidenceSpan` in `HydrateV2State` alongside the existing `CurrentTimeSkipPhase`/`CurrentEncounterNumber`/`InteractionsInCurrentEncounter` restore block in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs:4257-4263`
- [x] T008 [P] Create `IsAftermathHusbandContrast(RPTheme? theme, string phase)` static helper mirroring `IsMultiEncounterClimax` (line 57) with explicit `phase == "Reset" → return false` exclusion in `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs`
- [x] T009 Widen `EnsureEncounterCompletedMappingAsync` in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs:4530` to also throw when `IsAftermathHusbandContrast(theme, phase)` is true and no `encounter-completed` semantic mapping exists — same `InvalidOperationException` type, same fail-fast pattern

**Checkpoint**: Foundation ready — enum extended, state persisted, marker detection + strict mapping contract available for user story work.

---

## Phase 3: User Story 1 — Post-Encounter Closure with Husband (Priority: P1) 🎯 MVP

**Goal**: After an encounter boundary, the state machine inserts the `AftermathCoupleInteraction` closure leg between `CloseScene` and `AdvanceTime` when both markers are active (Climax), or as a standalone closure turn when only the aftermath marker is active (any non-Reset phase).

**Independent Test**: Configure a theme with `[Aftermath:husband-contrast]` in Climax guidance plus `[ClimaxMode:multi-encounter]`, play past an encounter boundary in a multi-encounter flow, and observe the debug events sequence `MultiEncounterInstructionInjected(CloseScene) → MultiEncounterInstructionInjected(AftermathCoupleInteraction) → MultiEncounterInstructionInjected(AdvanceTime)` with the aftermath directive text mentioning the husband and the contrast expectation.

### Tests for User Story 1

> **NOTE**: Write these tests FIRST, ensure they FAIL before implementation. Pure-unit, mirroring `MultiEncounterTimeSkipTests.cs` patterns (inline `AdaptiveScenarioState` construction, no DI).

- [x] T010 [P] [US1] Create `AftermathHusbandContrastTests.cs` skeleton with class declaration and shared constructor setup mirroring `MultiEncounterTimeSkipTests.cs` in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T011 [P] [US1] Write `TimeSkipPhase_AftermathCoupleInteraction_HasValue3` enum sanity test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T012 [P] [US1] Write `CloseScene_Phase_Transitions_To_AftermathCoupleInteraction_WhenMarkerPresent` (then to AdvanceTime) in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T013 [P] [US1] Write `CloseScene_Phase_Transitions_To_AdvanceTime_WhenMarkerAbsent` regression test for the existing split in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T014 [P] [US1] Write `AftermathCoupleInteraction_Transitions_ToAdvanceTime_WhenMultiEncounter` test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T015 [P] [US1] Write `AftermathCoupleInteraction_Transitions_ToNone_WhenNoMultiEncounter` test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T016 [P] [US1] Write `HasRecentUserInstruction_DeferStaysActiveDuringAftermathLeg` test confirming FR-005 deferral semantics extend to the new leg in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`

### Implementation for User Story 1

- [x] T017 [US1] Generalize `TryDetectEncounterBoundaryAsync` phase gate at `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs:4545` — replace `if (state.CurrentPhase != Climax) return;` with `if (state.CurrentPhase == Reset) return;` and load theme once at method top
- [x] T018 [US1] Replace the `if (state.CurrentEncounterNumber <= 0) return;` early return at `RolePlayEngineService.cs:4546` with a marker-aware check: return early if neither `IsMultiEncounterClimax(theme, phase)` nor `IsAftermathHusbandContrast(theme, phase)` is true
- [x] T019 [US1] Move the `IsMultiEncounterClimax(theme, "Climax")` gate at `RolePlayEngineService.cs:4579` from a hard return to a branch contributor; move the `InteractionsInCurrentEncounter < minIxns` (min 4 interactions) guard at `RolePlayEngineService.cs:4595` INSIDE the multi-encounter branch only
- [x] T020 [US1] After detection succeeds + keyword gate passes in `TryDetectEncounterBoundaryAsync`, branch on which marker(s) are active: multi-encounter + Climax → bump `CurrentEncounterNumber`, reset `InteractionsInCurrentEncounter`, set `CurrentTimeSkipPhase = CloseScene` (existing); aftermath marker present → set `state.LastEncounterEvidenceSpan = detected.EvidenceSpan`; both markers + Climax → both consequences fire atomically; aftermath only (non-Climax or Climax without multi-encounter) → set `CurrentTimeSkipPhase = AftermathCoupleInteraction` directly (skip CloseScene) in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs:4544`
- [x] T021 [US1] Add `AftermathCoupleInteraction` branch to the overflow time-skip injection block at `RolePlayEngineService.cs:1532`: when `CurrentTimeSkipPhase == AftermathCoupleInteraction`, emit the aftermath contrast directive text and advance phase to `AdvanceTime` if multi-encounter active, else `None`; mark `IsStateDirty = true`
- [x] T022 [US1] Change `CloseScene → AdvanceTime` transition at `RolePlayEngineService.cs:1533` to `CloseScene → AftermathCoupleInteraction` when the theme carries `[Aftermath:husband-contrast]`, otherwise `CloseScene → AdvanceTime` (existing); rewrite the `CloseScene` directive text from `"Close the current encounter naturally."` to the explicit closure directive per FR-010 in `RolePlayEngineService.cs:1533`
- [x] T023 [US1] Emit `MultiEncounterInstructionInjected` debug event with `phase = "AftermathCoupleInteraction"` for the new leg, mirroring the existing event emission at `RolePlayEngineService.cs:1558-1573`
- [x] T024 [US1] Add Information-level Serilog logs for the aftermath leg transition (entering AftermathCoupleInteraction, advancing to AdvanceTime or None) with structured properties `{SessionId}`, `{Phase}`, `{EncounterNumber}` in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

**Checkpoint**: User Story 1 fully functional — the aftermath closure turn fires between CloseScene and AdvanceTime (or standalone in non-Climax phases). Existing 28 multi-encounter tests still pass (marker-absent path unchanged except for the FR-010 CloseScene prose rewrite).

---

## Phase 4: User Story 2 — Author Opt-In via Theme Marker (Priority: P2)

**Goal**: Theme authors control aftermath activation by adding/removing `[Aftermath:husband-contrast]` in phase guidance text. Marker works in any non-Reset phase; Reset is explicitly excluded. Missing `encounter-completed` mapping fails fast.

**Independent Test**: Edit a theme's phase guidance to add/remove the marker and verify aftermath behavior only activates when the marker is present; verify Reset phase ignores the marker; verify a theme with the marker but no `encounter-completed` mapping throws a configuration error at session init.

### Tests for User Story 2

- [x] T025 [P] [US2] Write `IsAftermathHusbandContrast_ReturnsTrue_WhenMarkerPresent` test (any non-Reset phase) in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T026 [P] [US2] Write `IsAftermathHusbandContrast_ReturnsFalse_ForResetPhase` explicit out-of-scope test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T027 [P] [US2] Write `IsAftermathHusbandContrast_ReturnsFalse_WhenMarkerAbsent` test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T028 [P] [US2] Write `TryDetectEncounterBoundary_FiresInBuildUp_WhenMarkerPresent` test covering phase-gate relaxation in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T029 [P] [US2] Write `TryDetectEncounterBoundary_SkipsInReset_EvenWithMarker` out-of-scope enforcement test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`

### Implementation for User Story 2

- [x] T030 [US2] Verify the `IsAftermathHusbandContrast` helper (created in T008) correctly handles `activeTheme is null → return false` and the case-insensitive `GuidanceText.Contains("[Aftermath:husband-contrast]", StringComparison.OrdinalIgnoreCase)` pattern in `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs`
- [x] T031 [US2] Add Serilog `LogDebug` trace to `IsAftermathHusbandContrast` callers showing marker resolution result (`{ThemeId}`, `{Phase}`, `{MarkerPresent}`) in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (the `TryDetectEncounterBoundaryAsync` marker-aware branch)

**Checkpoint**: User Story 2 fully functional — marker opt-in is verified, Reset exclusion enforced, non-Climax phases supported, missing mapping fails fast.

---

## Phase 5: User Story 3 — Husband-Wife Actor Focus (Priority: P3)

**Goal**: During `AftermathCoupleInteraction`, overflow actor selection is restricted to wife + husband only (persona excluded). If either spouse is unresolvable, the aftermath leg aborts explicitly with a diagnostic log + debug event (no silent fallback). Fast Pacing HC is suppressed during the aftermath leg only.

**Independent Test**: Trigger aftermath in a scenario with 3+ characters (including persona) and verify only wife and husband appear in overflow actor candidates; verify missing-spouse scenarios produce `HusbandAftermathAbortedMissingSpouse` debug event and clean abort; verify Fast Pacing HC is suppressed in the assembled prompt during aftermath.

### Tests for User Story 3

- [x] T032 [P] [US3] Write `HusbandAftermathInjector_ShouldFire_WhenPhaseIsAftermathCoupleInteraction` test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T033 [P] [US3] Write `HusbandAftermathInjector_ShouldNotFire_WhenPhaseIsCloseScene_OrAdvanceTime_OrNone` test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T034 [P] [US3] Write `HusbandAftermathInjector_BuildText_ReferencesLastEncounterEvidenceSpan` test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T035 [P] [US3] Write `FinalDirectiveInjector_SuppressesFastPacingHC_WhenAftermathPhaseActive` test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T036 [P] [US3] Write `FinalDirectiveInjector_FiresNormally_WhenAftermathPhaseInactive` regression test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T037 [P] [US3] Write `AftermathHusbandActorFilter_ReturnsWifeThenHusband` test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`
- [x] T038 [P] [US3] Write `AftermathHusbandActorFilter_AbortsAndLogs_WhenSpouseMissing` test in `DreamGenClone.Tests/RolePlay/AftermathHusbandContrastTests.cs`

### Implementation for User Story 3

- [x] T039 [P] [US3] Create `HusbandAftermathInjector` class implementing `IPromptInjector` with `Id = "husband-aftermath"`, `Priority = 85`, `ShouldFire` returning true when `CurrentTimeSkipPhase == AftermathCoupleInteraction`, and `BuildText` emitting the FR-007 contrast directive referencing `LastEncounterEvidenceSpan` with the "had an intimate encounter with another man" fallback in `DreamGenClone.Web/Application/RolePlay/Injectors/HusbandAftermathInjector.cs` (new file)
- [x] T040 [US3] Register `HusbandAftermathInjector` in the `IPromptInjector` DI loop in `DreamGenClone.Web/Program.cs:122-135` alongside the existing 12 injectors
- [x] T041 [US3] Suppress the Fast Pacing HC in `FinalDirectiveInjector.BuildText` by adding `&& context.Session.AdaptiveState.CurrentTimeSkipPhase != TimeSkipPhase.AftermathCoupleInteraction` to the existing Fast Pacing `if` guard in `DreamGenClone.Web/Application/RolePlay/Injectors/FinalDirectiveInjector.cs` (keep `ShouldFire` unchanged — base closer still emits)
- [x] T042 [US3] Extract `ResolveSpouseForAftermathAsync(RolePlaySession session, CancellationToken ct)` helper from the existing spouse-resolution logic at `RolePlayEngineService.cs:2730-2755` so both `BuildOpeningNarrativePromptAsync` and the aftermath actor filter share one source of truth; helper returns `(personaName, spouseName?)` in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [x] T043 [US3] Add aftermath actor filter branch at the start of `ResolveSceneContinueActorsAsync` at `RolePlayEngineService.cs:2185`: when `CurrentTimeSkipPhase == AftermathCoupleInteraction`, call `ResolveSpouseForAftermathAsync`, build a `List<OverflowActorCandidate>` with wife first then husband (both as `ContinueAsActor.Npc`), persona excluded
- [x] T044 [US3] Implement the explicit abort path in `ResolveSceneContinueActorsAsync`: if wife or husband is missing from scenario characters, emit `HusbandAftermathAbortedMissingSpouse` Serilog `LogWarning` with structured properties `{SessionId}`, `{PersonaName}`, `{SpouseName}`, `{Reason}` + write `RolePlayDebugEventRecord` for the diagnostic panel, clear `CurrentTimeSkipPhase` to `AdvanceTime` if multi-encounter active else `None`, set `IsStateDirty = true`, return empty `List<OverflowActorCandidate>()` in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs:2185`
- [x] T045 [US3] Refactor `BuildOpeningNarrativePromptAsync` at `RolePlayEngineService.cs:2730-2755` to call the new `ResolveSpouseForAftermathAsync` helper instead of the inline spouse-resolution logic (single source of truth)

**Checkpoint**: User Story 3 fully functional — actor filter restricts to wife + husband with explicit abort, Fast Pacing HC suppressed during aftermath, injector registered and firing at priority 85.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Backlog hygiene, documentation, and end-to-end validation.

- [x] T046 [P] Update `specs/Planning/backlog.md` to move B-056 state `new → designed` with notes referencing the spec/plan/research artifacts and the marker name `[Aftermath:husband-contrast]`
- [x] T047 [P] Annotate B-054 in `specs/Planning/backlog.md` with note: "Subsumed by B-056's `AftermathCoupleInteraction = 3` enum extension. B-054 documented the original intent; B-056 delivers a generalized marker-driven version that works in any phase."
- [x] T048 Run full build: `dotnet build DreamGenClone.sln --no-restore` — must report 0 errors
- [x] T049 Run existing multi-encounter regression tests: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~MultiEncounterTimeSkip" --no-build` — 28 tests must still pass
- [x] T050 Run new aftermath tests: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~AftermathHusbandContrastTests" --no-build` — all new tests pass
- [x] T051 Run RolePlay suite regression: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlay" --no-build` — existing suite still green
- [x] T052 Verify DB schema: `dotnet run --project artifacts/tmp/dbquery -- schema RolePlayV2AdaptiveStates` — confirm `LastEncounterEvidenceSpan TEXT` column exists after schema bootstrap
- [ ] T053 Run `quickstart.md` validation — manual end-to-end smoke (optional, post-implementation): seed a theme with `[ClimaxMode:multi-encounter] [Aftermath:husband-contrast]` in Climax phase guidance + `encounter-completed` semantic mapping, play past an encounter boundary, verify the debug events sequence and directive text per `specs/001-husband-aftermath/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately. T001 → T002 → T003 are sequential within the same file.
- **Foundational (Phase 2)**: Depends on Setup completion. T004 → T005 → T006 are sequential (same file, schema → write → read). T007 depends on T002. T008 is parallel. T009 depends on T008.
- **User Stories (Phase 3–5)**: All depend on Foundational phase completion.
  - US1 (Phase 3) is the MVP — implements the core state-machine extension.
  - US2 (Phase 4) depends on US1's marker-aware detection branch (T017-T020) and the helper from T008.
  - US3 (Phase 5) depends on US1's `AftermathCoupleInteraction` phase existing in the state machine (T021-T022) so the injector and actor filter have a phase to react to.
- **Polish (Phase 6)**: Depends on all user stories being complete. T046-T047 are parallel; T048-T053 are sequential validation steps.

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2). No dependencies on other stories. **MVP** — delivers the closure turn sequencing.
- **User Story 2 (P2)**: Depends on US1's detection generalization (T017-T020) and the `IsAftermathHusbandContrast` helper (T008). Independently testable: marker presence/absence + Reset exclusion + non-Climax support.
- **User Story 3 (P3)**: Depends on US1's `AftermathCoupleInteraction` state-machine branch (T021-T022). Independently testable: actor filter restriction + abort path + Fast Pacing suppression.

### Within Each User Story

- Tests written FIRST (pure-unit, must FAIL before implementation) per the spec's regression-baseline mandate.
- Models/enum extensions before services.
- Detection generalization before state-machine branch insertion.
- State-machine branch before injector registration.
- Spouse helper extraction before actor filter branch.

### Parallel Opportunities

- **Phase 2**: T008 (`IsAftermathHusbandContrast` helper) is parallel with T004-T007 (persistence migration + hydration).
- **Phase 3 tests**: T010-T016 are all parallel — same test file but different test methods, no inter-test dependencies.
- **Phase 4 tests**: T025-T029 are all parallel.
- **Phase 5 tests**: T032-T038 are all parallel.
- **Phase 5 implementation**: T039 (new injector file) is parallel with T041 (FinalDirectiveInjector edit) and T042 (helper extraction) — different files, no dependencies.
- **Phase 6**: T046 (backlog B-056) and T047 (backlog B-054 annotation) are parallel.

---

## Parallel Example: User Story 1 Tests

```bash
# Launch all US1 tests together (all in AftermathHusbandContrastTests.cs, different methods):
Task: "T011 [P] [US1] Write TimeSkipPhase_AftermathCoupleInteraction_HasValue3 enum sanity test"
Task: "T012 [P] [US1] Write CloseScene_Phase_Transitions_To_AftermathCoupleInteraction_WhenMarkerPresent"
Task: "T013 [P] [US1] Write CloseScene_Phase_Transitions_To_AdvanceTime_WhenMarkerAbsent regression test"
Task: "T014 [P] [US1] Write AftermathCoupleInteraction_Transitions_ToAdvanceTime_WhenMultiEncounter test"
Task: "T015 [P] [US1] Write AftermathCoupleInteraction_Transitions_ToNone_WhenNoMultiEncounter test"
Task: "T016 [P] [US1] Write HasRecentUserInstruction_DeferStaysActiveDuringAftermathLeg test"
```

## Parallel Example: User Story 3 Implementation

```bash
# Launch the three independent US3 implementation files together:
Task: "T039 [P] [US3] Create HusbandAftermathInjector.cs (new file)"
Task: "T041 [US3] Suppress Fast Pacing HC in FinalDirectiveInjector.cs"
Task: "T042 [US3] Extract ResolveSpouseForAftermathAsync helper in RolePlayEngineService.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T003) — enum extension + state field
2. Complete Phase 2: Foundational (T004-T009) — persistence + hydration + marker helper + mapping contract
3. Complete Phase 3: User Story 1 (T010-T024) — detection generalization + state-machine branch + CloseScene prose rewrite
4. **STOP and VALIDATE**: Run `MultiEncounterTimeSkipTests` (28 tests, regression) + `AftermathHusbandContrastTests` US1 subset. Confirm the closure turn fires between CloseScene and AdvanceTime.
5. Deploy/demo if ready — the MVP delivers the core closure value.

### Incremental Delivery

1. Setup + Foundational → Foundation ready (enum + persistence + marker helper)
2. Add User Story 1 → Test independently → MVP! (closure turn sequencing)
3. Add User Story 2 → Test independently → marker opt-in verified, Reset excluded, non-Climax supported
4. Add User Story 3 → Test independently → actor filter + abort path + Fast Pacing suppression
5. Polish phase → backlog hygiene + full regression validation + DB inspection + optional E2E smoke
6. Each story adds value without breaking previous stories (FR-010 CloseScene prose rewrite improves marker-absent themes too).

### Parallel Team Strategy

With multiple developers after Foundational completes:
- Developer A: US1 (state-machine extension — the critical path)
- Developer B: US2 (marker detection verification — can start in parallel with US1 since T008 helper exists from Foundational)
- Developer C: US3 (injector + actor filter — can start T039/T041/T042 in parallel with US1's state-machine work, but T043-T044 depend on US1's `AftermathCoupleInteraction` branch landing)

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- [Story] label maps task to specific user story for traceability.
- Each user story is independently completable and testable.
- Tests MUST fail before implementing — pure-unit pattern mirrors `MultiEncounterTimeSkipTests.cs` (inline state, no DI, no shared static state — per repo memory on xUnit parallel execution).
- Commit after each task or logical group.
- Stop at any checkpoint to validate the story independently.
- Repo no-fallback rule (`.github/copilot-instructions.md`): T044 abort path MUST NOT silently substitute actors — explicit diagnostic log + state clear only.
- Repo roleplay-engine strict config contract: the `[Aftermath:husband-contrast]` marker is UI-backed editable persisted data (theme phase-guidance text) — no hidden code-only defaults.
- Razor editing rules do not apply — no `.razor` files are touched in this feature.
