# Data Model: Fix Climax Time-Skip System

## Entities

No new entities. No schema changes.

## Existing Fields Used

### `RolePlayInteraction.GeneratedByCommand` (already persisted)

- **Type**: `string?` (nullable)
- **Purpose**: Identifies the engine command that created the interaction
- **User-typed Instructions**: `null` (user-authored)
- **Engine time-skip**: `"MultiEncounterTimeSkip"` (new value, same field)
- **Other engine commands**: `"Continue"`, `"Narrative"`, `"Retry"`, etc.
- **Persistence**: Already stored in SQLite via `RolePlayStateRepository`

### `AdaptiveScenarioState.TimeSkipPending` (already persisted)

- **Type**: `bool`
- **Purpose**: Flag set when encounter boundary detection fires; cleared when time-skip directive is injected
- **New behavior**: Remains true across turns if injection is skipped due to user Instruction (retry semantics)

### `AdaptiveScenarioState.CurrentEncounterNumber` (already persisted)

- **Type**: `int`
- **Purpose**: Tracks the current encounter number within a multi-encounter Climax
- **Gate**: Time-skip only fires when `> 1` (not on encounter 1 initialization)

### `AdaptiveScenarioState.InteractionsInCurrentEncounter` (already persisted)

- **Type**: `int`
- **Purpose**: Counts interactions in the current encounter
- **Gate**: Time-skip block checks `== 0` (counter reset by boundary detection)

## Validation Rules

- `GeneratedByCommand` is null for user-authored interactions
- `GeneratedByCommand = "MultiEncounterTimeSkip"` for engine time-skip directives
- `TimeSkipPending` must be true for injection to fire
- `CurrentEncounterNumber` must be > 1 for injection to fire
- `CurrentPhase` must be `Climax` for injection to fire

## State Transitions

```text
[Encounter boundary detected]
  → CurrentEncounterNumber++
  → InteractionsInCurrentEncounter = 0
  → TimeSkipPending = true
  → CharacterEncounterStates.Clear()

[Next overflow pass]
  → Check TimeSkipPending
  → Check CurrentEncounterNumber > 1
  → Check no user Instruction in last 3 interactions
  → IF all pass:
      → First actor gets PromptIntent.Instruction with directive
      → TimeSkipPending = false
  → IF user Instruction found:
      → Skip injection
      → TimeSkipPending remains true (retry next turn)
```
