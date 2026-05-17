# Tasks: B-024 Narrative Prompt Fix

**Input**: `specs/024-narrative-prompt-fix/` — plan.md, spec.md, research.md, data-model.md  
**Branch**: `024-narrative-prompt-fix`  
**Date**: 2026-05-14

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Parallelizable — different files, no unresolved dependency
- **[US#]**: User story / requirement mapping

---

## Phase 1: Setup

**Purpose**: Verify baseline — confirm existing narrative validation tests pass before any changes.

- [X] T001 Run existing narrative validation tests to confirm green baseline: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~NarrativeValidation" -v minimal`

---

## Phase 2: Foundational (REQ-8 — Location Label Helper)

**Purpose**: `NarrativeLocationLabel` is a pure static helper used by REQ-8 injection sites. Must exist before the injection site edits in Phase 3.

**⚠️ Complete before Phase 3**

- [X] T002 Add private static helper `NarrativeLocationLabel(string? raw)` to `RolePlayContinuationService.cs` — strips subtitle after first ` — `, ` – `, ` - `, or ` : ` separator and returns the leading part trimmed; returns empty string for null/empty input in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`
- [X] T003 [P] Add unit tests for `NarrativeLocationLabel` covering: em-dash subtitle stripped, colon subtitle stripped, plain name unchanged, null/empty input — in `DreamGenClone.Tests/RolePlay/RolePlayContinuationNarrativeValidationTests.cs`

---

## Phase 3: User Story 1 — Prompt Construction Fixes (REQ-1, REQ-6, REQ-7-prompt, REQ-8 sites)

**Goal**: Every call to `BuildPromptAsync` with `PromptIntent.Narrative` produces a prompt that uses the correct intensity, enumerates physical scene categories, suppresses dialogue, and injects a sanitized location label.

**Independent Test**: In `ContinueBatchAsync_NarrativePrompt_*` tests — assert `styleHint` is not Atmospheric when session has high intensity; assert writing instruction contains enumerated physical-detail categories; assert location string in prompt does not contain subtitle text.

- [X] T004 [US1] Remove the `if (intent == PromptIntent.Narrative)` intensity override block (~line 966) in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — delete lines that set `effectiveStyleLabel = IntensityLadder.GetLabel(IntensityLevel.Intro)` and append `"narrative-forced-atmospheric"` to `effectiveStyleReason`; the `scenePresenceScale` pre-capture remains
- [X] T005 [US1] [P] Replace the non-Climax narrative writing instruction (~line 1131) in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` with a rewritten instruction that: (a) states narrator role is omniscient; (b) enumerates required categories — spatial layout, room details, lighting, sounds, character positions relative to each other and environment; (c) includes "Your priority is the physical scene and environment — where characters are, how they are positioned, what surrounds them, what sounds and sensory details exist."; (d) sets zero-dialogue default: "Include zero quoted speech. Include one brief spoken fragment only if it is absolutely required for scene continuity and cannot be omitted."; (e) keeps 100–300 words output target
- [X] T006 [US1] [P] Replace the Climax narrative writing instruction (~line 1122) in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` with a rewritten instruction that: (a) enumerates required physical-detail categories — bodies, clothing/undress state, physical contact points, exact body part positions, physical sensations, sounds (breathing, movement, ambient), rhythm and movement; (b) includes "Write as a detailed physical account — what is touching what, how characters are positioned, what sensations and sounds are present. This is not about feelings or decisions; it is about what is physically occurring."; (c) sets zero-dialogue absolute: "Include zero quoted speech. Do not write any dialogue in this passage."; (d) keeps 300-word minimum; (e) keeps persona name reference and `styleHint`
- [X] T007 [US1] Apply `NarrativeLocationLabel` to the top HARD CONSTRAINT location line (~line 402) in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — wrap `session.AdaptiveState.CurrentSceneLocation` with the helper before interpolation
- [X] T008 [US1] [P] Apply `NarrativeLocationLabel` to the Scene Continuity Anchor location line (~line 650) in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`
- [X] T009 [US1] [P] Write tests in `DreamGenClone.Tests/RolePlay/RolePlayContinuationNarrativeValidationTests.cs`:
  - `NarrativePrompt_AtHighIntensity_StyleHintIsNotAtmospheric` — session with non-Intro intensity; assert prompt `styleHint` does not contain "Atmospheric" / "Intro"
  - `NarrativePrompt_NonClimax_ContainsSceneDescriptionCategories` — assert prompt contains "spatial layout" or "where characters are" and zero-dialogue instruction
  - `NarrativePrompt_Climax_ContainsPhysicalDetailCategories` — assert prompt contains "physical contact" or "body part positions" and zero-dialogue instruction
  - `NarrativePrompt_LocationSubtitleStripped` — session with location `"Trailer — Shared Space"`; assert HARD CONSTRAINT line contains `"Trailer"` and not `"Shared Space"`

---

## Phase 4: User Story 2 — Validation Pipeline Fixes (REQ-3, REQ-4, REQ-7-threshold)

**Goal**: `AnalyzeNarrativeOutput` correctly classifies narrator-body first-person, triggers retry on interiority, and applies a stricter quoted-block threshold in Climax mode.

**Independent Test**: Existing retry tests still pass. New tests cover quote-strip first-person, interiority-triggers-retry, and Climax threshold=1 behavior.

- [X] T010 [US2] In `AnalyzeNarrativeOutput` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`, compute `narratorBodyOnly` by replacing all `QuotedBlockRegex` matches with empty string, then apply `FirstPersonLeakRegex` to `narratorBodyOnly` instead of the full `text`
- [X] T011 [US2] Add `|| interiorityCount > 0` to the `shouldRetry` condition in `AnalyzeNarrativeOutput` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`
- [X] T012 [US2] Add `bool climaxMode` parameter to `AnalyzeNarrativeOutput` signature in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`; resolve effective threshold inside: `var quotedThreshold = climaxMode ? 1 : NarrativeQuotedBlockRetryThreshold;` — replace all uses of `NarrativeQuotedBlockRetryThreshold` in the method body with `quotedThreshold`
- [X] T013 [US2] Update `GenerateNarrativeWithValidationAsync` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` to pass `climaxMode` based on `session.AdaptiveState.CurrentNarrativePhase == NarrativePhase.Climax` to both `AnalyzeNarrativeOutput` calls
- [X] T014 [US2] [P] Write tests in `DreamGenClone.Tests/RolePlay/RolePlayContinuationNarrativeValidationTests.cs`:
  - `NarrativeValidation_FirstPersonInQuote_DoesNotTriggerRetry` — output `"\"I wasn't ready,\" she said."` → `firstPersonCount == 0`, `ShouldRetry == false`
  - `NarrativeValidation_FirstPersonInNarratorBody_TriggersRetry` — output `"I moved through the hallway."` → `firstPersonCount > 0`, `ShouldRetry == true`
  - `NarrativeValidation_Interiority_TriggersRetry` — output `"She thought about the previous night."` → `HasViolation == true`, `ShouldRetry == true`
  - `NarrativeValidation_CliaxMode_SingleQuote_TriggersRetry` — one quoted block, climaxMode=true → `ShouldRetry == true`
  - `NarrativeValidation_NonClimaxMode_SingleQuote_NoRetry` — one quoted block, climaxMode=false → `ShouldRetry == false`

---

## Phase 5: User Story 3 — Violation-Specific Correction Prompt (REQ-5)

**Goal**: `BuildNarrativeCorrectionPrompt` produces targeted fix instructions matching the specific violations found.

**Independent Test**: Tests assert each violation type produces the matching correction clause and only that clause.

- [X] T015 [US3] Update `BuildNarrativeCorrectionPrompt` signature in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` to `BuildNarrativeCorrectionPrompt(string originalPrompt, NarrativeValidationResult analysis)` — replace generic correction text with violation-specific clauses: (a) if `QuotedBlockCount >= threshold`: "Found {n} quoted blocks — reduce to zero. Do not write any dialogue."; (b) if `FirstPersonLeakCount > 0`: "Found first-person pronoun in narrator body — write in third person throughout; do not use 'I', 'me', 'my', 'mine', or 'myself' outside of a quoted fragment."; (c) if `CharacterInteriorityCount > 0`: "Found inner-thought phrases — remove sentences about what characters thought, felt, wondered, realized, or decided; describe only externally observable actions, positions, and states."; (d) if `DialogueAttributionCount >= 2 && QuotedBlockCount >= 2`: "Found multi-character dialogue exchange — eliminate back-and-forth; include at most one brief spoken fragment." — always include a closing: "Rewrite focusing on physical scene: positions, surroundings, sensations, and movement."
- [X] T016 [US3] Update the call site in `GenerateNarrativeWithValidationAsync` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` to pass `firstAnalysis` to `BuildNarrativeCorrectionPrompt`
- [X] T017 [US3] [P] Write tests in `DreamGenClone.Tests/RolePlay/RolePlayContinuationNarrativeValidationTests.cs`:
  - `CorrectionPrompt_QuotedBlockOnly_ContainsQuoteClause_NotFirstPersonClause`
  - `CorrectionPrompt_FirstPersonOnly_ContainsFirstPersonClause_NotQuoteClause`
  - `CorrectionPrompt_Interiority_ContainsInteriorityClause`
  - `CorrectionPrompt_MultipleViolations_ContainsAllRelevantClauses`

---

## Phase 6: User Story 4 — Validation Pipeline Interface (REQ-2)

**Goal**: All `PromptIntent.Narrative` generation routes through the validated pipeline. `RolePlayEngineService` no longer calls `ContinueAsync(PromptIntent.Narrative)` directly.

**Independent Test**: Stub completion client debug events show `NarrativeValidation` events for opening narrative and `/narrative` command paths (currently absent).

- [X] T018 [US4] Add `Task<RolePlayInteraction> ContinueNarrativeAsync(RolePlaySession session, string actorName, string promptText, CancellationToken cancellationToken = default)` to `DreamGenClone.Web/Application/RolePlay/IRolePlayContinuationService.cs`
- [X] T019 [US4] Implement `ContinueNarrativeAsync` in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`: call `BuildPromptAsync(session, ContinueAsActor.Npc, actorName, PromptIntent.Narrative, promptText, ct)`, resolve model via `_modelResolver`, call `GenerateNarrativeWithValidationAsync`, return a `RolePlayInteraction` with `InteractionType.System`, `ActorName = actorName`, `GeneratedByCommand = "Narrative"`, and the model metadata fields
- [X] T020 [US4] Update the `/narrative` command call site in `RolePlayEngineService` (~line 727) in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — replace `_continuationService.ContinueAsync(... PromptIntent.Narrative ...)` with `_continuationService.ContinueNarrativeAsync(session, actorName, promptText, cancellationToken)`
- [X] T021 [US4] Update the opening narrative call site in `RolePlayEngineService` (~line 1126) in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — replace `_continuationService.ContinueAsync(... PromptIntent.Narrative, openingPrompt, onChunk ...)` with `_continuationService.ContinueNarrativeAsync(session, "Narrative", openingPrompt, cancellationToken)` and remove the `onChunk` streaming parameter (opening narrative is non-streaming)
- [X] T022 [US4] [P] Write tests in `DreamGenClone.Tests/RolePlay/RolePlayContinuationNarrativeValidationTests.cs`:
  - `ContinueNarrativeAsync_ValidOutput_ReturnsInteractionWithNarrativeValidationEvent` — assert `NarrativeValidation` debug event emitted
  - `ContinueNarrativeAsync_ViolatingOutput_RetriesAndEmitsWarning` — first model output has quoted blocks; assert retry attempted and warning event present

---

## Phase 7: Polish

**Purpose**: Build verification and test suite run.

- [X] T023 Build solution to confirm zero errors: `dotnet build DreamGenClone.sln -v minimal`
- [X] T024 [P] Run full narrative validation test suite: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~NarrativeValidation" -v normal`

---

## Dependencies

```
T001 (baseline)
  └─ T002, T003 (Phase 2 — helper)
       └─ T004–T009 (Phase 3 — prompt construction, parallel within phase)
            └─ T010–T014 (Phase 4 — validation logic, parallel within phase)
                 └─ T015–T017 (Phase 5 — correction prompt, parallel within phase)
                      └─ T018–T022 (Phase 6 — interface + engine wiring)
                           └─ T023–T024 (Phase 7 — verify)
```

## Parallel Opportunities Per Phase

| Phase | Parallel tasks |
|-------|---------------|
| 2 | T002 (helper impl) + T003 (helper tests) |
| 3 | T005, T006, T007, T008 (different methods/lines); T009 (tests) |
| 4 | T010–T013 (all in same method — sequential by edit site); T014 (tests — parallel after T010–T013) |
| 5 | T015–T016 (sequential — T016 depends on T015 signature); T017 (tests — after T015) |
| 6 | T018 (interface) → T019 (impl) → T020, T021 (engine, parallel) → T022 (tests) |
| 7 | T023 (build) + T024 (tests, parallel) |

## Implementation Strategy

**MVP scope** (minimum to ship): T001–T009 (Phase 1–3). Removes the intensity override and rewrites the writing instructions. This alone delivers the largest user-visible improvement: richer scene description at correct intensity.

**Full scope**: Complete all phases in order. Each phase is independently testable before the next begins.

**Suggested delivery order**:
1. Phase 1–2 (baseline + helper) — 15 min
2. Phase 3 (prompt construction) — 30 min
3. Phase 4 (validation logic) — 20 min
4. Phase 5 (correction prompt) — 15 min
5. Phase 6 (interface wiring) — 30 min
6. Phase 7 (verify) — 10 min
