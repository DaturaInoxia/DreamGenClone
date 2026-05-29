# Data Model: Semantic Analysis — Dedicated Model & Concurrent Processing

**Branch**: `001-semantic-dedicated-model`  
**Date**: 2026-05-28

---

## Modified Entities

### AppFunction (enum) — DreamGenClone.Domain/ModelManager/AppFunction.cs

Add one new value at the end of the existing enum.

| Value | Name | Description |
|---|---|---|
| 0 | `RolePlayGeneration` | Live RP text continuation (existing) |
| 1 | `StoryModeGeneration` | Story mode generation (existing) |
| 2 | `StorySummarize` | Story summarization (existing) |
| 3 | `StoryAnalyze` | Story analysis (existing) |
| 4 | `StoryRank` | Story ranking (existing) |
| 5 | `ScenarioPreview` | Scenario preview (existing) |
| 6 | `ScenarioAdapt` | Scenario adaptation (existing) |
| 7 | `ScenarioAssistant` | Scenario assistant (existing) |
| 8 | `WritingAssistant` | Writing assistant (existing) |
| 9 | `RolePlayAssistant` | RP assistant (existing) |
| 10 | `ModelAnalysis` | Model analysis (existing) |
| **11** | **`RolePlaySemanticAnalysis`** | **Background semantic event inference for RP interactions (NEW)** |

**Change**: Append `RolePlaySemanticAnalysis = 11` (or next available int; enum values are not persisted as integers, they are persisted as the string name via `AppFunction.ToString()`).

---

### FunctionModelDefault (class) — DreamGenClone.Domain/ModelManager/FunctionModelDefault.cs

Add one new nullable property.

**Existing fields:**

| Property | Type | SQLite Column | Notes |
|---|---|---|---|
| `Id` | `string` | `TEXT PRIMARY KEY` | GUID |
| `FunctionName` | `string` | `TEXT NOT NULL` | AppFunction.ToString() |
| `ModelId` | `string` | `TEXT NOT NULL` | Empty string when unset |
| `Temperature` | `double` | `REAL` | |
| `TopP` | `double` | `REAL` | |
| `MaxTokens` | `int` | `INTEGER` | |
| `UpdatedUtc` | `string` | `TEXT` | ISO-8601 |

**New field:**

| Property | Type | SQLite Column | Notes |
|---|---|---|---|
| **`MaxConcurrentJobs`** | **`int?`** | **`INTEGER NULL`** | **Max parallel jobs for the semantic worker. Only meaningful for `RolePlaySemanticAnalysis`. Null = use built-in default (2).** |

**No other entities change schema.**

---

## New Entities

### SemanticBackgroundJobQueue (in-process only, no DB)

An in-process `Channel<BackgroundJobEnvelope>` queue dedicated to `SemanticInteractionAnalysis` jobs.  
- Mirrors the structure of `GenericBackgroundJobQueue` but isolated to one job type.  
- No DB table — jobs are ephemeral in-memory; loss on restart is acceptable (same as existing queue).

| Aspect | Value |
|---|---|
| Interface | `ISemanticBackgroundJobQueue : IBackgroundJobQueue` |
| Implementation | `SemanticBackgroundJobQueue` |
| Backing store | `Channel<BackgroundJobEnvelope>` (unbounded, in-process) |
| Deduplication | `ConcurrentDictionary<string, BackgroundJobEnvelope>` (by JobId) |
| Persistence | None — volatile, ephemeral |

---

## SQLite Schema Changes

### FunctionModelDefaults table — ALTER TABLE (existing databases)

```sql
ALTER TABLE FunctionModelDefaults
ADD COLUMN MaxConcurrentJobs INTEGER NULL;
```

Applied once via the legacy migration gate in `SqlitePersistence.cs`.

### FunctionModelDefaults table — CREATE TABLE (fresh installs)

Update the `CREATE TABLE IF NOT EXISTS FunctionModelDefaults` statement to include:

```sql
MaxConcurrentJobs INTEGER NULL
```

No other tables require changes.

---

## Validation Rules

| Field | Rule |
|---|---|
| `MaxConcurrentJobs` | Must be null, or an integer in range **1–16**. Values outside this range must be rejected at the UI layer before persistence. The worker treats null as 2 (built-in default). |
| `AppFunction.RolePlaySemanticAnalysis` | Must appear exactly once in `FunctionModelDefaults` after the feature is active (row is inserted during first-run or seeded at startup if absent). |

---

## State Transitions (existing — no changes)

`SemanticAnalysisStatus` is unchanged:

```
Idle → Analyzing → Complete
               ↘ Error
```

The `Error` state is reached when either:
- `ModelResolutionException` is thrown (no model assigned to `RolePlaySemanticAnalysis`)
- Any unhandled exception occurs during job processing

The worker sets `ErrorMessage` on the analysis state record when transitioning to `Error`.
