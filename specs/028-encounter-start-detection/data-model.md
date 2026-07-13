# Data Model: Semantic Encounter-Start Detection & Memory Enrichment

**Feature**: 028-encounter-start-detection  
**Date**: 2026-07-08

## Overview

No new tables or schema migrations. One new property on an existing entity, and one new configuration option.

---

## Entity Changes

### RolePlayInteraction (Modified)

**File**: `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs`  
**Namespace**: `DreamGenClone.Web.Domain.RolePlay`

**New property**:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `WasEncounterStart` | `bool` | `false` | Set to `true` on the interaction where semantic inference detected an encounter-start. Semantically distinct from `WasInSexScene` (which is set on every interaction with sexual keyword content). |

**Relationship to existing properties**:

| Property | When set | Meaning |
|----------|----------|---------|
| `WasInSexScene` | Every interaction with sexual keywords | "This interaction had sexual content" |
| `WasEncounterBoundaryDetected` | Last interaction of encounter | "Encounter ended here" |
| `WasEncounterStart` (NEW) | First interaction of encounter | "Encounter began here" |

**State transitions**:
```
Non-sexual interaction → [sexual keywords] → WasInSexScene = true
WasInSexScene + semantic detection → WasEncounterStart = true, encounter tracking begins
Encounter active → [boundary detection] → WasEncounterBoundaryDetected = true
```

---

### EncounterSummaryRecord (Unchanged)

**File**: `DreamGenClone.Web/Application/RolePlay/EncounterSummaryService.cs`  

No structural changes. The enrichment prompt now uses:
- `record.CharacterId` directly (was previously mislabeled as `displayName` via `DetectionEvidence`)
- `session.AdaptiveState.CharacterStats[record.CharacterId].CharacterRole` for role context
- Uses `StartInteractionIndex`/`EndInteractionIndex` from the record (unchanged — correctness depends on Part C/D bug fixes)

---

### AdaptiveScenarioState / V2State (Unchanged structure, bug-fixed behavior)

No new fields. Existing fields with corrected behavior:

| Field | Bug | Fix |
|-------|-----|-----|
| `CurrentEncounterStartInteractionIndex` | Not reset after boundary (Part D) | Reset to 0 after boundary processing |
| `CurrentEncounterStartInteractionIndex` | Clobbered by Climax entry (Part C) | Gated on `== 0` before Climax-entry overwrite |
| `InteractionsInCurrentEncounter` | Correctly signals "in encounter" | Used as re-entry guard (was `CurrentEncounterNumber`) |

---

## Configuration

### RolePlayMemoryOptions (Modified)

**File**: `DreamGenClone.Infrastructure/Configuration/RolePlayMemoryOptions.cs`  
**Namespace**: `DreamGenClone.Infrastructure.Configuration`

**New property**:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EncounterStartConfidenceThreshold` | `decimal` | `0.70` | Global confidence threshold for `encounter-started` semantic detection. Applied universally across all themes. Configurable via `appsettings.json` under `RolePlayMemory.EncounterStartConfidenceThreshold`. |

---

## Validation Rules

1. **`WasEncounterStart`**: Only one interaction per encounter has this set to `true`. Enforced by the re-entry guard (`InteractionsInCurrentEncounter > 0`).
2. **Confidence threshold**: Must be in range (0.0, 1.0]. Validated at option binding time by .NET configuration.
3. **Character role resolution**: Falls back to `"Unknown"` if `CharacterStats` doesn't contain the character — no crash.

---

## Persistence

All data persisted via existing EF Core SQLite mappings:
- `RolePlayInteraction.WasEncounterStart` → new column in `RolePlayInteractions` table (EF Core migration or EnsureCreated adds it)
- No new tables
- No data migration needed (default `false` for existing rows)
