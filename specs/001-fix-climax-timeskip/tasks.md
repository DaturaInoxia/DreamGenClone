# Tasks: Fix Climax Time-Skip System

**Branch**: `001-fix-climax-timeskip` | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## Implementation Strategy

**MVP scope**: User Story 1 (one-shot injection) — this is the critical fix that stops the model from looping. User Stories 2 and 3 are quality improvements that can follow incrementally.

**Delivery order**: US1 → US2 → US3 → Polish. Each story is independently testable.

---

## Phase 1: Setup

- [X] T001 Verify build succeeds on current branch state in `DreamGenClone.sln`

## Phase 2: Foundational

- [X] T002 [P] Add `HasRecentUserInstruction` static helper method to `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — checks last N interactions for `ActorName="Instruction"` AND `GeneratedByCommand` is null/empty

## Phase 3: User Story 1 — One-Shot Injection (P1)

**Goal**: Time-skip directive fires exactly once per boundary event, using `PromptIntent.Instruction` on the first overflow actor instead of creating a persistent Instruction interaction.

**Independent test**: Trigger encounter boundary → verify directive appears in exactly one prompt → verify it does NOT appear in subsequent turns.

- [X] T003 [US1] Remove the time-skip Instruction interaction injection block (lines ~1492-1540) from `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — delete the block that creates `RolePlayInteraction` with `ActorName="Instruction"`, adds it to `session.Interactions`, and writes the `MultiEncounterInstructionInjected` debug event
- [X] T004 [US1] Remove the stale `injectedTimeSkipInstruction` variable declaration and the post-loop `IsExcluded = true` block (lines ~1497, ~1591-1595) from `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [X] T005 [US1] Add `timeSkipActive` flag computation before the overflow loop in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — checks `isClimaxPhase && CurrentEncounterNumber > 1 && InteractionsInCurrentEncounter == 0 && TimeSkipPending && !HasRecentUserInstruction(session, 3)`
- [X] T006 [US1] Modify the first-actor branch (`i == 0`) in the overflow loop in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — when `timeSkipActive` is true, set `promptText` to the time-skip directive and use `PromptIntent.Instruction` instead of `PromptIntent.Message`; clear `TimeSkipPending` and log `MultiEncounterTimeSkipDirectiveInjected` debug event
- [X] T007 [US1] Verify that `RolePlayContinuationService.cs` requires NO changes — confirm the "Active Instruction (persistent)" re-injection block (lines 1272-1292) is already guarded by `if (intent != PromptIntent.Instruction)` so it is automatically bypassed when the first actor uses `PromptIntent.Instruction`
- [X] T008 [US1] Write test `TimeSkipPending_True_FirstActorGetsInstructionIntent` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` — verify first actor prompt uses `PromptIntent.Instruction` with directive text
- [X] T009 [US1] Write test `TimeSkipPending_True_SubsequentActorsGetMessageIntent` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` — verify actors 2+ use `PromptIntent.Message`
- [X] T010 [US1] Write test `TimeSkipPending_True_SecondTurnDoesNotReinject` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` — verify directive does not appear in second turn after `TimeSkipPending` is cleared
- [X] T011 [US1] Write test `EncounterNumber1_TimeSkipDoesNotFire` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` — verify `CurrentEncounterNumber > 1` gate prevents firing on encounter 1 initialization

## Phase 4: User Story 2 — Stale Encounter Number (P2)

**Goal**: Directive text contains no encounter number, preventing stale references and premature next-encounter prompting.

**Independent test**: Trigger boundaries at different encounter numbers → verify directive text never contains `#N`.

- [X] T012 [US2] Verify the directive text in T006 uses the encounter-number-agnostic string: `"Close the current encounter naturally. Then advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life."` in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [X] T013 [US2] Write test `TimeSkipPending_True_DirectiveHasNoEncounterNumber` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` — verify directive text contains no `#` character and no numeric encounter reference

## Phase 5: User Story 3 — User Steer Priority (P3)

**Goal**: Engine skips time-skip injection when a user-typed Instruction is active in the last 3 interactions; `TimeSkipPending` persists for retry.

**Independent test**: Type a user steer near a boundary → verify no engine injection → verify `TimeSkipPending` remains true.

- [X] T014 [US3] Add skip-due-to-user-instruction logging in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — when `HasRecentUserInstruction` returns true and `TimeSkipPending` is true, log `MultiEncounterTimeSkipSkippedDueToUserInstruction` debug event and do NOT clear `TimeSkipPending`
- [X] T015 [US3] Write test `UserInstructionPresent_TimeSkipSkipped` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` — verify injection skipped when user Instruction (`GeneratedByCommand` null) is in last 3 interactions
- [X] T016 [US3] Write test `UserInstructionPresent_TimeSkipPendingRemainsTrue` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` — verify `TimeSkipPending` remains true after skip so engine retries next turn
- [X] T017 [US3] Write test `EngineInstructionPresent_TimeSkipNotSkipped` in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` — verify engine Instructions (`GeneratedByCommand="MultiEncounterTimeSkip"`) do NOT trigger the skip

## Phase 6: Polish & Cross-Cutting

- [X] T018 [P] Run full build and verify 0 errors in `DreamGenClone.sln`
- [X] T019 [P] Run all multi-encounter time-skip tests and verify they pass in `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`
- [X] T020 [P] Verify no regression in existing encounter boundary detection tests in `DreamGenClone.Tests/RolePlay/`

---

## Dependencies

```text
T001 (setup) → T002 (helper)
T002 → T005 (flag uses helper)
T003, T004 → T005, T006 (removal before new logic)
T005, T006 → T007 (verify no continuation service changes)
T006 → T008, T009, T010, T011 (tests for US1)
T006 → T012, T013 (US2 depends on US1 directive text)
T005, T014 → T015, T016, T017 (US3 tests)
T018 → T019 → T020 (polish sequence)
```

## Parallel Execution Examples

### Per US1:
- T003 and T004 can be done in parallel (both are removals in the same file but different sections)
- T008, T009, T010, T011 can be written in parallel after T006 (independent test cases)

### Per US3:
- T015, T016, T017 can be written in parallel after T014 (independent test cases)

## Suggested MVP Scope

**MVP = US1 only (T001-T011)**. This fixes the critical looping bug. US2 and US3 are quality improvements that can follow in the same PR or a follow-up.
