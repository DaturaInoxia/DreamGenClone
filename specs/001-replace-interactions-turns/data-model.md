# Data Model: Replace Interactions with Turns Throughout RP Engine and Data Model

**Date**: 2026-07-13
**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

This feature is a pure rename + one-way migration of existing stored values. No new entities, no new relationships, no new tables. The model behavior and cardinality of every affected entity is unchanged — only field names (and, on migration, the numeric scale of those fields) change.

For each entity below:
- **New field(s)**: The post-rename name (the "current" name after this feature ships).
- **Was**: The pre-rename name being replaced.
- **Type / Unit**: Unchanged by the rename; recorded for clarity.
- **Migration rule**: How the stored value is converted on the migration pass. `ceil(n / 3) = (n + 2) / 3` for non-negative integers.

---

## 1. AdaptiveScenarioState (V2)

**File**: `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`
**Role**: Per-session runtime state for phase progression, encounter counting, and scenario commitment.

| New Field | Was | Type / Unit | Migration Rule |
|-----------|-----|------------|----------------|
| `TurnCountInPhase` | `InteractionCountInPhase` | int, turns (was: interactions) | `UPDATE ... SET TurnCountInPhase = (InteractionCountInPhase + 2) / 3` |
| `TurnsSinceCommitment` | `InteractionsSinceCommitment` | int, turns | `UPDATE ... SET TurnsSinceCommitment = (InteractionsSinceCommitment + 2) / 3` |
| `TurnsInApproaching` | `InteractionsInApproaching` | int, turns | `UPDATE ... SET TurnsInApproaching = (InteractionsInApproaching + 2) / 3` |
| `TurnsInCurrentEncounter` | `InteractionsInCurrentEncounter` | int, turns | `UPDATE ... SET TurnsInCurrentEncounter = (InteractionsInCurrentEncounter + 2) / 3` |

**Validation rules**: Unchanged. All four fields MUST be `>= 0`. `TurnCountInPhase` resets to 0 on phase transition (was: reset of `InteractionCountInPhase`).

**State transitions**: Unchanged. The increment sites remain exactly the four `StartTurnAsync` call points in `RolePlayEngineService` (AddInteraction, Continue, SubmitPrompt, ContinueAs) — each increments `TurnCountInPhase` by exactly 1 per turn, matching the prior `InteractionCountInPhase` increment contract.

---

## 2. ThemeScoreState (V2)

**File**: `DreamGenClone.Domain/RolePlay/AdaptiveStateV2Records.cs`
**Role**: Per-theme scoring state within a session, including post-completion cooldown.

| New Field | Was | Type / Unit | Migration Rule |
|-----------|-----|------------|----------------|
| `CompletionCooldownTurns` | `CompletionCooldownInteractions` | int, turns | `UPDATE RolePlayV2ThemeScores SET CompletionCooldownTurns = (CompletionCooldownInteractions + 2) / 3` |

**Validation rules**: `>= 0`. Decremented by 1 per turn while positive (was: decrement of `CompletionCooldownInteractions`).

**State transitions**: `0 → set by theme completion event → decrement-by-1-per-turn → 0 (cooldown expired)`. Unchanged.

---

## 3. ScenarioHistoryEntry

**File**: `DreamGenClone.Domain/RolePlay/AdaptiveStateV2Records.cs`
**Role**: Record of a completed scenario within a session's scenario history.

| New Field | Was | Type / Unit | Migration Rule |
|-----------|-----|------------|----------------|
| `TurnCount` | `InteractionCount` | int, turns | `UPDATE RolePlayV2ScenarioHistory SET TurnCount = (InteractionCount + 2) / 3` |

**Validation rules**: `>= 0`. Set once when a scenario completes, frozen thereafter (immutable history entry).

---

## 4. EncounterSummaryRecord

**File**: `DreamGenClone.Domain/RolePlay/EncounterSummaryRecord.cs`
**Role**: Summary of an encounter within a phase.

| New Field | Was | Type / Unit | Migration Rule |
|-----------|-----|------------|----------------|
| `TurnCountInPhase` | `InteractionCountInPhase` | int, turns | `UPDATE RolePlayV2EncounterSummaries SET TurnCountInPhase = (InteractionCountInPhase + 2) / 3` |

**Unchanged (data-model concept — explicitly NOT renamed)**:
- `StartInteractionIndex`: int — index into `RolePlaySession.Interactions` list (timeline offset, not phase counter)
- `EndInteractionIndex`: int — index into `RolePlaySession.Interactions` list

**Validation rules**: `TurnCountInPhase >= 0`. `StartInteractionIndex <= EndInteractionIndex`.

---

## 5. LifecycleInputs (Contract)

**File**: `DreamGenClone.Application/RolePlay/RolePlayContracts.cs`
**Role**: Input DTO carrying session metrics into the scenario lifecycle evaluator.

| New Field | Was | Type / Unit | Migration Rule |
|-----------|-----|------------|----------------|
| `TurnsSinceCommitment` | `InteractionsSinceCommitment` | int, turns | In-memory only — populated by reading `AdaptiveScenarioState.TurnsSinceCommitment`; no DB migration of this field directly. The underlying source column migration is covered by §1. |

---

## 6. NarrativeGateMetricKeys (Constants)

**File**: `DreamGenClone.Domain/RolePlay/NarrativeGateProfile.cs`
**Role**: String constants used as keys in the gate-evaluation metric dictionary passed between engine services and the gate evaluator.

| New Constant | Was | Used By | Migration Rule |
|--------------|-----|---------|----------------|
| `TurnsSinceCommitment` | `InteractionsSinceCommitment` | ScenarioLifecycleService (write), ScenarioSelectionService (write), gate evaluator (read), RolePlayWorkspace.razor (gate evaluation helper) | String constant changes at compile time. Stored gate JSON does NOT reference this constant name — gate JSON uses `minimumTurns` (see §8). No DB string migration needed. |

---

## 7. TransitionTriggerType (Enum)

**File**: referenced from `ScenarioLifecycleService.cs` (enum lives in `DreamGenClone.Domain`).
**Role**: Enum classification of what triggered a phase transition.

| New Enum Value | Was | Migration Rule |
|----------------|-----|----------------|
| `TurnCountGate` | `InteractionCountGate` | Code-only enum rename. Enum values are not persisted as strings in the DB (transitions record `TriggerType` as a string label from a separate domain); this enum value affects only in-memory classification and logging. No DB migration. |

---

## 8. Theme Machine Gate Configuration JSON Blob

**Storage**: `RPThemeMachineTransitions.GateConfigJson` (TEXT column, SQLite).
**Role**: Per-transition gate configuration JSON; parsed at evaluation time by `ThemeMachineEvaluator`.

| New JSON Property | Was | Type | Migration Rule |
|-------------------|-----|------|----------------|
| `minimumTurns` | `minimumInteractions` | int, turns | For each row: parse JSON, read `minimumInteractions`, compute `minimumTurns = max(0, (minimumInteractions + 2) / 3)`, remove `minimumInteractions`, add `minimumTurns` with computed value, preserve all other properties, write blob back. |

**JSON schema after migration** (example — Climax → Reset rule):
```json
{
  "minimumTurns": 2,
  "requireReturnBeatCompleted": true,
  "returnBeatCompletionSignals": ["returned"],
  "returnBeatTransgressorRole": "Wife",
  "returnBeatPartnerRole": "Husband"
}
```

**Validation rules** (post-rename, in `RPThemeService`):
- `minimumTurns` MUST be present and an integer `>= 0`.
- During the transition window, `minimumInteractions` is still accepted as a fallback (only on read, divided by 3 with ceiling) for un-migrated rows.
- New transitions and edits to existing transitions always write `minimumTurns`, never `minimumInteractions`.

---

## 9. StoryAnalysisOptions (Configuration)

**File**: `DreamGenClone.Infrastructure/Configuration/StoryAnalysisOptions.cs`
**Role**: Strongly-typed `appsettings.json` binding for phase-pacing configuration.

| New Key | Was | Type | Default Conversion |
|---------|-----|------|---------------------|
| `AdaptiveEarlyTurnThreshold` | `AdaptiveEarlyTurnInteractionThreshold` | int | Code default divided by 3 (ceiling) |
| `AdaptivePerTurnTotalDeltaBudget` | `AdaptivePerInteractionTotalDeltaBudget` | double/decimal (verify) | Numeric value divided by 3 (ceiling) — see note |
| `CompletedScenarioThemeCooldownTurns` | `CompletedScenarioThemeCooldownInteractions` | int | Code default divided by 3 (ceiling) |
| `BuildUpMinTurnsBeforeCommit` | `BuildUpMinInteractionsBeforeCommit` | int | Code default divided by 3 (ceiling) |

**Note on `AdaptivePerTurnTotalDeltaBudget`**: This is a budget *per turn*, scaling from a *per-interaction* value. The per-turn budget SHOULD be the per-interaction budget × ~3 (turn ≈ 3 interactions). Conversion rule: `new = old * 3` — NOT divided by 3. Confirm the exact semantics during implementation by reading the consuming service. This is the only config option where the multiplication direction is inverted; flag it explicitly in `tasks.md`.

**`appsettings.json` deployment files**: NOT programmatically migrated. Renamed keys become live when the code with new bindings ships. Any deployment `appsettings.json` containing the old keys silently stops binding (these keys are optional with code defaults). The `quickstart.md` documents this.

---

## 10. Out-of-Scope Entities (Explicitly NOT Modified)

These reference the canonical `RolePlayInteraction` timeline (individual AI messages), NOT phase-advancement counting. They MUST remain unchanged:

- `RolePlayInteraction` — session timeline message entity
- `RolePlaySession.Interactions` — list property (timeline)
- `InteractionId` — identifier column on multiple tables
- `InteractionType` — enum (values `System`, `Npc`, `User`, `Custom`)
- `IInteractionCommandService` / `InteractionCommandService` / `IInteractionRetryService`
- `ISemanticInteractionAnalysisRepository` / `SemanticInteractionAnalysisJobHandler`
- `SemanticInteractionAnalysisState`
- `DecisionTrigger.InteractionStart` — trigger enum value
- `InteractionEvidenceSignal` — keyword-hit accumulator on `AdaptiveStateV2` (NOT a turn counter)
- `PinnedInteractionCount`, `OutputInteractionCount`, `OutputInteractionIdsJson` — turn-persistence table columns referencing actual interaction rows
- `EncounterSummaryRecord.StartInteractionIndex` / `EndInteractionIndex` — indices into `Interactions` list
- UI render-tracking variables (`_lastRenderedInteractionCount`) and CSS classes (`rw-interaction`, `rw-interaction-pending`, `rw-interaction-body`)

---

## 11. Migration Ordering & Idempotency

**Order**:
1. `ALTER TABLE ... RENAME COLUMN` for all 6 columns (guarded by old-column-exists + new-column-absent checks).
2. `UPDATE ... SET new_col = (old_col + 2) / 3` for each renamed numeric column (post-rename; `old_col` is no longer accessible, so execute this UPDATE *immediately after each rename* using the *new* column name and a temporary holding expression).
3. JSON blob rewrite pass across `RPThemeMachineTransitions` for `minimumInteractions → minimumTurns`.
4. Verify `RPThemeProfiles.ThemeSelectionTurnsPerTheme` already exists (pre-migrated in a prior feature); no-op.

**Idempotency mechanism**: Each step checks the current state before mutating:
- Column rename: skip if old column missing OR new column already exists.
- Numeric UPDATE: skip if a migration marker row exists in a migrations table (or use SQLite `PRAGMA user_version` bump). The exact mechanism is left to implementation, but the marker MUST be set atomically with the conversion.
- JSON rewrite: skip rows whose blob already contains `"minimumTurns"` and does not contain `"minimumInteractions"`.

**Rollback**: Out of scope. Migration is one-way. Backups of `dreamgenclone.dev.db` are the user's responsibility (the app is local-first). Document this in `quickstart.md`.

---

## 12. Entity Relationship Summary

No relationships change. Every entity listed in this data-model document retains its existing cardinality, foreign keys, and parent-child relationships. The rename affects only field names within entities and JSON property names within blobs.

---

## Validation Test Coverage

The following existing tests cover the renamed fields and migration:
- `AdaptiveScenarioStateV2RoundTripTests.cs` — V2 state persistence round-trip (will assert `Turn*` fields post-rename; values updated to reflect migration if the test data uses pre-migration units).
- `DecisionPointMutationTests.cs` — cadence gate using `TurnCountInPhase`.
- `EncounterSummaryServiceTests.cs` — encounter summary `TurnCountInPhase`.
- `PhaseLifecycleTransitionTests.cs` — lifecycle inputs `TurnsSinceCommitment`.
- `RolePlaySessionLifecycleTests.cs` — gate JSON uses `minimumTurns`; expected values adjusted for ÷3.
- `RolePlayThemeMachineCommandTests.cs` — gate JSON uses `minimumTurns`.
- `ThemeMachineEvaluatorTests.cs` — gate JSON uses `minimumTurns`.
- `RPThemeMachineDefinitionValidationTests.cs` — gate config validation messages reference `minimumTurns`.
- `ScenarioStateModelTests.cs` — legacy V1 `ScenarioMetadata.TurnCount`.

New tests (added during implementation):
- Migration test: pre-migration row → post-migration row (column rename + ÷3 conversion + JSON blob rewrite) — must be deterministic and idempotent.