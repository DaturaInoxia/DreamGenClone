# Data Model: RP Session Opening Period

**Feature**: `001-opening-period`  
**Date**: 2026-06-22

## Entities

### Scenarios (modified)

**Table**: `Scenarios`  
**Purpose**: Holds scenario definitions. New column for opening-period guidance text.

| Column | Type | Change | Notes |
|--------|------|--------|-------|
| `Id` | TEXT PK | existing | Scenario identifier (GUID) |
| `Name` | TEXT | existing | Human-readable name |
| `PayloadJson` | TEXT | existing | Full scenario payload (JSON blob) |
| `OpeningGuidanceText` | TEXT NULLABLE | **new** | Opening-period guidance text for this scenario. When NULL, the engine uses the seeded default. |
| `UpdatedUtc` | TEXT | existing | Last update timestamp |

**Migration SQL**:
```sql
ALTER TABLE Scenarios ADD COLUMN OpeningGuidanceText TEXT;

UPDATE Scenarios SET OpeningGuidanceText = 'Focus on the couple''s relationship and their current life together. Include a brief sense of their intimate life from her point of view — the rhythm of it, what she feels about it, what she wants or doesn''t get — grounding these details in the character profiles and their descriptions. Describe their routines, interactions, and daily rhythms. Establish the setting, mood, and any relevant history. Other characters remain in the background.';
```

### Opening Period (in-memory, no new entity)

The opening period is a **session lifecycle stage**, not a persistent entity. It is derived from an in-memory comparison:

```
IsInOpeningPeriod = session.AdaptiveState.ObservedTurnCount <= 3
                   && session is newly created (not a Reset→BuildUp cycle)
```

**State**: Not persisted separately. The gate is checked at prompt-building time using `ObservedTurnCount` (which IS persisted in `RolePlayV2AdaptiveStates`).

**Lifecycle**:
1. Session created → `ObservedTurnCount = 0`
2. First `StartTurnAsync` → `ObservedTurnCount = 1` → opening period begins
3. Each subsequent turn → `ObservedTurnCount` increments
4. After turn 3 → `ObservedTurnCount = 4` → opening period ends, never re-enters

### ObservedTurnCount (existing, no changes)

**Table**: `RolePlayV2AdaptiveStates`  
**Column**: `ObservedTurnCount` (INTEGER, existing)

Incremented at four turn-start sites:
- `RolePlayEngineService.cs:823` (AddInteraction)
- `RolePlayEngineService.cs:923` (Continue)
- `RolePlayEngineService.cs:1067` (SubmitPrompt)
- `RolePlayEngineService.cs:1417` (ContinueAs)

**No changes needed** to this column or its increment sites. The opening period gate simply reads its value.

## Relationships

```
Session (1) ── (1) AdaptiveState ── ObservedTurnCount
                                        │
                                        ▼
                              Opening Period Gate
                              (ObservedTurnCount <= 3)
                                        │
                          ┌─────────────┴─────────────┐
                          ▼                           ▼
                    Opening Period              Post-Opening
                    (turns 1-3)                (turn 4+)
                          │                           │
                    Opening Guidance          Theme Guidance
                    (from Scenarios.         (from RPThemePhaseGuidance,
                     OpeningGuidanceText)     BuildFramingGuards, etc.)

Scenario (1) ── (1) OpeningGuidanceText
```

## State Transitions

```
[NEW SESSION]
    │
    ▼
ObservedTurnCount = 0
    │
    │ StartTurnAsync
    ▼
ObservedTurnCount = 1 ──→ OPENING PERIOD ACTIVE
    │                         • Suppress theme guidance
    │ StartTurnAsync           • Inject OpeningGuidanceText
    ▼                         • Exclude OtherMan
ObservedTurnCount = 2
    │
    │ StartTurnAsync
    ▼
ObservedTurnCount = 3
    │
    │ StartTurnAsync
    ▼
ObservedTurnCount = 4 ──→ OPENING PERIOD ENDS
    │                         • Resume theme guidance
    │                         • OtherMan eligible
    │ StartTurnAsync           • Observer window may begin (if applicable)
    ▼
ObservedTurnCount = 5 ...
```

## RP Engine Strict Config Contract

- **Threshold**: `private const int OpeningPeriodTurnCount = 3` — fixed architectural constant, not a user-tunable behavior control. Does not violate the "no hardcoded defaults" rule.
- **Guidance text**: Stored in `Scenarios.OpeningGuidanceText` — configurable data. Satisfies "UI-backed configuration" (seeded via migration, editable in future UI).
- **No fallback branches**: The gate is a single `if (ObservedTurnCount <= OpeningPeriodTurnCount)` condition. No alternate paths, no defaults, no guessing.
- **One resolution path**: The opening period gate is in one place (prompt-building method). OtherMan exclusion is in one place (actor resolution method). No duplicated logic.
