# Contracts: Replace Interactions with Turns Throughout RP Engine and Data Model

**Date**: 2026-07-13
**Spec**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md)

This feature is a rename + migration; it introduces no new external interfaces. It changes the *shape* of internal contracts (renamed fields/properties) and the *JSON property name* of one gate-config property in the theme machine. The contracts below document the post-rename interface — the shape callers see and produce after the feature ships.

For each contract: **Before** records the pre-rename shape for migration planning; **After** records the canonical contract going forward.

---

## Contract 1: LifecycleInputs (Application → Infrastructure)

**File**: `DreamGenClone.Application/RolePlay/RolePlayContracts.cs` → `LifecycleInputs` record/struct.

**Purpose**: Carries session metrics into the scenario lifecycle evaluator (`ScenarioLifecycleService`).

### Before
```csharp
public sealed record LifecycleInputs
{
    public int InteractionsSinceCommitment { get; init; }
    // ... other inputs
}
```

### After
```csharp
public sealed record LifecycleInputs
{
    public int TurnsSinceCommitment { get; init; }
    // ... other inputs
}
```

**Unit**: turns (integer). MUST be `>= 0`.
**Source**: populated by `RolePlayEngineService` from `AdaptiveScenarioState.TurnsSinceCommitment`.

---

## Contract 2: Gate Evaluation Metric Dictionary (Domain)

**File**: `DreamGenClone.Domain/RolePlay/NarrativeGateProfile.cs` → `NarrativeGateMetricKeys`.

**Purpose**: String keys used by the engine to populate and the gate evaluator to read the metric dictionary.

### Before
```csharp
public static class NarrativeGateMetricKeys
{
    public const string InteractionsSinceCommitment = "InteractionsSinceCommitment";
    // ...
}
```

### After
```csharp
public static class NarrativeGateMetricKeys
{
    public const string TurnsSinceCommitment = "TurnsSinceCommitment";
    // ...
}
```

**Migration impact**: The dictionary key string changes. Any code reading the dictionary with the old literal string key MUST be updated to use the new constant. No DB migration — this key is not persisted as a string in gate JSON; gate JSON uses `minimumTurns` (see Contract 3).

---

## Contract 3: Theme Machine Gate Configuration JSON

**Storage**: `RPThemeMachineTransitions.GateConfigJson` column (TEXT).
**Producer**: `RPThemeService` (writes JSON when themes are created/edited).
**Consumer**: `ThemeMachineEvaluator` (parses JSON to evaluate cooldown transitions).

### Before
```json
{
  "minimumInteractions": 9,
  "requireReturnBeatCompleted": true,
  "returnBeatCompletionSignals": ["returned"],
  "returnBeatTransgressorRole": "Wife",
  "returnBeatPartnerRole": "Husband"
}
```

### After
```json
{
  "minimumTurns": 3,
  "requireReturnBeatCompleted": true,
  "returnBeatCompletionSignals": ["returned"],
  "returnBeatTransgressorRole": "Wife",
  "returnBeatPartnerRole": "Husband"
}
```

**Rules**:
- The `minimumInteractions` JSON property is renamed `minimumTurns`.
- The numeric value is divided by 3 with ceiling rounding on the migration pass. Post-migration, the value represents turns.
- All sibling properties (`requireReturnBeatCompleted`, `returnBeatCompletionSignals`, `returnBeatTransgressorRole`, `returnBeatPartnerRole`, any future additions) are preserved verbatim.
- `minimumTurns` MUST be present, an integer, and `>= 0` (validated by `RPThemeService`).
- **Backward-compatibility read**: `ThemeMachineEvaluator` and `RPThemeService` validation MUST accept `minimumInteractions` as a fallback for un-migrated rows, dividing its value by 3 (ceiling) at read time. This is the only permitted runtime interaction-to-turn conversion and only on the legacy-read path.
- **Write contract**: New themes and edits to existing themes always write `minimumTurns`, never `minimumInteractions`.

---

## Contract 4: AdaptiveScenarioState (V2) — Domain Persistence Shape

**File**: `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`.
**Persistence**: `RolePlayStateRepository` ↔ `RolePlayV2AdaptiveStates` table.

### Before
```csharp
public sealed class AdaptiveScenarioState
{
    public int InteractionCountInPhase { get; set; }
    public int InteractionsSinceCommitment { get; set; }
    public int InteractionsInApproaching { get; set; }
    public int InteractionsInCurrentEncounter { get; set; }
    // ... other fields unchanged
}
```

### After
```csharp
public sealed class AdaptiveScenarioState
{
    public int TurnCountInPhase { get; set; }
    public int TurnsSinceCommitment { get; set; }
    public int TurnsInApproaching { get; set; }
    public int TurnsInCurrentEncounter { get; set; }
    // ... other fields unchanged
}
```

**Invariants**:
- All four fields `>= 0`.
- `TurnCountInPhase` reset to 0 on every phase transition (four sites in `RolePlayEngineService`).
- `TurnsSinceCommitment` reset to 0 on scenario commit (`RolePlayAdaptiveStateService`).
- `TurnsInApproaching` reset to 0 on commit (`RolePlayAdaptiveStateService`).
- `TurnsInCurrentEncounter` reset on encounter boundary.
- Increment by exactly 1 per turn (per `StartTurnAsync` call) — interaction counts are NEVER fed into these fields.

---

## Contract 5: SQLite Schema (Post-Migration)

**File**: `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`.

### After — `RolePlayV2AdaptiveStates` columns renamed:
- `InteractionCountInPhase` → `TurnCountInPhase`
- `InteractionsSinceCommitment` → `TurnsSinceCommitment`
- `InteractionsInApproaching` → `TurnsInApproaching`

### After — `RolePlayV2ThemeScores` column renamed:
- `CompletionCooldownInteractions` → `CompletionCooldownTurns`

### After — `RolePlayV2ScenarioHistory` column renamed:
- `InteractionCount` → `TurnCount`

### After — `RolePlayV2EncounterSummaries` column renamed:
- `InteractionCountInPhase` → `TurnCountInPhase`

### Value migration:
- Every value in every renamed column is divided by 3 with ceiling rounding (`(value + 2) / 3`).
- `RPThemeProfiles.ThemeSelectionTurnsPerTheme` already exists (prior migration) — not modified.

### Idempotency:
- Each `ALTER TABLE ... RENAME COLUMN` is guarded by an old-name-exists + new-name-absent check.
- Numeric `UPDATE` is guarded by a migration marker (SQLite `PRAGMA user_version` bump or a `__migrations` table row), set atomically with the conversion.
- `GateConfigJson` rewrite skips rows already containing `"minimumTurns"` and not containing `"minimumInteractions"`.

---

## Contract 6: TransitionTriggerType (Enum)

**File**: `DreamGenClone.Domain/RolePlay/` (enum declaration).

### Before
```csharp
public enum TransitionTriggerType
{
    InteractionCountGate,
    // ...
}
```

### After
```csharp
public enum TransitionTriggerType
{
    TurnCountGate,
    // ...
}
```

**Persistence impact**: This enum value is not persisted as a string in a column used for phase decisions (referenced only in-memory by `ScenarioLifecycleService` for classification). No DB migration required. If any log message stringifies this enum value, the log text will read `TurnCountGate` post-rename.

---

## Contract 7: StoryAnalysisOptions (`appsettings.json` binding)

**File**: `DreamGenClone.Infrastructure/Configuration/StoryAnalysisOptions.cs`.

### Before
```json
{
  "StoryAnalysis": {
    "Adaptive": {
      "AdaptiveEarlyTurnInteractionThreshold": 9,
      "AdaptivePerInteractionTotalDeltaBudget": 0.5,
      "CompletedScenarioThemeCooldownInteractions": 12,
      "BuildUpMinInteractionsBeforeCommit": 6
    }
  }
}
```

### After
```json
{
  "StoryAnalysis": {
    "Adaptive": {
      "AdaptiveEarlyTurnThreshold": 3,
      "AdaptivePerTurnTotalDeltaBudget": 1.5,
      "CompletedScenarioThemeCooldownTurns": 4,
      "BuildUpMinTurnsBeforeCommit": 2
    }
  }
}
```

**Conversion rules** (see research.md R7 and data-model.md §9):
- Integer thresholds: divided by 3 with ceiling (e.g., 9 → 3, 12 → 4, 6 → 2).
- `AdaptivePerTurnTotalDeltaBudget`: multiplied by 3 (it is a budget *per turn*; the prior value was *per interaction*; one turn ≈ 3 interactions, so the per-turn budget is ~3× the per-interaction budget). Final conversion to be verified against the consuming service in `tasks.md` / implementation.

**Deployment files**: NOT programmatically migrated. Renamed keys become live when the code ships. Old keys silently stop binding (these keys are optional with code defaults).

---

## Out-of-Scope Contracts (Explicitly NOT Modified)

These contracts reference `RolePlayInteraction` (the session timeline message entity) and MUST remain unchanged:

- `RolePlayInteraction` entity schema and its `Interactions` list relationship to `RolePlaySession`
- `InteractionType` enum and its values
- `InteractionId` as a foreign key / identifier
- `IInteractionCommandService` / `IInteractionRetryService` interfaces
- `SemanticInteractionAnalysisJobHandler` / `ISemanticInteractionAnalysisRepository`
- `DecisionTrigger.InteractionStart` enum value
- `InteractionEvidenceSignal` field semantic (keyword-hit accumulator; not a turn counter)
- `EncounterSummaryRecord.StartInteractionIndex` / `EndInteractionIndex` (timeline indices, not phase counters)
- `PinnedInteractionCount` / `OutputInteractionCount` / `OutputInteractionIdsJson` (turn-persistence table columns referencing actual interaction rows)

---

## Test Surface

The contracts above are verified by these existing test classes (updated during implementation):
- `AdaptiveScenarioStateV2RoundTripTests` — Contract 4 (persistence round-trip)
- `PhaseLifecycleTransitionTests` — Contract 1 (LifecycleInputs)
- `ThemeMachineEvaluatorTests` — Contract 3 (gate JSON)
- `RolePlaySessionLifecycleTests` — Contract 3 (gate JSON end-to-end)
- `RolePlayThemeMachineCommandTests` — Contract 3 (gate JSON via command flow)
- `RPThemeMachineDefinitionValidationTests` — Contract 3 (validation messages)
- `DecisionPointMutationTests` — Contract 4 (cadence gate using `TurnCountInPhase`)
- `EncounterSummaryServiceTests` — Contract 4 (encounter summary `TurnCountInPhase`)
- `ScenarioStateModelTests` — Contract 4 (legacy `ScenarioMetadata.TurnCount`)

New tests added during implementation:
- A migration test: verify column rename + ÷3 value conversion + JSON blob rewrite on a synthetic pre-migration DB. Assert idempotency.