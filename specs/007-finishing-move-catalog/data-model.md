# Data Model: Finishing Move System – Catalog and Matrix Redesign

**Branch**: `007-finishing-move-catalog`  
**Phase**: 1 – Design

---

## Modified Entity: RPFinishingMoveMatrixRow

**File**: `DreamGenClone.Domain/RolePlay/RPThemeModels.cs`

| Field | Type | Notes |
|-------|------|-------|
| Id | string (GUID) | PK |
| ~~DominanceBand~~ → **OtherManDominanceBand** | string | Renamed. Values: `0-29`, `30-59`, `60-100` |
| DesireBand | string | Values updated: `0-29`, `30-59`, `60-100` (was `0-49`, `50-74`, `75-100`) |
| SelfRespectBand | string | Unchanged (`0-29`, `30-59`, `60-100`) |
| PrimaryLocations | List\<string\> | Curator override list — kept as-is |
| SecondaryLocations | List\<string\> | Curator override list — kept as-is |
| ExcludedLocations | List\<string\> | Curator override list — kept as-is |
| WifeReceptivity | string | Unchanged |
| WifeBehaviorModifier | string | Unchanged |
| OtherManBehaviorModifier | string | Unchanged |
| TransitionInstruction | string | Unchanged |
| SortOrder | int | Unchanged |
| IsEnabled | bool | Unchanged |
| CreatedUtc / UpdatedUtc | DateTime | Unchanged |

**UNIQUE constraint**: `(DesireBand, SelfRespectBand, OtherManDominanceBand)`

---

## New Entity: RPFinishLocation

**Table**: `RPFinishLocations`  
**File**: `DreamGenClone.Domain/RolePlay/RPThemeModels.cs`

| Field | Type | Notes |
|-------|------|-------|
| Id | string (GUID) | PK |
| Name | string | e.g., `"In Mouth"`, `"Facial Open Mouth"` |
| Description | string | Plain language description for curator |
| Category | string | One of: `Internal`, `External`, `Facial`, `OnBody`, `Withdrawal` |
| EligibleDesireBands | string | Comma-separated band keys; empty = any |
| EligibleSelfRespectBands | string | Comma-separated band keys; empty = any |
| EligibleOtherManDominanceBands | string | Comma-separated band keys; empty = any |
| SortOrder | int | Display order |
| IsEnabled | bool | When false: excluded from prompt and greyed in UI |
| CreatedUtc / UpdatedUtc | DateTime | Audit timestamps |

---

## New Entity: RPFinishFacialType

**Table**: `RPFinishFacialTypes`  
**File**: `DreamGenClone.Domain/RolePlay/RPThemeModels.cs`

| Field | Type | Notes |
|-------|------|-------|
| Id | string (GUID) | PK |
| Name | string | e.g., `"Open Mouth"`, `"Eyes Closed"` |
| Description | string | Curator-facing description |
| PhysicalCues | string | Narrative cue text for the LLM |
| EligibleDesireBands | string | Comma-separated band keys; empty = any |
| EligibleSelfRespectBands | string | Comma-separated band keys; empty = any |
| EligibleOtherManDominanceBands | string | Comma-separated band keys; empty = any |
| SortOrder | int | Display order |
| IsEnabled | bool | |
| CreatedUtc / UpdatedUtc | DateTime | |

**Note**: This section is only included in the assembled prompt when at least one eligible `RPFinishLocation` in the same prompt has `Category = Facial`.

---

## New Entity: RPFinishReceptivityLevel

**Table**: `RPFinishReceptivityLevels`  
**File**: `DreamGenClone.Domain/RolePlay/RPThemeModels.cs`

| Field | Type | Notes |
|-------|------|-------|
| Id | string (GUID) | PK |
| Name | string | One of the 8 canonical names: Begging, Enthusiastic, Eager, Accepting, Tolerating, Reluctant, CumDodging, Enduring |
| Description | string | Curator-facing description of state |
| PhysicalCues | string | Body language, expression cues for LLM |
| NarrativeCue | string | Short guidance for narrative tone |
| EligibleDesireBands | string | Comma-separated; empty = any |
| EligibleSelfRespectBands | string | Comma-separated; empty = any |
| SortOrder | int | Controls display order (0 = Begging, 7 = Enduring) |
| IsEnabled | bool | |
| CreatedUtc / UpdatedUtc | DateTime | |

**Note**: No OtherManDominance column — receptivity is driven by the wife's Desire and SelfRespect, not the other man's dominance.

---

## New Entity: RPFinishHisControlLevel

**Table**: `RPFinishHisControlLevels`  
**File**: `DreamGenClone.Domain/RolePlay/RPThemeModels.cs`

| Field | Type | Notes |
|-------|------|-------|
| Id | string (GUID) | PK |
| Name | string | One of 3 canonical names: Asks, Leads, Commands |
| Description | string | Curator-facing description |
| ExampleDialogue | string | One or two example lines for LLM guidance |
| EligibleOtherManDominanceBands | string | Comma-separated; empty = any |
| SortOrder | int | 0 = Asks, 1 = Leads, 2 = Commands |
| IsEnabled | bool | |
| CreatedUtc / UpdatedUtc | DateTime | |

**Note**: No Desire or SelfRespect columns — control level is purely a function of the other man's dominance.

---

## New Entity: RPFinishTransitionAction

**Table**: `RPFinishTransitionActions`  
**File**: `DreamGenClone.Domain/RolePlay/RPThemeModels.cs`

| Field | Type | Notes |
|-------|------|-------|
| Id | string (GUID) | PK |
| Name | string | Short label, e.g., `"Verbal command"`, `"Holds in place"` |
| Description | string | Curator-facing description |
| TransitionText | string | Text/guidance injected into LLM prompt |
| EligibleDesireBands | string | Comma-separated; empty = any |
| EligibleSelfRespectBands | string | Comma-separated; empty = any |
| EligibleOtherManDominanceBands | string | Comma-separated; empty = any |
| SortOrder | int | Display order |
| IsEnabled | bool | |
| CreatedUtc / UpdatedUtc | DateTime | |

---

## Schema Migration

**Name**: `MigrateFinishingMoveMatrixToV2Async`  
**Location**: `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs`  
**Trigger**: Called at top of `EnsureSupplementalTablesAsync`, before `CREATE TABLE IF NOT EXISTS` statements

| Step | Action |
|------|--------|
| 1 | Detect `DominanceBand` column on `RPFinishingMoveMatrixRows` via `TableHasColumnAsync` |
| 2 | If not found → migration already applied or table doesn't exist yet → skip |
| 3 | If found → archive existing rows into `RPFinishingMoveMatrixRows_Archived_v2` (preserving all fields) |
| 4 | DROP TABLE `RPFinishingMoveMatrixRows` and its indexes |
| 5 | Recreation handled by the subsequent `CREATE TABLE IF NOT EXISTS` block with `OtherManDominanceBand` and updated UNIQUE constraint |
| 6 | Seed service repopulates from scratch with new band ranges |

**Idempotency**: Migration checks for the `DominanceBand` column. If the column is absent (new schema already in place), migration is skipped. Safe to run on every startup.

---

## Band Ranges Summary

| Tier | Low | Mid | High |
|------|-----|-----|------|
| Band key | `0-29` | `30-59` | `60-100` |
| Applies to | Desire, SelfRespect, OtherManDominance | same | same |

All three stats use the same three-tier keys. Empty eligibility field = eligible for all tiers.

---

## State Transitions (not applicable)

Catalog entities are reference data with no state machine transitions. Lifecycle is: created → enabled → disabled. No phase transitions.

---

## Validation Rules

| Entity | Rule |
|--------|------|
| All entities | `Name` must not be empty |
| All entities | Band eligibility fields: if non-empty, each comma-separated part must be a recognized band key (`0-29`, `30-59`, `60-100`) or empty |
| RPFinishLocation | `Category` must be one of `{Internal, External, Facial, OnBody, Withdrawal}` |
| RPFinishReceptivityLevel | Exactly 8 seed entries: `{Begging, Enthusiastic, Eager, Accepting, Tolerating, Reluctant, CumDodging, Enduring}` |
| RPFinishHisControlLevel | Exactly 3 seed entries: `{Asks, Leads, Commands}` |
| RPFinishingMoveMatrixRow | `DesireBand`, `SelfRespectBand`, `OtherManDominanceBand` all required (non-empty); `UNIQUE (DesireBand, SelfRespectBand, OtherManDominanceBand)` |
