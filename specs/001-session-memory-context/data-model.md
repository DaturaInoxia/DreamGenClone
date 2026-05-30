# Data Model: B-041 — Session Memory Context (Intimate Encounter History Injection)

*Phase 1 output*

---

## Overview

This feature introduces one new persistent entity (`EncounterSummaryRecord`), one new DB table (`RolePlayV2EncounterSummaries`), one new configuration class (`RolePlayMemoryOptions`), one new background job payload (`EncounterSummaryJobPayload`), additive changes to `AdaptiveScenarioState` and `RolePlaySession`, and one new constant in `BackgroundJobTypes`.

No existing entities are retired or renamed.

---

## New Entity: `EncounterSummaryRecord`

**Location**: `DreamGenClone.Domain/RolePlay/EncounterSummaryRecord.cs`

### Properties

| Property | Type | Description |
|---|---|---|
| `Id` | `string` | GUID (no dashes), primary key |
| `SessionId` | `string` | Owning session |
| `CharacterId` | `string` | The character whose perspective this summary represents |
| `SummaryType` | `EncounterSummaryType` | `PhaseMilestone` or `ArcCompletion` |
| `CycleIndex` | `int` | Which arc (0-based) this transition belongs to |
| `FromPhase` | `NarrativePhase` | The phase being exited |
| `ToPhase` | `NarrativePhase` | The phase being entered |
| `OccurredUtc` | `DateTime` | UTC timestamp of the phase transition |
| `InteractionCountInPhase` | `int` | How many interactions occurred in `FromPhase` before this transition |
| `SceneLocation` | `string?` | Active scene location at transition time (nullable) |
| `ActiveThemeId` | `string?` | `PrimaryThemeId` at transition time (nullable) |
| `FinishingMoveId` | `string?` | Finishing move ID if one was reached (ArcCompletion only, nullable) |
| `PositionIdsJson` | `string?` | JSON array of position IDs used in the arc (ArcCompletion only, nullable) |
| `CharacterStatsSnapshotJson` | `string` | JSON of this character's stats at transition: `{"Desire":N,"Restraint":N,"Tension":N,"Connection":N}` |
| `TemplateSummary` | `string` | Deterministic template-generated text, written synchronously at transition |
| `LlmSummary` | `string?` | LLM-generated prose, written asynchronously by the job handler (nullable until job completes) |
| `LlmEnhancedUtc` | `DateTime?` | UTC timestamp when `LlmSummary` was written (nullable) |

### Computed Properties

```csharp
// Returns LLM prose if available, otherwise template text
public string ActiveSummary => LlmSummary ?? TemplateSummary;

// True if LLM enhancement has been applied
public bool IsEnhanced => LlmSummary is not null;
```

### Supporting Enum: `EncounterSummaryType`

**Location**: `DreamGenClone.Domain/RolePlay/EncounterSummaryRecord.cs` (nested or same file)

```csharp
public enum EncounterSummaryType
{
    PhaseMilestone,   // Template-only; written at every non-ArcCompletion transition
    ArcCompletion     // LLM-enriched; written at Climax→Reset
}
```

### Business Rules

- `TemplateSummary` MUST NOT be null or empty — validated before save
- `CharacterStatsSnapshotJson` MUST be valid JSON (at minimum `{}`) — generated from live character snapshot, never null
- `FinishingMoveId` and `PositionIdsJson` are only populated when `SummaryType == ArcCompletion`; ignored for `PhaseMilestone`
- `LlmSummary` is written only by `EncounterSummaryJobHandler`; no other code path writes it
- If `CharacterSnapshots` is empty at transition time, template generation produces a minimal stat-free summary and MUST NOT throw

---

## New DB Table: `RolePlayV2EncounterSummaries`

**Creation location**: `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` — in the `EnsureAdaptiveStateSchemaAsync` block alongside existing V2 tables

```sql
CREATE TABLE IF NOT EXISTS RolePlayV2EncounterSummaries (
    Id                          TEXT NOT NULL PRIMARY KEY,
    SessionId                   TEXT NOT NULL,
    CharacterId                 TEXT NOT NULL,
    SummaryType                 TEXT NOT NULL,
    CycleIndex                  INTEGER NOT NULL DEFAULT 0,
    FromPhase                   TEXT NOT NULL,
    ToPhase                     TEXT NOT NULL,
    OccurredUtc                 TEXT NOT NULL,
    InteractionCountInPhase     INTEGER NOT NULL DEFAULT 0,
    SceneLocation               TEXT NULL,
    ActiveThemeId               TEXT NULL,
    FinishingMoveId             TEXT NULL,
    PositionIdsJson             TEXT NULL,
    CharacterStatsSnapshotJson  TEXT NOT NULL DEFAULT '{}',
    TemplateSummary             TEXT NOT NULL DEFAULT '',
    LlmSummary                  TEXT NULL,
    LlmEnhancedUtc              TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_RolePlayV2EncounterSummaries_Session_OccurredUtc
    ON RolePlayV2EncounterSummaries (SessionId, OccurredUtc DESC);
```

**Notes**:
- No FK constraints (consistent with all other V2 tables in this codebase)
- `OccurredUtc` stored as ISO 8601 text (consistent with `RolePlayV2PhaseTransitions` and other tables)
- `SummaryType` stored as text (`"PhaseMilestone"` or `"ArcCompletion"`) — parsed to enum on load

---

## Schema Change: `Sessions` Table

**Migration**: Add `MaxMilestonesToInject` column to the `Sessions` table.

```sql
-- Guarded with PRAGMA table_info check (same pattern as existing column additions)
ALTER TABLE Sessions ADD COLUMN MaxMilestonesToInject INTEGER NULL;
```

**Map to**: `RolePlaySession.MaxMilestonesToInject` (`int?`, null = use global default).

---

## Changes to Existing Domain Types

### `AdaptiveScenarioState` (additive, no breaking changes)

**File**: `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`

Add one new property:

```csharp
/// <summary>
/// Per-character encounter summaries loaded from RolePlayV2EncounterSummaries.
/// Populated at session load; updated in-memory when new summaries are written.
/// </summary>
public List<EncounterSummaryRecord> EncounterSummaries { get; set; } = [];
```

This list is loaded by `RolePlayStateRepository.LoadEncounterSummariesAsync` and appended to whenever a new summary is written during a session (in-memory cache of what's in the DB for the session).

### `RolePlaySession` (additive, no breaking changes)

**File**: `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`

Add one new property:

```csharp
/// <summary>
/// Per-session override for the number of phase milestones to inject into prompts.
/// Null means use the global RolePlayMemoryOptions.MaxMilestonesToInject default.
/// </summary>
public int? MaxMilestonesToInject { get; set; }
```

---

## New Configuration Class: `RolePlayMemoryOptions`

**Location**: `DreamGenClone.Infrastructure/Configuration/RolePlayMemoryOptions.cs`

```csharp
public sealed class RolePlayMemoryOptions
{
    public const string SectionName = "RolePlayMemory";

    /// <summary>
    /// Maximum number of PhaseMilestone entries from the current arc to inject
    /// into the continuation prompt.
    /// Default: 5.
    /// </summary>
    public int MaxMilestonesToInject { get; init; } = 5;

    /// <summary>
    /// Maximum number of ArcCompletion entries (most recent arcs) to inject
    /// into the continuation prompt.
    /// Default: 10.
    /// </summary>
    public int MaxArcCompletionsToInject { get; init; } = 10;

    /// <summary>
    /// When true, the EncounterSummaryJobHandler will call the LLM to generate
    /// per-character intimate act prose for ArcCompletion entries.
    /// When false, only TemplateSummary is used for all entries.
    /// Default: true.
    /// </summary>
    public bool EnableLlmSummaryEnhancement { get; init; } = true;
}
```

**`appsettings.Development.json`** addition:

```json
"RolePlayMemory": {
  "MaxMilestonesToInject": 5,
  "MaxArcCompletionsToInject": 10,
  "EnableLlmSummaryEnhancement": true
}
```

---

## New Job Payload: `EncounterSummaryJobPayload`

**Location**: `DreamGenClone.Application/RolePlay/EncounterSummaryJobPayload.cs`

```csharp
public sealed class EncounterSummaryJobPayload
{
    public string SessionId { get; set; } = string.Empty;

    /// <summary>The CycleIndex of the completed arc to summarize.</summary>
    public int CycleIndex { get; set; }
}
```

**Deduplication key** used at enqueue: `$"enc-summary:{sessionId}:{cycleIndex}"`

One job is enqueued per arc transition (Climax→Reset), not per character. The handler generates prose for all characters in a single LLM call.

---

## New Constant: `BackgroundJobTypes`

**File**: `DreamGenClone.Web/Application/BackgroundJobs/BackgroundJobTypes.cs` (or wherever the existing constants are defined)

Add:

```csharp
public const string EncounterSummaryEnhancement = "encounter-summary-enhancement";
```

---

## Load Site: `RolePlayStateRepository`

**New method** `LoadEncounterSummariesAsync(string sessionId, int maxMilestones, int maxArcCompletions, int currentCycleIndex, CancellationToken)`:

```sql
-- Load most recent M ArcCompletion entries (all prior arcs)
SELECT * FROM RolePlayV2EncounterSummaries
WHERE SessionId = @sessionId AND SummaryType = 'ArcCompletion'
ORDER BY OccurredUtc DESC
LIMIT @maxArcCompletions;

-- Load last N PhaseMilestone entries for current arc
SELECT * FROM RolePlayV2EncounterSummaries
WHERE SessionId = @sessionId
  AND SummaryType = 'PhaseMilestone'
  AND CycleIndex = @currentCycleIndex
ORDER BY OccurredUtc DESC
LIMIT @maxMilestones;
```

Results are merged (arc completions reversed to chronological order first, then milestones reversed to chronological) and returned as `IReadOnlyList<EncounterSummaryRecord>`.

**Called from**: `LoadStateAsync` (or `LoadAdaptiveStateAsync`) — same pattern as `LoadScenarioHistoryAsync`.

---

## Write Site: `RolePlayEngineService`

**Hook location**: Immediately after `await _stateRepository.SaveTransitionEventAsync(lifecycle.TransitionEvent, cancellationToken)` (~L2892).

**Write logic**:
1. For each character in `v2State.CharacterSnapshots`:
   a. Build `EncounterSummaryRecord` with `SummaryType` = `PhaseMilestone` (or `ArcCompletion` if Climax→Reset)
   b. Generate `TemplateSummary` via `IEncounterSummaryService.GenerateTemplateAsync`
   c. `await _encounterSummaryService.SaveAsync(record, cancellationToken)` — writes to DB
   d. Append record to `v2State.EncounterSummaries` (in-memory update)
2. If transition is Climax→Reset AND `_memoryOptions.EnableLlmSummaryEnhancement`:
   - Enqueue one `EncounterSummaryEnhancement` job for the arc (payload: `{SessionId, CycleIndex}`)

**Session creation update**: `CreateRolePlaySessionRequest.MaxMilestonesToInject` (int?, optional) → written to `session.MaxMilestonesToInject` on session create.
