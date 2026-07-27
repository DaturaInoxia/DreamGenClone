# Tasks: Final Writing Instruction Consolidation

**Input**: Design documents from `/specs/001-final-writing-instruction/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: SlotContractTests.cs is updated per slot changes. Integration testing (FR-013) is a separate validation phase.

**Organization**: Tasks grouped by user story. UI (US3/US4) sequenced last per spec clarification.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)

---

## Phase 1: Data Foundation (Setup)

**Purpose**: Add new fields to data models and DB schema — no prompt or UI changes yet.

- [x] T001 [P] [US3] Add `ImmersionDirective`, `ActionDirective`, `WordTargetMin`, `WordTargetMax`, `NarrativeWordTargetMin`, `NarrativeWordTargetMax` to `DreamGenClone.Domain/StoryAnalysis/SteeringProfile.cs`
- [x] T002 [P] [US4] Add `Tone`, `Register`, `Focus` fields to `DreamGenClone.Web/Domain/Scenarios/NarrativeSettings.cs`; deprecate `NarrativeTone` (retain for backward compat)
- [x] T003 [US3] Run ALTER TABLE migration on `StyleProfiles` table to add 6 new columns with default values (empty/zero — fail-fast at runtime until populated)

**Checkpoint**: Data models and DB schema ready. Existing code still works (new fields are additive).

---

## Phase 2: Profile Data Cleanup (DB Only)

**Purpose**: Populate new fields and clean up profile data per research.md decisions.

- [x] T004 [P] [US5] DB: DELETE Atmospheric row from `ToneProfiles` (`Id=96b9e19cd16048a49e6460d0c115e658`)
- [x] T005 [P] [US5] DB: INSERT new Atmospheric row into `StyleProfiles` with populated required fields (per research.md R3)
- [x] T006 [P] [US5] DB: UPDATE Sensual `ToneProfile` Description to cleaned heat-level-only text (per research.md R2)
- [x] T007 [P] [US5] DB: UPDATE Emotional `ToneProfile` Description to cleaned heat-level-only text (per research.md R2)
- [x] T008 [P] [US3] DB: UPDATE Sultry `StyleProfile` to populate `ImmersionDirective`, `ActionDirective`, `WordTargetMin`, `WordTargetMax`, `NarrativeWordTargetMin`, `NarrativeWordTargetMax` (per research.md R4)
- [x] T009 [P] [US4] DB: UPDATE scenario `135a9237` PayloadJson to populate `Tone`="Erotic, conversational, playful", `Register`="Low to moderate language complexity", `Focus`="Physical pleasure" in NarrativeSettings (per research.md R5)

**Checkpoint**: All profile data cleaned and new fields populated. Atmospheric is a StyleProfile. Sultry has required fields. Scenario 135a9237 has decomposed tone.

---

## Phase 3: Prompt Slot Changes (Core)

**Purpose**: Consolidate writing direction into Slot 17 (FinalInstruction). Remove writing direction from Slots 8, 12, and 15.

### Slot 17 — Consolidated Writing Instruction

- [x] T010 [US4] Add `ResolvedNarrativeToneData` sub-record and field to `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs`; extend `ResolvedWritingStyleData` with `ProfileName`, `ImmersionDirective`, `ActionDirective`, `WordTargetMin`, `WordTargetMax`, `NarrativeWordTargetMin`, `NarrativeWordTargetMax`
- [x] T011 [US4] Implement 3-tier Tone resolution (new Tone → legacy NarrativeTone → null) in prompt builder or resolver; resolve SteeringProfile new fields with fail-fast validation (FR-006, FR-008)
- [x] T012 [US1] [US2] Rewrite `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/FinalInstructionSlot.cs`: consolidated 9-component output per contracts/slot-17-output-contract.md (Scene Direction before Writing Instruction per R1; Writing Instruction at absolute end; Character + Narrative variants)
- [x] T013 [US1] Update all prompt-facing labels to writer-standard terms in `FinalInstructionSlot.cs`: "Prose Style", "Voice", "Tone", "Heat Level", "Pacing", "Scene Direction" (per contracts/terminology-mapping.md)

### Slots 8, 12, 15 — Remove Writing Direction

- [x] T014 [US2] Rewrite `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/WritingStyleSlot.cs`: remove writing direction emission; emit only single reference line "Writing direction: see Writing Instruction below." (or empty)
- [x] T015 [US2] Rewrite `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/IntensityPacingSlot.cs`: remove heat level, contract, pacing emission; retain only available positions
- [x] T016 [US2] Clean up `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/ThemeContractSlot.cs`: confirm phase guidance prose is removed (already commented out — remove dead code comments)

### Tests

- [x] T017 [US1] [US2] Update `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs`: update expected strings for new labels (US1), consolidated Slot 17 output (US2), stripped-down Slots 8/15 output (US2)

**Checkpoint**: All writing direction consolidated into Slot 17. Slots 8 and 15 are structural only. Slot 12 no longer emits phase guidance. Labels use writer-standard terms. Contract tests pass.

---

## Phase 4: UI Changes (Sequenced Last — Dedicated Agent)

**Purpose**: Expose new SteeringProfile and NarrativeSettings fields in the UI per spec clarification (all UI grouped, sequenced last).

- [x] T018 [US3] Add editable fields for `ImmersionDirective`, `ActionDirective`, `WordTargetMin`, `WordTargetMax`, `NarrativeWordTargetMin`, `NarrativeWordTargetMax` to the Style Profile management Razor page
- [x] T019 [US4] Add editable fields for `Tone`, `Register`, `Focus` to the Scenario narrative settings Razor page; deprecate/hide legacy `NarrativeTone` field

**Checkpoint**: Writers can configure all new fields via the UI. Values persist and are reflected in the next built prompt.

---

## Phase 5: Validation & Integration Testing

**Purpose**: Verify the implementation works end-to-end and the Scene Direction ↔ Writing Instruction ordering passes validation.

- [x] T020 Build solution: `dotnet build DreamGenClone.sln`
- [x] T021 Run slot contract tests: `dotnet test DreamGenClone.Tests --filter "SlotContractTests"`
- [x] T022 Run full role-play test suite: `dotnet test DreamGenClone.Tests --filter "RolePlay"`
- [x] T023 Integration testing for Scene Direction ↔ Writing Instruction ordering per FR-013: (A) manual qualitative review of N sample generations per ordering against 4-item checklist; (B) automated scoring script for objective markers; (C) single-author subjective review. Chosen ordering must pass all three.

**Checkpoint**: All tests pass. Ordering validated. Feature ready for spec amendment.

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Data Foundation) ──→ Phase 2 (Profile Cleanup)
                                      │
         ┌────────────────────────────┘
         ▼
Phase 3 (Slot Changes) ──→ Phase 4 (UI) ──→ Phase 5 (Validation)
```

### Task Dependencies Within Phases

**Phase 1**: T001 + T002 can run in parallel (different files). T003 after DB schema is understood (can run after T001).

**Phase 2**: All tasks T004-T009 are [P] — independent DB updates on different rows/tables.

**Phase 3**: T010-T011 must complete before T012 (FinalInstructionSlot needs resolved data). T011 must complete before T013 (labels reference resolved data). T014, T015, T016 can run in parallel with each other and with T012. T017 depends on all slot rewrites.

**Phase 4**: T018 + T019 can run in parallel.

**Phase 5**: Sequential — T020 → T021 → T022 → T023.

### User Story Mapping

| Task | US1 (Terminology) | US2 (Consolidation) | US3 (Configurable Directives) | US4 (Tone Decomp) | US5 (Profile Cleanup) |
|------|:---:|:---:|:---:|:---:|:---:|
| T001 | | | ✓ | | |
| T002 | | | | ✓ | |
| T003 | | | ✓ | | |
| T004-T007 | | | | | ✓ |
| T008 | | | ✓ | | |
| T009 | | | | ✓ | |
| T010-T011 | | | | ✓ | |
| T012 | ✓ | ✓ | | | |
| T013 | ✓ | | | | |
| T014 | | ✓ | | | |
| T015 | | ✓ | | | |
| T016 | | ✓ | | | |
| T017 | ✓ | ✓ | | | |
| T018 | | | ✓ | | |
| T019 | | | | ✓ | |
| T020-T023 | ✓ | ✓ | ✓ | ✓ | ✓ |

---

## Implementation Strategy

### MVP First (Phase 1 + 2 + 3 Only)

1. Complete Phase 1: Data Foundation
2. Complete Phase 2: Profile Data Cleanup
3. Complete Phase 3: Slot Changes
4. **STOP**: Build + run tests. Manual prompt inspection. If good, proceed to UI.

### Incremental Delivery

1. Phases 1-2 → Schema and data ready. Existing prompts still work.
2. Phase 3 → Consolidated Slot 17. Writer-standard labels. Strip Slots 8/15. **Core feature delivered.**
3. Phase 4 (UI) → Writers can configure fields. **Full feature delivered.**
4. Phase 5 → Validation and integration testing.

---

## Parallel Opportunities

- **Phase 1**: T001 + T002 in parallel (Domain vs Web/Domain — different projects)
- **Phase 2**: T004-T009 all in parallel (independent DB rows)
- **Phase 3**: T014 + T015 + T016 in parallel with T012 (different slot files)
- **Phase 4**: T018 + T019 in parallel (different Razor pages)

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Phase 4 (UI) is grouped for dedicated-agent implementation per spec clarification
- Each checkpoint validates independently
- Hard Rule: no hardcoded fallbacks — T011 fail-fast validation must be complete before T012 merges
