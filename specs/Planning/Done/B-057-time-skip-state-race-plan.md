# Plan: B-057 — Time-skip state race — HydrateV2State overwrites detection

**TL;DR**: Two-part refactor. **Part A**: Every time-skip mutation persists synchronously — `HydrateV2State` becomes unconditional DB restore. **Part B**: Universal encounter tracking (any phase, any marker) + interaction-level encounter metadata for diagnostics UI. DB always authoritative. Never revisit this fix.

## Root Cause (Part A)

`HydrateV2State` loads from DB (stale — last-turn snapshot) and overwrites in-memory state that detection just advanced in the same turn. The partial B-057 fix added conditional protection logic + save-before-hydrate flushes at 2 of 4 call sites, but the protection logic has ordinal-comparison gaps and the pattern is fragile.

### Scope of the Race Condition

The race exists **only** in `HydrateV2State` (called from `RunRolePlayV2PipelinesAsync` at ~line 3023).
`AlignPromptNarrativeStateWithV2Async` (called at ~line 934, 1122, 1229, 1268, 1280, 1441, 1459, 1656, 1682, 1733) **intentionally skips** `CurrentTimeSkipPhase` / `CurrentEncounterNumber` / `InteractionsInCurrentEncounter` — it synchronizes only phase, scenario, theme, and character snaps. This means 10 of 11 HydrateV2State call sites are already safe from the time-skip race. Only the 1 site inside `RunRolePlayV2PipelinesAsync` at line 3023 fires the race. The plan simplifies all paths unconditionally so the invariant holds even if new call sites are added.

**Implication**: If the unconditional restore in Phase 4 changes behavior unexpectedly, test only the `RunRolePlayV2PipelinesAsync` → `HydrateV2State` path. The other 10 paths already don't touch these fields.

## Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| **Part A approach** | Option E — sync persist ALL time-skip mutations | Eliminates every possible desync path at the root. DB always authoritative. |
| **Encounter scope** | Universal — any phase, any marker | Not gated on `[ClimaxMode:multi-encounter]`. Encounters start on first sexual content, end on keyword/LLM completion detection. |
| **Counter model** | Separate: `GlobalEncounterCount` (cumulative) + `CurrentEncounterNumber` (active) | `CurrentEncounterNumber` repurposed as universal active-encounter tracker. Multi-encounter Climax uses global counter for numbering instead of hardcoded `1`. |
| **Multi-encounter integration** | Unified numbering | `CurrentEncounterNumber = GlobalEncounterCount + 1` on Climax entry (instead of hardcoded `1`). Encounters numbered globally: encounter 1 (BuildUp), encounter 2 (Climax), etc. |
| **Interaction data** | Nullable fields on `RolePlayInteraction` | Legacy interactions don't have this data. New fields are `int?` / `string?`. |
| **Content classification** | Keyword-based (free, always available) | Uses existing `SexualActivityKeywords` / `SubtleSexualActivityKeywords` / `EncounterCompletionKeywords` arrays. No LLM cost. |
| **Explicitness** | String label from session's resolved intensity | Captures `LastResolvedIntensityLabel` at creation time. No new inference needed. |

---

## Part A: Synchronous persist for time-skip state

### Phase 1: Sync persist in `TryDetectEncounterBoundaryAsync`

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (~line 4785)

After the detection mutation block, add synchronous persist + reset dirty:
```csharp
state.CurrentEncounterNumber++;
state.InteractionsInCurrentEncounter = 0;
state.CurrentTimeSkipPhase = TimeSkipPhase.CloseScene;
state.CharacterEncounterStates.Clear();
state.IsStateDirty = true;

// NEW: synchronous persist — DB is always authoritative
await _stateRepository.SaveAdaptiveStateAsync(state, cancellationToken);
state.IsStateDirty = false;
```

### Phase 2: Sync persist in overflow time-skip phase transitions

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (~lines 1556, 1567, 1573)

Three sites in the multi-encounter time-skip overflow injection block. Replace each `session.AdaptiveState.IsStateDirty = true` with:
```csharp
await _stateRepository.SaveAdaptiveStateAsync(session.AdaptiveState, cancellationToken);
session.AdaptiveState.IsStateDirty = false;
```

Sites:
- CloseScene → AftermathCoupleInteraction (or AdvanceTime if no aftermath)
- AftermathCoupleInteraction → AdvanceTime (or None if aftermath-only)
- AdvanceTime → None

### Phase 3: Sync persist in `ResolveOverflowContinueActorsAsync` cleanup

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (~line 2279)

Replace `IsStateDirty = true` with synchronous persist:
```csharp
session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.None;
await _stateRepository.SaveAdaptiveStateAsync(session.AdaptiveState, cancellationToken);
session.AdaptiveState.IsStateDirty = false;
```

### Phase 4: Simplify `HydrateV2State` — unconditional DB restore

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (~lines 4368-4395)

Remove the conditional protection block. Replace with:
```csharp
// All time-skip mutations persist synchronously. DB is always authoritative.
// Unconditional restore — no conditional logic needed.
mapped.CurrentTimeSkipPhase = previousState.CurrentTimeSkipPhase;
mapped.CurrentEncounterNumber = previousState.CurrentEncounterNumber;
mapped.InteractionsInCurrentEncounter = previousState.InteractionsInCurrentEncounter;
mapped.LastEncounterEvidenceSpan = previousState.LastEncounterEvidenceSpan;
```

> **Forward-compat (B-058)**: B-058 Phase 5.3 removes `LastEncounterEvidenceSpan` from `AdaptiveScenarioState` entirely. When B-058 is implemented, the `mapped.LastEncounterEvidenceSpan = …` line above becomes a compilation error and must be deleted at that point. Do NOT pre-delete it now — leaving it in place avoids a broken build between B-057 and B-058.

### Phase 5: Remove B-057 save-before-hydrate pattern

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (~lines 1322-1328, ~1763-1771, ~1797)

Remove the IsStateDirty save blocks before `RunRolePlayV2PipelinesAsync` at these three sites:
1. **`SubmitPromptAsync`** (~line 1326) — save block after climax completion interaction generation, before pipeline
2. **`ContinueAsAsync` post-overflow** (~line 1771) — save block after overflow continue actors loop, before pipeline
3. **`ContinueAsAsync` single-actor fallback** (~line 1797) — save block after fallback continue and before pipeline

Update comments: *"Redundant — time-skip mutations persist synchronously at their mutation sites."*

> **Keep the turn-completion save block** (~line 1820, `ContinueAsAsync` post-`FlushAsync`):
> ```csharp
> if (session.AdaptiveState.IsStateDirty) { await _stateRepository.SaveAdaptiveStateAsync(...); }
> await _stateRepository.CompleteTurnAsync(...);
> ```
> This block persists `IsStateDirty = true` for **non-time-skip** state changes (character stats, theme scores, location state). After sync persist, time-skip mutations will already be flushed, so this block naturally becomes a no-op for time-skip and a partial save for everything else. **Do NOT remove or change this block.**

---

## Part B: Universal encounter tracking + interaction-level metadata

### Phase 6: New fields on `AdaptiveScenarioState`

**File:** `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`

Add:
```csharp
/// Cumulative count of ALL completed encounters in this session.
/// Incremented on every encounter boundary detection (any phase, any marker).
/// Never decremented. Persisted in DB.
public int GlobalEncounterCount { get; set; }
```

Update `CurrentEncounterNumber` XML doc to reflect universal semantics. **Important semantic distinction**: `CurrentEncounterNumber = 0` means **no active encounter** (inactive/dormant), NOT "encounter counter reset to zero". The cumulative total of all completed encounters lives in `GlobalEncounterCount`, which is never decremented.

Lifecycle:
- Set to `GlobalEncounterCount + 1` on first sexual content in any phase
- Set to `GlobalEncounterCount + 1` on Climax entry (multi-encounter)
- Incremented on boundary detection (existing behaviour, now universal)
- Set to `0` on boundary completion (encounter no longer active)
- Set to `0` on phase Reset or leaving Climax (existing behaviour) 

This field can be `0` while `GlobalEncounterCount > 0` — that is expected (no active encounter right now, but past encounters have occurred).

### Phase 7: New fields on `RolePlayInteraction`

**File:** `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs`

Add:
```csharp
/// This interaction's position in the session (0-based). null = legacy data.
public int? SessionInteractionIndex { get; set; }

/// Which global encounter # this interaction belongs to. null = no active encounter / legacy.
public int? EncounterNumberAtCreation { get; set; }

/// Position within the encounter (0-based). null = not in an encounter / legacy.
public int? InteractionIndexInEncounter { get; set; }

/// Session's resolved intensity label at creation time (e.g. "Explicit", "Hardcore"). null = legacy.
public string? ExplicitnessLevelAtCreation { get; set; }
```

### Phase 8: Universal encounter tracking logic

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
**Method:** `UpdateStateAndDetectEncounterAsync` (~lines 2528-2562)

Extend per-interaction processing with:

1. **Stamp session interaction index**: Simple incrementing counter on `RolePlayEngineService` (field or from interaction list count)
2. **Detect encounter start**: If `WasInSexScene` just became true AND `CurrentEncounterNumber == 0` → `CurrentEncounterNumber = GlobalEncounterCount + 1`
3. **Universal interaction counter**: If `CurrentEncounterNumber > 0`, increment `InteractionsInCurrentEncounter` (always, not just in Climax)
4. **Stamp interaction**: Set `interaction.EncounterNumberAtCreation`, `interaction.InteractionIndexInEncounter`, `interaction.ExplicitnessLevelAtCreation` (from `session.LastResolvedIntensityLabel`)
5. **Keyword-based boundary detection**: Run `ContainsEncounterCompletionKeywords` on interaction content as a FREE heuristic to detect encounter boundaries in non-Climax phases. If matched:
   - Increment `GlobalEncounterCount`
   - Set `CurrentEncounterNumber = 0`
   - Set `interaction.WasEncounterBoundaryDetected = true`
   - Save state synchronously

   > **Guard**: The keyword path **MUST** skip `InteractionType.System` interactions. The multi-encounter time-skip overflow block (Phase 2 region) injects `Instruction` System interactions whose directive text contains completion-adjacent language ("Wrap up the current encounter naturally…"), which would falsely match `ContainsEncounterCompletionKeywords`. Use: `if (interaction.InteractionType != InteractionType.System) { … }` — mirroring the existing keyword hard-gate at line 4761.

The keyword path supplements (not replaces) the LLM path:
- **LLM path**: Drives time-skip injection in Climax/aftermath phases
- **Keyword path**: Drives global counter increment in ALL phases, no LLM cost

### Phase 9: Update Climax entry numbering

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (~line 4225)

Change from hardcoded `= 1` to `= v2State.GlobalEncounterCount + 1`:
```csharp
// Before: v2State.CurrentEncounterNumber = 1;
// After:  v2State.CurrentEncounterNumber = v2State.GlobalEncounterCount + 1;
```

### Phase 10: UI — Interaction Info modal

**File:** `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` (~lines 8160-8290)

Extend the Interaction Info modal with a collapsible **"Encounter Details"** section:
- **Session interaction #**: `SessionInteractionIndex`
- **Encounter #**: `EncounterNumberAtCreation`
- **Position in encounter**: `InteractionIndexInEncounter`
- **Explicitness level**: `ExplicitnessLevelAtCreation`
- (Existing) In Sex Scene: `WasInSexScene`
- (Existing) Encounter Boundary: `WasEncounterBoundaryDetected`

### Phase 11: Update contract docs

**File:** `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` (~lines 230-250)

Update `IsStateDirty` XML doc:
- Time-skip mutations now persist synchronously (not via dirty flag)
- Dirty flag still applies to: character stats, theme scores, non-time-skip phase transitions, location state

Add XML doc on `GlobalEncounterCount` noting it's the authoritative cumulative count.

### Phase 11b: RebuildAdaptiveStateInternalAsync convergence note

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (~line 2490)

`RebuildAdaptiveStateInternalAsync` creates a **fresh** `AdaptiveScenarioState` and replays all non-excluded interactions through `UpdateStateAndDetectEncounterAsync`. After B-057 Phase 8, the replay loop calls the universal encounter tracking logic for every interaction — including `ContainsEncounterCompletionKeywords` matching — so `GlobalEncounterCount` re-converges to the correct cumulative value automatically.

**No change needed.** This is a verification note: if `GlobalEncounterCount` diverges from expected after a rebuild, the bug is in the keyword-detection run during replay (not in persistence).

---

## Part C: Tests

### Phase 12: Comprehensive tests

**File:** `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs`

> **Existing test conflict**: `MultiEncounterTimeSkipTests.cs` already has tests at lines 149-217 that set and assert `CurrentEncounterNumber` with Climax-only semantics. Some of these may conflict with the universal repurposing. **Action**: review existing tests and either update their assertions to match universal semantics or replace them. Do NOT leave dead tests asserting Climax-only defaults like `CurrentEncounterNumber = 0` after universal tracking changes the lifecycle.

Part A tests:
1. `HydrateV2State_UnconditionalRestore_DoesNotOverwriteDetectionState`
2. `HydrateV2State_UnconditionalRestore_RestoresFromDB_WhenInMemoryIsDefault`
3. `TryDetectEncounterBoundaryAsync_SavesToDB_Synchronously`
4. `OverflowTimeSkipPhaseTransition_SavesToDB_Synchronously`
5. `FullTimeSkipCycle_PersistsEveryTransition`
6. `B057_SaveBeforeHydrateRemoved_DoesNotAffectStateConsistency`

Part B tests:
7. `UniversalEncounter_Starts_OnFirstSexualContent_InAnyPhase`
8. `UniversalEncounter_InteractionFields_AreStamped_Correctly`
9. `UniversalEncounter_GlobalCounter_Increments_OnBoundary`
10. `UniversalEncounter_KeywordBoundary_DetectsCompletion`
11. `UniversalEncounter_Climax_UsesGlobalCounter_ForNumbering`
12. `UniversalEncounter_MultiEncounter_StillWorks_InClimax`

---

## Relevant files

| File | What |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Part A Phases 1-5; Part B Phases 8-9 |
| `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` | Part B Phase 6 (new fields), Phase 11 (doc updates) |
| `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs` | Part B Phase 7 (new interaction fields) |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` | Part B Phase 10 (UI display) |
| `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` | Part C Phase 12 (12+ test methods) |

## Verification

1. **Build**: `dotnet build DreamGenClone.sln` — 0 errors
2. **Existing tests**: All pass (607+)
3. **New tests**: All Phase 12 tests pass
4. **Manual smoke test — Part A**: Create RP session with multi-encounter Climax theme, play through encounter boundary → verify `CurrentEncounterNumber` immediately updated in DB, overflow phases persist, hydrate restores correctly
5. **Manual smoke test — Part B**: Create RP session in BuildUp phase → first sexual content interaction → `EncounterNumberAtCreation = 1`, interactions stamped with encounter metadata, keyword completion increments `GlobalEncounterCount`, Interaction Info UI shows encounter details
6. **No regressions**: AftermathHusbandContrastTests, MultiEncounterTimeSkipTests, RolePlaySessionLifecycleTests all pass

## Scope boundaries

- **In scope**: Universal encounter tracking (any phase, any marker); Interaction-level metadata on `RolePlayInteraction` (4 new fields); Keyword-based completion detection; Global encounter counter; Sync persist for time-skip state; UI display updates
- **Not in scope**: LLM-based content classification per interaction (hands, oral, anal, orgasms, finishing moves) — deferred; Encounter summary auto-generation — covered by B-041; Finishing move matrix integration — covered by B-029
- **Not changing**: `AddInteractionAsync` / `ContinueAsync` call order; `CharacterSnapshots` deep-copy in `HydrateV2State`; `EnableAdaptiveStateUpdates` flag

---

## Downstream Consumer: B-058 (Per-Encounter Memory + Knowledge Gating)

B-058 depends on the following B-057 Part B fields existing before B-058 implementation begins:

| B-057 Field | B-058 Usage | Required? |
|---|---|---|
| `AdaptiveScenarioState.GlobalEncounterCount` | B-058 Phase 2.4 reads it to stamp `EncounterNumber` on `EncounterCompletion` rows | **Yes — B-057 must add this** |
| `AdaptiveScenarioState.CurrentEncounterNumber` (universal, not just Climax) | B-058 Phase 9 uses `GlobalEncounterCount + 1` for Climax entry numbering; Phase 6 knowledge gating reads `EncounterCompletions` by cycle | **Yes — B-057 must repurpose this** |
| `RolePlayInteraction.EncounterNumberAtCreation` | B-058 does not directly use, but cross-checks in Phase 12 tests | No — additive only |
| `RolePlayInteraction.InteractionIndexInEncounter` | B-058 does not use (uses list indices instead) | No — additive only |

**Implementation order rule**: B-057 Part B MUST complete before B-058 Phase 2 begins. B-057 Part A (sync persist) has no B-058 dependency and can ship independently.
