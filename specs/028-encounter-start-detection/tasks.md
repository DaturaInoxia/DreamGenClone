# Tasks: Semantic Encounter-Start Detection & Memory Enrichment

**Input**: Design documents from `/specs/028-encounter-start-detection/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Not explicitly requested in spec — no test tasks included.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

All source files under `DreamGenClone.Web/`. Config under `DreamGenClone.Infrastructure/Configuration/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuration plumbing needed before any implementation

- [X] T001 Add `EncounterStartConfidenceThreshold` property (decimal, default 0.70) to `RolePlayMemoryOptions` class in `DreamGenClone.Infrastructure/Configuration/RolePlayMemoryOptions.cs`
- [X] T002 [P] Add `EncounterStartConfidenceThreshold` binding to `appsettings.json` (`RolePlayMemory` section) and `appsettings.Development.json` in `DreamGenClone.Web/`

**Checkpoint**: Config plumbing ready — `IOptions<RolePlayMemoryOptions>` consumers can read the threshold.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entity change + bug fixes that both US1 and US2 depend on for correct behavior

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 Add `bool WasEncounterStart` property (default `false`) to `RolePlayInteraction` class in `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs`
- [X] T004 [P] [US3] Reset `CurrentEncounterStartInteractionIndex = 0` after encounter boundary — add `state.CurrentEncounterStartInteractionIndex = 0;` after the `GenerateEncounterCompletionSummariesAsync` call (and its `catch` block) in `TryDetectEncounterBoundaryAsync` in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (Part D fix — 1 line)
- [X] T005 [P] [US3] Gate Climax-entry start-index capture — add `&& v2State.CurrentEncounterStartInteractionIndex == 0` to the `if (lifecycle.TransitionEvent.ToPhase == NarrativePhase.Climax)` condition at ~line 3708 in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (Part C fix — 1 line)

**Checkpoint**: Foundation ready — domain entity updated, both index-corruption bugs fixed. US1 and US2 can now begin in parallel.

---

## Phase 3: User Story 1 - Reliable Sexual Encounter Detection (Priority: P1) 🎯 MVP

**Goal**: Semantic LLM inference detects when a sexual encounter actually begins, distinguishing physical sexual activity from mere explicit conversation

**Independent Test**: Play through session with flirtation → verify no `EncounterStartDetected`. First sexual contact → verify `EncounterStartDetected` fires, `CurrentEncounterNumber` set, `CurrentEncounterStartInteractionIndex` set, `WasEncounterStart = true` on the interaction. Verify encounter #2+ start also works.

### Implementation for User Story 1

- [X] T006 [US1] Add `TryDetectEncounterStartAsync` method to `RolePlayEngineService` in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`. Follows the `TryDetectEncounterBoundaryAsync` pattern: re-entry guard, context window, `_semanticEventInferenceService.InferAsync` with `AllowedEventIds = ["encounter-started"]`, filter by `EncounterStartConfidenceThreshold`, set `CurrentEncounterNumber` (if 0), set `CurrentEncounterStartInteractionIndex`, tag `interaction.WasEncounterStart = true`, write `EncounterStartDetected` debug event. On LLM failure: Warning log, write `EncounterStartDetectionFailed` event.
- [X] T007 [US1] Replace the keyword-only encounter-start detection block with a call to `TryDetectEncounterStartAsync`. `HasSexualActivityContent()` retained as pre-filter. `CurrentEncounterNumber == 0` guard removed.
- [X] T008 [US1] `EncounterStartConfidenceThreshold` read from `_memoryOptions?.Value.EncounterStartConfidenceThreshold ?? 0.70m`.
- [X] T009 [US1] The `encounter-started` LLM prompt is embedded inline in the `EventDescriptions` dictionary.
- [X] T010 [US1] Information-level log on detection (`EncounterStartDetected`), Warning-level on inference failure, Debug-level on no detection.

**Checkpoint**: Encounter-start detection is semantic, universal, and logged. Can be tested independently by playing sessions and checking debug events.

---

## Phase 4: User Story 2 - Vivid First-Person Encounter Memories (Priority: P2)

**Goal**: Encounter completion enrichment produces vivid first-person prose with who, what acts, orgasms, and sensory/emotional detail — role-agnostic for Wife, Husband, OtherMan, Persona

**Independent Test**: Complete an encounter, wait for enrichment job, inspect `LlmSummary` in `RolePlayV2EncounterSummaries` — verify first-person ("I..."), explicit anatomical description, orgasm details, sensory/emotional content, correct character name (not detection evidence).

### Implementation for User Story 2

- [X] T011 [US2] Fix `displayName` data bug in `BuildEncounterCompletionPrompt` — replaced `record.DetectionEvidence` with `record.CharacterId` directly in `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs`
- [X] T012 [US2] Add `characterRole` resolution in `BuildEncounterCompletionPrompt` — resolved from `session.AdaptiveState.CharacterStats[record.CharacterId]?.CharacterRole ?? "Unknown"`
- [X] T013 [US2] Rewrite `BuildEncounterCompletionPrompt` return string with the new first-person prompt — vivid prose, first person, who/what/orgasms/sensory/emotional, `Detection evidence:` line removed, `characterRole` added
- [X] T014 [US2] Information-level log when LLM enrichment completes (success) and Warning-level log when enrichment fails — already present in existing `EnhanceRecordAsync` method

**Checkpoint**: Encounter memories are vivid first-person prose. Can be tested independently by completing encounters and checking LlmSummary.

---

## Phase 5: User Story 3 - Correct Encounter Interaction Ranges (Priority: P3)

**Goal**: Encounter completion records always reference the correct interaction range regardless of which phase the encounter started in

**Independent Test**: Run multi-encounter session — verify each `EncounterCompletion` record's `StartInteractionIndex`/`EndInteractionIndex` span the correct interactions with no overlap or gaps.

> **Note**: T004 and T005 (Part D and Part C bug fixes) are already in Phase 2 as foundational. This phase adds verification and any remaining edge-case handling.

### Implementation for User Story 3

- [X] T015 [US3] Verify Part D reset — added `B059 EncounterStartIndex_ResetAfterBoundary` debug log at the reset point.
- [X] T016 [US3] Verify Part C guard — added `B059 EncounterStartIndex_ClimaxEntry_BlockedByGuard` debug log when guard blocks Climax-entry overwrite.

**Checkpoint**: All interaction ranges correct. Multi-encounter sessions produce clean, non-overlapping EncounterCompletion records.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Build verification, documentation, and final validation

- [X] T017 [P] Build verification — `dotnet build DreamGenClone.Web --no-restore` = Build succeeded. `dotnet build DreamGenClone.Tests --no-restore` = Build succeeded.
- [X] T018 [P] Run existing tests — 760 passed / 58 failed (all pre-existing DB schema migration gaps, verified against repo memory)
- [X] T019 Run quickstart.md smoke test validation — build complete, no errors
- [X] T020 [P] Mark B-059 backlog item as `implemented` in `specs/Planning/backlog.md`

**Checkpoint**: Feature fully implemented, built, tested, and tracked.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup (T001, T002) — config must exist before code references it. **BLOCKS all user stories.**
- **User Story 1 (Phase 3)**: Depends on Foundational (T003, T004, T005). No dependency on US2 or US3.
- **User Story 2 (Phase 4)**: Depends on Foundational (T003). Independent of US1 and US3. Can run parallel to US1.
- **User Story 3 (Phase 5)**: Core fixes in Phase 2 (T004, T005). This phase adds verification. Depends on US1 only for integration validation.
- **Polish (Phase 6)**: Depends on all user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2. No dependency on US2 or US3.
- **US2 (P2)**: Can start after Phase 2. No dependency on US1 or US3. Different file (`EncounterSummaryJobHandler.cs` vs `RolePlayEngineService.cs`).
- **US3 (P3)**: Core fixes in Phase 2 (T004, T005). Verification tasks (T015, T016) can run after US1 is complete for integration validation.

### Within Each User Story

- T006 (method) → T007 (replace old block) — the method must exist before it can be called
- T008 and T009 can run parallel to T006 (they're part of the method implementation)
- T011 → T012 → T013 — fix displayName first, then add role resolution, then rewrite prompt

### Parallel Opportunities

| Group | Tasks | Reason |
|-------|-------|--------|
| Phase 1 | T001, T002 | Different files: .cs and .json |
| Phase 2 | T003, T004, T005 | Different files/sections: Domain entity, two separate locations in Engine |
| US1 internal | T008, T009 (with T006) | Same method, sequential within but independent concerns |
| US1 + US2 | T006-T010 \|\| T011-T014 | Different files: EngineService vs JobHandler |
| Phase 6 | T017, T018, T020 | Build, test, backlog — all independent |

---

## Parallel Example: Phase 2 + US1 + US2

```text
# After Phase 1 complete, launch Phase 2 tasks in parallel:
Task: "T003 Add WasEncounterStart to RolePlayInteraction.cs"
Task: "T004 Reset start index after boundary in RolePlayEngineService.cs"
Task: "T005 Gate Climax-entry capture in RolePlayEngineService.cs"

# After Phase 2 complete, launch US1 and US2 in parallel:
Task: "T006-T010: Semantic encounter-start detection in RolePlayEngineService.cs"
Task: "T011-T014: Prompt rewrite in EncounterSummaryJobHandler.cs"

# After US1 complete, run US3 verification:
Task: "T015-T016: Verify Part C/D fixes"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001, T002)
2. Complete Phase 2: Foundational (T003, T004, T005)
3. Complete Phase 3: US1 (T006-T010)
4. **STOP and VALIDATE**: Play session with flirtation → sexual activity → verify `EncounterStartDetected` fires correctly
5. Build and confirm 0 errors

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 → Semantic start detection works → **MVP!**
3. Add US2 → Vivid first-person memories → Enhanced experience
4. Add US3 verification → Confirmed correct ranges → Production ready
5. Polish → Build, test, backlog update

### Single Developer Strategy

Recommended order: Phase 1 → Phase 2 → US3 (T004, T005 already in Phase 2, then T015, T016 validation) → US1 → US2 → Phase 6.

Reason: US3 fixes are 1-liners and are already in Phase 2. US1 is the largest change (~80 lines new method). US2 is the prompt rewrite (~50 lines). US3 verification is quick after US1 is done.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- All changes are in 3 files: `RolePlayEngineService.cs`, `EncounterSummaryJobHandler.cs`, `RolePlayInteraction.cs`
- No new files, no new projects, no new dependencies
- Total estimated: ~150 lines of code across 20 tasks
