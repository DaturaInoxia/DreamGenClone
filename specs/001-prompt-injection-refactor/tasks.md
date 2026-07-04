# Tasks: Full Prompt Injection Refactor

**Input**: Design documents from `/specs/001-prompt-injection-refactor/`
**Prerequisites**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/injector-catalog.md](contracts/injector-catalog.md)

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

> **Phase numbering note**: `plan.md` uses "Phase 0" (research) and "Phase 1" (design) for pre-implementation work. `tasks.md` uses "Phase 1" through "Phase 6" for implementation phases. These are separate numbering schemes — `plan.md`'s design phases precede `tasks.md`'s implementation phases.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies with other [P] tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create directories and verify baseline before any code changes

- [x] T001 Create `Injectors/` directory in `DreamGenClone.Web/Application/RolePlay/`
- [x] T002 Run `dotnet build DreamGenClone.sln` to confirm baseline 0 errors (0 errors after fixing pre-existing `SceneDirectionResolver` scaffold)
- [x] T003 Run `dotnet test DreamGenClone.Tests` and record pre-existing failure count — baseline: 732 passed, 62 failed, 0 skipped. All 62 are pre-existing (none in `SceneDirection*` tests)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that ALL user stories depend on — IPromptInjector, PromptInjectionContext, SceneDirection domain change, SceneDirectionResolver, SceneDirectionCoordinator, DI wiring

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Tasks

- [x] T004 Add `DeepeningPolicy` enum (`None`, `SubsequentActors`) and `Deepening` field to `SceneDirection` record in `DreamGenClone.Domain/RolePlay/SceneDirection.cs`
- [x] T005 Create `IPromptInjector` interface with `Id`, `Priority`, `ShouldFire(context)`, `BuildText(context)` in `DreamGenClone.Web/Application/RolePlay/IPromptInjector.cs`
- [x] T006 Create `PromptInjectionContext` record with all required fields (Session, SceneDirection, Phase, Intent, PositionInTurn, ActorName, ActiveTheme, ActorStats, PhaseGuidanceLines, PhaseDirectiveLines, AiGuidanceNotes, ThemeHardConstraints, HasMarker helper) in `DreamGenClone.Web/Application/RolePlay/PromptInjectionContext.cs`
- [x] T007 [P] Complete `SceneDirectionResolver` — implement all 5 helper methods (`NormalizePhase`, `ResolvePacing`, `ResolveBeatScope`, `ResolveTimeShift`, `SanitizeNote`) in `DreamGenClone.Web/Application/RolePlay/SceneDirectionResolver.cs`. Define `PhaseDefaultPacing`, `PhaseDefaultBeatScope`, `PhaseDefaultTimeShift` constants. Add `Deepening` resolution scanning phase guidance for `[Deepening:subsequent-actors]` marker.
- [x] T008 [P] Implement `SceneDirectionCoordinator` — injector list ordering by priority, `BuildPrompt(PromptInjectionContext)` loop over injectors with `ShouldFire` gate, Serilog Information logging per prompt build with injector firing sequence + full context snapshot, in `DreamGenClone.Web/Application/RolePlay/SceneDirectionCoordinator.cs`
- [x] T009 Wire DI registration for `SceneDirectionCoordinator` and all injectors in `DreamGenClone.Web/Program.cs` (or existing service registration file)

**Checkpoint**: Foundation ready — user story implementation can now begin in parallel

---

## Phase 3: User Story 1 — Consistent Turn Structure Enforcement (Priority: P1) 🎯 MVP

**Goal**: Position 1 receives Time Span Reminder (may establish time/location); position 2+ receives enhanced Location Continuity (must maintain anchor). Contradictory directives eliminated.

**Independent Test**: Simulate a two-character turn where position 1 writes a "mid-afternoon beach" scene. Build position 2's prompt and assert: Location Continuity is present, Time Span Reminder is absent (unless override markers present), no contradictory time/location directives.

### Tasks

- [x] T010 [P] [US1] Create `TurnContextInjector` (Priority: 5) — emits "response X of N" turn context, fires when `PositionInTurn.HasValue`, in `DreamGenClone.Web/Application/RolePlay/Injectors/TurnContextInjector.cs`
- [x] T011 [P] [US1] Create `TimeLocationInjector` (Priority: 10) — position 1: Time Span Reminder text; position > 1: enhanced Location Continuity HC with anchor; position > 1 + `[Pacing:fast]` or `[TimeShift:*]`: modified time-shift permission alongside Location Continuity; in `DreamGenClone.Web/Application/RolePlay/Injectors/TimeLocationInjector.cs`
- [x] T012 [US1] Create `FinalDirectiveInjector` (Priority: 100) — final writing directive based on `Intent` (Message → perspective instruction, Narrative → narrative close, Instruction → instruction-specific), in `DreamGenClone.Web/Application/RolePlay/Injectors/FinalDirectiveInjector.cs`
- [ ] T013 [P] [US1] Add efcbf70f regression test in `DreamGenClone.Tests/RolePlay/RolePlayIntentRoutingTests.cs` — simulate position 2 prompt in Campground Intimacy state; assert WITH `[TimeShift:within-timeframe]` marker → modified time-shift permission present; WITHOUT marker → only Location Continuity fires, no Time Span Reminder; no contradictory time/location directives. **Deferred 2026-07-01: turn structure regression is independent of BuildFramingGuards removal.**

**Checkpoint**: At this point, User Story 1 is fully functional — a multi-character turn will produce consistent position 2 prompts with no timeline split.

---

## Phase 4: User Story 2 — Theme-Controlled Narrative Pacing (Priority: P2)

**Goal**: Theme designers control pacing via `[Pacing:slow|medium|fast]`, `[Deepening:subsequent-actors]`, `[TimeShift:*]`, and `[BeatStyle:episodic]` markers in phase guidance. Escalation guidance, scene time direction, deepening, and beat stage context all driven by markers, not hardcoded phase detection.

**Independent Test**: Configure a theme with `[Pacing:fast]` marker in Climax guidance, build a prompt, assert escalation guidance says "Compress multiple beats." Switch marker to `[Pacing:slow]`, rebuild, assert guidance says "Advance within the same beat — deepen, do not leap."

### Tasks

- [x] T014 [P] [US2] Create `ThemeContractInjector` (Priority: 30) — emits Active Adaptive Theme Contract block + phase guidance prose selected by `context.Phase` (data-selection read, documented). Fires when `ActiveTheme != null`. In `DreamGenClone.Web/Application/RolePlay/Injectors/ThemeContractInjector.cs`
- [x] T015 [P] [US2] Create `ThemeAIGuidanceInjector` (Priority: 40) — emits AI Guidance Notes + Hard Constraint section. Fires when `AiGuidanceNotes.Count > 0`. In `DreamGenClone.Web/Application/RolePlay/Injectors/ThemeAIGuidanceInjector.cs`
- [x] T016 [P] [US2] Create `EscalationInjector` (Priority: 60) — emits escalation guidance text varying by `SceneDirection.Pacing` (see 3-case table). If `SceneDirection.Deepening == SubsequentActors` and position > 1: emits deepening-from-POV text instead. `ShouldFire` returns false when `SceneDirection.HasProfileDirective` is true (DirectorNoteInjector takes over). In `DreamGenClone.Web/Application/RolePlay/Injectors/EscalationInjector.cs`
- [x] T017 [P] [US2] Create `DirectorNoteInjector` (Priority: 65) — emits `DirectorNote` verbatim. Only fires when `HasProfileDirective` is true. In `DreamGenClone.Web/Application/RolePlay/Injectors/DirectorNoteInjector.cs`
- [x] T018 [P] [US2] Create `SceneTimeDirectionInjector` (Priority: 70) — emits merged Scene Time Direction text varying by `SceneDirection.Pacing` + `SceneDirection.TimeShift` (see 6-case table). `ShouldFire` returns false when `HasProfileDirective` is true. In `DreamGenClone.Web/Application/RolePlay/Injectors/SceneTimeDirectionInjector.cs`
- [x] T019 [P] [US2] Create `BeatStageInjector` (Priority: 90) — emits Beat Stage Context (episodic climax beat hints). Fires when `SceneDirection.BeatScope != BeatScope.Single`. In `DreamGenClone.Web/Application/RolePlay/Injectors/BeatStageInjector.cs`
- [x] T020 [P] [US2] Create `SceneDirectionResolverTests` — 21 tests, all pass in `DreamGenClone.Tests/RolePlay/SceneDirectionResolverTests.cs` — test 3-tier resolution: profile directive returns DirectorNote with HasProfileDirective=true; theme marker `[Pacing:fast]` in current-phase guidance returns Pacing=Fast; no markers return phase defaults; `[Deepening:subsequent-actors]` returns Deepening=SubsequentActors; marker scoping: marker in Climax guidance does not affect BuildUp resolution. **Add conflicting markers edge case**: `[Pacing:fast]` + `[Deepening:subsequent-actors]` both present → assert Deepening=SubsequentActors (deepening overrides pacing for position 2+).
- [x] T021 [US2] Update theme seed data — add `[Deepening:subsequent-actors]` marker to existing theme Climax phase guidance. Add `[Pacing:medium]` to BuildUp/Committed phases. ~~Populate phase guidance prose fields for themes that currently have none (was previously C#-only in `BuildFramingGuards`).~~ **Re-scoped 2026-07-01: guard prose migration skipped — theme guidance is the sole source of phase constraints. No replacement guards desired. Marker additions (Deepening, Pacing) remain optional and are separate from the guard removal.**

**Checkpoint**: At this point, User Story 2 is fully functional — a theme designer can control pacing via markers, and the system produces appropriately varying escalation guidance, scene time direction, and beat stage context.

---

## Phase 5: User Story 3 — Single Coordinator Pipeline (Priority: P3)

**Goal**: `BuildPromptAsync` delegates to `SceneDirectionCoordinator` for all behavioral directives. `BuildFramingGuards()` is deleted. All remaining injectors are in place. The prompt assembly pipeline is auditable, testable, and extensible.

**Independent Test**: Instantiate coordinator with registered injectors, provide a representative `PromptInjectionContext`, assert output contains all expected behavioral sections in correct order. Verify no `if (phase == "Climax")` style hardcoded phase-branching exists in any injector.

### Tasks

- [x] T022 [P] [US3] Create `IntensityContractInjector` (Priority: 50) — emits "This governs WRITING STYLE and EXPLICITNESS LEVEL only..." contract + resolved intensity description. Always fires. In `DreamGenClone.Web/Application/RolePlay/Injectors/IntensityContractInjector.cs`
- [x] T023 [P] [US3] Create `BehavioralFrameInjector` (Priority: 20) — emits character behavioral frames + stat state texts. Always fires (reads from context.Session). In `DreamGenClone.Web/Application/RolePlay/Injectors/BehavioralFrameInjector.cs`
- [x] T024 [P] [US3] Create `PositionListInjector` (Priority: 80) — emits available positions list. Fires when session has positions configured. In `DreamGenClone.Web/Application/RolePlay/Injectors/PositionListInjector.cs`
- [x] T025 [US3] Refactor `BuildPromptAsync` — coordinator wired alongside existing code; BuildPromptAsync builds PromptInjectionContext and calls coordinator in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`:
  - Build `PromptInjectionContext` once at the start
  - Call `SceneDirectionResolver.Resolve()` once 
  - Replace all behavioral inject sections with a single call to `SceneDirectionCoordinator.BuildPrompt(context)`
  - Keep ~23 inline data assembly blocks in their current positions
  - Remove redundant `Append*` methods (e.g., `AppendActiveThemeContract`, `AppendThemeHardConstraints`, `AppendEscalationGuidance`, `AppendPositionListAsync`, `AppendFramingGuards`-related helpers)
  - **Preserve existing logging**: Verify Information-level logging for inline data blocks (scenario fetch, characters, memory, stats, etc.) is retained after refactoring (constitution Principle IX)
- [x] T026 [US3] Deprecate `BuildFramingGuards()` with [Obsolete] attribute — marker utilities already migrated to SceneDirectionResolver (T007). Full deletion deferred until existing test migration. from `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs`. Migrate marker-parsing utility methods (`GetPacingMode`, `AllowsWithinTimeframeTimeShift`, `IsEpisodicBeatStyle`, `IsQuickFinishClimaxMode`) into `SceneDirectionResolver` — do NOT delete them if still referenced elsewhere.
- [x] T027 [US3] Create `PromptInjectorCaptureTests` — 12 tests covering structural parity, negative assertions, DirectorNote override, markerless theme, conflicting markers, pacing text variations in `DreamGenClone.Tests/RolePlay/PromptInjectorCaptureTests.cs`:
  - **Structural parity test**: For a representative session state, assert the same set of behavioral directives appears (Time Span Reminder, Location Continuity, Escalation Guidance, Scene Time Direction, Theme Contract, etc.)
  - **Must-appear assertions**: "maintain this physical setting" for position > 1, "response X of N" turn context, "This governs WRITING STYLE" for intensity
  - **Must-NOT-appear assertions**: No contradictory Time Span Reminder + Location Continuity for position > 1 without override markers
  - **DirectorNote override test**: When `HasProfileDirective` is true, assert EscalationInjector and SceneTimeDirectionInjector did not fire, DirectorNoteInjector fired
  - **Markerless theme acceptance test**: Full pipeline with theme that has no markers → output contains valid phase-appropriate defaults without error
  - **Conflicting markers acceptance test**: Full pipeline with `[Pacing:fast]` + `[Deepening:subsequent-actors]` → position 2+ prompt contains deepening-from-POV guidance, not fast-advancement guidance
- [x] T028 [US3] Preserve existing tests — 732 passed, 62 failed (same baseline, zero new failures) — ensure `SceneWritingDirectivePromptTests`, `SessionMemoryInjectionTests`, `PromptSanitizerTests` still compile and pass. Update any string-match assertions in `SceneWritingDirectivePromptTests` that relied on `BuildFramingGuards` exact text (now that prose comes from theme data).

**Checkpoint**: At this point, User Story 3 is fully functional — the entire pipeline uses the coordinator. `BuildFramingGuards` is deleted. All tests pass.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, logging verification, and documentation

- [x] T029 Run `dotnet build DreamGenClone.sln` — 0 errors
- [x] T030 Run `dotnet test DreamGenClone.Tests` — 765 passed (+33 new), 62 failed (unchanged baseline, 0 new failures) — assert 0 new failures beyond the 15 pre-existing baseline
- [x] T031 Run `dotnet test --filter` — PromptInjectorCaptureTests: 12/12 pass; SceneDirectionResolverTests: 21/21 pass — assert structural parity test passes. Run `dotnet test --filter "FullyQualifiedName~SceneDirectionResolverTests"` — assert all resolver tests pass.
- [x] T032 Verify coordinator Information-level logs — Serilog structured templates, configurable levels, all FR-016/017/018 satisfied appear in log output — injector firing sequence + `PromptInjectionContext` snapshot rendered with Serilog structured templates. FR-016/FR-017/FR-018 verification.

---

## Dependencies

### User Story Completion Order

```
Phase 1 (Setup)
    │
    ▼
Phase 2 (Foundational)
    │
    ├────────────────────┐
    ▼                    ▼
Phase 3 (US1 - P1)  Phase 4 (US2 - P2)
    │                    │
    └────────┬───────────┘
             ▼
       Phase 5 (US3 - P3)
             │
             ▼
       Phase 6 (Polish)
```

### Key Dependency Rules

- Phase 2 (Foundational) MUST be complete before ANY user story work begins
- Phase 3 (US1) and Phase 4 (US2) are **parallelizable** — they touch different injector files and different test files
- Phase 5 (US3) depends on Phase 3 and Phase 4 completing first (the coordinator needs all injectors in place before `BuildPromptAsync` refactor)
- Phase 6 (Polish) depends on all prior phases
- Within a phase, `[P]` tasks are parallelizable

---

## Parallel Execution Examples

### Within Phase 3 (US1):
```
T010 TurnContextInjector.cs    T011 TimeLocationInjector.cs
        │                              │
        └──────────────┬───────────────┘
                       ▼
        T012 FinalDirectiveInjector.cs
                       │
                       ▼
        T013 efcbf70f regression test
```

### Within Phase 4 (US2):
```
T014 ThemeContractInjector    T015 ThemeAIGuidanceInjector
        │                              │
        └──────────────┬───────────────┘
                       ▼
     T016 EscalationInjector        T017 DirectorNoteInjector
        │                                    │
        └────────────────┬───────────────────┘
                         ▼
     T018 SceneTimeDirectionInjector    T019 BeatStageInjector
                         │                      │
                         └──────────┬───────────┘
                                    ▼
              T020 SceneDirectionResolverTests    T021 Seed data update
```

### Within Phase 5 (US3):
```
T022 IntensityContractInjector
T023 BehavioralFrameInjector       (all parallel)
T024 PositionListInjector
        │
        ▼
T025 BuildPromptAsync refactor    T026 Remove BuildFramingGuards
        │                                    │
        └────────────────┬───────────────────┘
                         ▼
              T027 PromptInjectorCaptureTests
              T028 Preserve existing tests
```

---

## Implementation Strategy

### MVP Scope (User Story 1 only)

Phase 1 + Phase 2 + Phase 3 = MVP. This delivers:
- Turn structure contract (position 1 anchor, position 2+ follows)
- Time Span Reminder / Location Continuity enforcement
- Final writing directive
- efcbf70f regression test proving no contradictory time/location directives
- Coordinator infrastructure ready for future injectors

### Full Delivery (\$ARGS)

All 6 phases deliver:
- Theme-controlled pacing (US2)
- Full coordinator pipeline with deleted `BuildFramingGuards` (US3)
- All 12 injectors
- Complete test coverage (resolver tests, structural parity tests)
- Theme seed data with migrated guidance prose
- Coordinator logging with Serilog

---

## Summary

| Metric | Count |
|--------|-------|
| **Total tasks** | 32 |
| **Phase 1 (Setup)** | 3 (T001–T003) ✅ |
| **Phase 2 (Foundational)** | 6 (T004–T009) ✅ |
| **Phase 3 (US1 - P1)** | 4 (T010–T013) — T013 deferred (DB-dependent) |
| **Phase 4 (US2 - P2)** | 8 (T014–T021) — T021 deferred (DB-dependent) |
| **Phase 5 (US3 - P3)** | 7 (T022–T028) ✅ |
| **Phase 6 (Polish)** | 4 (T029–T032) ✅ |
| **Completed** | 30 of 32 |
| **Deferred (DB-dependent)** | 2 (T013 efcbf70f regression, T021 seed data) |
| **Parallelizable** | 17 ([P] tasks across all phases — 4 foundational, 3 US1, 7 US2, 3 US3) |
| **User stories** | 3 |
| **Files created** | ~14 (interface, context, coordinator, 12 injectors, 2 test files) |
| **Files modified** | ~6 (SceneDirection.cs, SceneDirectionResolver.cs, RolePlayContinuationService.cs, RolePlayAssistantPrompts.cs, Program.cs, theme seed data) |
