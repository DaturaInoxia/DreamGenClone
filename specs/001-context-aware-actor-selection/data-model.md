# Data Model: Context-Aware Actor Selection

*Phase 1 output*

---

## Overview

This feature introduces:

- **2 new enums**: `TimeOfDay` (Domain), `AffinityType` (Web/Domain)
- **1 new enum**: `PreferredTurnPosition` (Web/Domain)
- **2 new domain models**: `CharacterLocationAffinity` (Web/Domain/Scenarios), `CharacterTurnOverride` (Web/Domain/RolePlay)
- **2 new DTO sets**: `LocationDetectionRequest` / `LocationDetectionResult`, `ActorSelectionRequest` / `ActorSelectionResponse` (Web/Application/RolePlay/Models)
- **1 new computed record**: `AvailableCharacter` (private inside `RolePlayEngineService`)
- **3 additive columns**: `RolePlayV2AdaptiveStates.CurrentTimeOfDay`, `RolePlayV2AdaptiveStates.TimeOfDayManuallySet`, `RolePlayV2SemanticEvents.ActorName`
- **2 new `AppFunction` enum slots**: `RolePlayLocationDetection`, `RolePlayActorSelection`
- **1 new `BackgroundJobTypes` constant**: `LocationDetection`
- **1 new background job payload**: `LocationDetectionJobPayload`
- Additive changes to `AdaptiveScenarioState`, `Scenario`, `Character`, `RolePlaySession`, `SemanticEventRecord`

No existing entities are retired or renamed. Existing tables retain all current columns.

---

## New Entity: `TimeOfDay` (enum)

**Location**: `DreamGenClone.Domain/RolePlay/TimeOfDay.cs`

```csharp
namespace DreamGenClone.Domain.RolePlay;

public enum TimeOfDay
{
    Morning,
    Afternoon,
    Evening,
    Night
}
```

---

## New Entity: `AffinityType` (enum)

**Location**: `DreamGenClone.Web/Domain/Scenarios/CharacterLocationAffinity.cs` (nested in the same file as `CharacterLocationAffinity`)

```csharp
namespace DreamGenClone.Web.Domain.Scenarios;

public enum AffinityType
{
    None,        // Editor default — treated as no rule
    Preferred,   // Soft hint to LLM; doesn't force inclusion or exclusion
    Required,    // Hard include at the linked location
    Excluded     // Hard exclude at the linked location (highest precedence on conflict)
}
```

**Conflict precedence**: `Excluded > Required > Preferred > None` (applied across multiple affinity entries applicable to the same turn).

---

## New Entity: `CharacterLocationAffinity`

**Location**: `DreamGenClone.Web/Domain/Scenarios/CharacterLocationAffinity.cs`

### Properties

| Property | Type | Description |
|---|---|---|
| `LocationName` | `string` | Display name matching `Location.Name` (NOT `Location.Id`); directly compared to `CurrentSceneLocation` |
| `AffinityType` | `AffinityType` | `None` / `Preferred` / `Required` / `Excluded` |
| `TimeOfDay` | `TimeOfDay?` | Optional time-of-day restriction; null = wildcard (applies to any time) |

### Business Rules

- Multiple entries per (character, location) are allowed, each with a distinct or null `TimeOfDay`
- If two entries have the same `LocationName` AND the same `TimeOfDay`, the conflict is resolved by `Excluded > Required > Preferred` precedence; ties within the same `AffinityType` are undefined and an editor-side validator should warn
- Null `TimeOfDay` is a wildcard — applies only when no exact-time-match entry exists for the current `CurrentTimeOfDay`
- Affinity entries with `AffinityType = None` are equivalent to "no affinity" for that location + time slot
- Edited location names break affinity entries by name string; the editor should warn when a location rename leaves dangling affinities

### JSON Serialization

Standard `JsonSerializer` defaults compatible (camelCase tolerant on read, persisted as part of `Character.LocationAffinities` list — serialized within the `Scenario` aggregate via existing scenario JSON persistence in `ScenarioService`).

---

## New Entity: `PreferredTurnPosition` (enum)

**Location**: `DreamGenClone.Web/Domain/RolePlay/CharacterTurnOverride.cs` (same file as the model)

```csharp
namespace DreamGenClone.Web.Domain.RolePlay;

public enum PreferredTurnPosition
{
    Auto,    // Default; no scoring adjustment
    First,   // Scoring hint: small additive boost (+50 per R9 weights)
    Last     // Scoring hint: small penalty (−50 per R9 weights)
}
```

`First` and `Last` are scoring hints, not hard sort rules (per clarification Q3). At most one of `First`/`Last` applies per character; editor UI enforces single selection. `Auto` means no adjustment.

---

## New Entity: `CharacterTurnOverride`

**Location**: `DreamGenClone.Web/Domain/RolePlay/CharacterTurnOverride.cs`

### Properties

| Property | Type | Description |
|---|---|---|
| `CharacterName` | `string` | The character this override applies to (matched case-insensitively against `Character.Name` / interaction `ActorName`) |
| `ResponsePriority` | `int?` | Nullable integer 0–100; null or 0 = no boost; applied additively to base score (per R9) |
| `ParticipateInAutoContinue` | `bool` | Default `true`; false hard-excludes from `ResolveAvailableCharacters` output |
| `PreferredPosition` | `PreferredTurnPosition` | Default `Auto`; applied as a scoring hint only |

### Business Rules

- `ResponsePriority` MUST be clamped to `[0, 100]` on save
- `ParticipateInAutoContinue = false` is a hard filter applied BEFORE scoring (mutates the candidate list, not just the score)
- `PreferredPosition` adjusts the score but does NOT force a fixed slot — the AI/scoring may still override
- Stored per session (`RolePlaySession.CharacterTurnOverrides`); defaults to an empty dictionary
- Persona (POV character "You") is excluded from overrides — overrides apply only to NPC scenario characters

---

## Updated Entity: `Character` (additive)

**File**: `DreamGenClone.Web/Domain/Scenarios/Character.cs`

### New Property

```csharp
public List<CharacterLocationAffinity> LocationAffinities { get; set; } = [];
```

Initialized as an empty list; backward-compatible on load (older scenarios without the field deserialize to empty list = "no affinities for this character — falls back to in-scene detection").

---

## Updated Entity: `Scenario` (additive)

**File**: `DreamGenClone.Web/Domain/Scenarios/Scenario.cs`

### New Property

```csharp
public TimeOfDay DefaultTimeOfDay { get; set; } = TimeOfDay.Afternoon;
```

Default `Afternoon` matches the design spec's sensible default. Serialized with the scenario; reading from older scenarios without the field leaves the C# default (`Afternoon`).

`Scenario.DefaultStartingLocationId` already exists (line 105) but is currently unused. The seeding wiring lives in `RolePlayEngineService.CreateSessionAsync` (not a new property), per R14.

---

## Updated Entity: `AdaptiveScenarioState` (additive)

**File**: `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`

### New Properties

```csharp
public TimeOfDay? CurrentTimeOfDay { get; set; }
public bool TimeOfDayManuallySet { get; set; }
```

### Business Rules

- `CurrentTimeOfDay == null` means time has not been detected yet. Scoring and affinity resolution treat null as "no time-of-day restriction applies" (i.e., wildcard behaviors choose the null-TimeOfDay affinity).
- `TimeOfDayManuallySet = true` blocks `DetectTimeOfDayAsync` from overwriting `CurrentTimeOfDay`. Switching the UI back to "Auto" sets it to `false` and re-enables auto-detection.
- Seeding in `CreateSessionAsync`: `state.CurrentTimeOfDay = scenario.DefaultTimeOfDay;` (alongside existing `scenario.Default*` copies near L404–L410)
- Seeding in `SeedFromScenarioAsync`: same pattern IF the method re-seeds adaptive state separately (currently it copies `scenario.Default*` to the new `AdaptiveScenarioState`)

---

## New DB Columns: Additive Migrations

All migrations live inside `RolePlayStateRepository.EnsureAdaptiveStateSchemaAsync` and use the existing `HasColumnAsync` + `ALTER TABLE` idempotent pattern.

### `RolePlayV2AdaptiveStates.CurrentTimeOfDay`

```sql
ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CurrentTimeOfDay TEXT NULL;
```

Stores `TimeOfDay` enum name (`"Morning"`, `"Afternoon"`, `"Evening"`, `"Night"`) or NULL.

### `RolePlayV2AdaptiveStates.TimeOfDayManuallySet`

```sql
ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN TimeOfDayManuallySet INTEGER NOT NULL DEFAULT 0;
```

Boolean flag (0 / 1).

### `RolePlayV2SemanticEvents.ActorName`

```sql
ALTER TABLE RolePlayV2SemanticEvents ADD COLUMN ActorName TEXT;
```

Nullable text column; null for rows that haven't been backfilled. Loaded as `string?` on `SemanticEventRecord`.

---

## Updated Entity: `SemanticEventRecord` (additive)

**File**: `DreamGenClone.Domain/RolePlay/AdaptiveStateV2Records.cs`

### New Property

```csharp
public string? ActorName { get; set; }
```

Nullable; populated on new events from `SemanticEventInferenceRequest.ActorName` (already threaded by callers). Null on historical events until the one-time backfill (R7) populates it via `RolePlayInteractions.ActorName` JOIN.

---

## New AppFunction Slots

**File**: `DreamGenClone.Domain/ModelManager/AppFunction.cs`

### Add

```csharp
RolePlayLocationDetection,
RolePlayActorSelection
```

Place near `RolePlaySemanticAnalysis` (existing at L13). Each slot is a distinct model default in Model Manager; users configure cheap/fast models separately for the two functions. `ModelResolutionService.ResolveAsync(AppFunction.RolePlayLocationDetection)` throws `ModelResolutionException` when no default is configured — the caller catches and records `Success = false` per the no-fallback pattern (R1).

Compact, sensible default models:
- `RolePlayLocationDetection`: cheap, fast, single-shot JSON extractor (e.g., a small general-purpose model with low temperature, low max tokens ~300)
- `RolePlayActorSelection`: small/medium ranking model (~1500 token prompt, low temperature 0.3 for ordered selection)

---

## New `BackgroundJobTypes.Constant`

**File**: `DreamGenClone.Web/Application/BackgroundJobs/BackgroundJobTypes.cs`

```csharp
public const string LocationDetection = "location-detection";
```

Add in alphabetical order alongside `SemanticInteractionAnalysis` and `EncounterSummaryEnhancement`. Lowercase-kebab-case matches existing convention.

---

## New Job Payload: `LocationDetectionJobPayload`

**Location**: `DreamGenClone.Web/Application/RolePlay/LocationDetectionJobPayload.cs`

### Properties

| Property | Type | Description |
|---|---|---|
| `SessionId` | `string` | Owning session; handler loads fresh V2 state from `RolePlayStateRepository` |

Minimal payload — handler loads fresh `AdaptiveScenarioState` and `RolePlaySession` from DB to avoid stale in-memory state conflicts. Dedupe key: `$"location:{SessionId}"`.

### JSON Serialization

Uses the same `JsonSerializerDefaults.Web` as `SemanticInteractionAnalysisJobPayload`.

---

## New DTOs: `LocationDetectionRequest` / `LocationDetectionResult`

**File**: `DreamGenClone.Web/Application/RolePlay/Models/LocationDetectionModels.cs`

### `LocationDetectionRequest`

| Property | Type | Description |
|---|---|---|
| `SessionId` | `string` | Required; correlation for logs |
| `RecentInteractions` | `IReadOnlyList<string>` | Last ~3 NPC/Custom interaction summaries (2–3 sentences each, ≤500 tokens total) |
| `ScenarioLocationNames` | `IReadOnlyList<string>` | All `Location.Name` values from `scenario.Locations`; LLM must pick one of these or null |
| `PreviousLocation` | `string?` | Current `CurrentSceneLocation`; LLM uses for "no change detected" cases |
| `CharacterNames` | `IReadOnlyList<string>` | All scenario NPC names; LLM lists per-character locations |

### `LocationDetectionResult`

| Property | Type | Description |
|---|---|---|
| `Success` | `bool` | True only when the LLM call succeeded and JSON parsed cleanly |
| `ErrorMessage` | `string?` | Null on success; `ModelResolutionException.Message` or parse error on failure |
| `DetectedLocation` | `string?` | One of `ScenarioLocationNames` or null if no clear match |
| `LocationConfidence` | `decimal?` | 0– decimal confidence reported by LLM; null on failure |
| `PerCharacterLocations` | `IReadOnlyDictionary<string, string?>?` | Character name → location name mapping (or null if LLM didn't return one) |
| `LocationChanged` | `bool` | True if `DetectedLocation` differs from `PreviousLocation` |
| `Reasoning` | `string?` | LLM's short reasoning text |
| `RawModelOutput` | `string` | Raw LLM response for log/debugging |
| `PromptSystem` | `string` | System prompt for log/diagnostics |
| `PromptUser` | `string` | User prompt for log/diagnostics |

---

## New DTOs: `ActorSelectionRequest` / `ActorSelectionResponse` / `ActorCandidateInfo`

**File**: `DreamGenClone.Web/Application/RolePlay/Models/ActorSelectionModels.cs`

### `ActorSelectionRequest`

| Property | Type | Description |
|---|---|---|
| `SessionId` | `string` | For logging |
| `NarrativeSummary` | `string` | Condensed last ~3 interactions (≤500 tokens) |
| `CurrentPhase` | `string` | `NarrativePhase` enum name |
| `CurrentLocation` | `string?` | `CurrentSceneLocation` (display name) |
| `CurrentTimeOfDay` | `string?` | `TimeOfDay` enum name or null |
| `Candidates` | `IReadOnlyList<ActorCandidateInfo>` | Pre-scored and pre-filtered candidates from `ResolveAvailableCharacters` + `ScoreActorForAutoSelection` |
| `ActiveThemes` | `IReadOnlyList<string>` | Active theme IDs (read from `AdaptiveScenarioState.PrimaryThemeId`/`SecondaryThemeId` + `ThemeScores`) |
| `RecentSemanticEvents` | `IReadOnlyList<string>` | Last 3 semantic event IDs with `ActorName` if available |
| `BatchSize` | `int` | Capped to `[1, 6]` per `RolePlaySession.SceneContinueBatchSize` |

### `ActorCandidateInfo`

| Property | Type | Description |
|---|---|---|
| `Name` | `string` | Character display name |
| `Role` | `string?` | `CharacterRole` from snapshot (or null) |
| `IsInScene` | `bool` | From `RolePlayScenePresenceHelper.IsActorInScene` (false if unknown) |
| `AffinityStatus` | `string` | `None` / `Preferred` / `Required` / `Excluded` (post-resolution; Excluded filtered out earlier so this should never reach the LLM) |
| `TimeOfDayMatch` | `bool?` | True if affinity exact-matches current time-of-day, false if mismatch, null if affinity is wildcard |
| `KeyStats` | `IReadOnlyDictionary<string, int>` | Selected `CharacterStatProfileV2` stats — Desire/Restraint/Dominance (or whatever subset is representative) |
| `LastSpokeTurnsAgo` | `int?` | Recency count or null if never |
| `BaseScore` | `double` | Final scored value (location + affinity + time + recency + override + position hint) — passed as a hint to the LLM |
| `AffinityDetails` | `string?` | Short human-readable summary, e.g., `"Required at Beach (Evening match)"` for LLM context |

### `ActorSelectionResponse`

| Property | Type | Description |
|---|---|---|
| `Success` | `bool` | True on LLM success and parse-validation; false on failure/timeout/no-model |
| `ErrorMessage` | `string?` | Set on failure |
| `OrderedNames` | `IReadOnlyList<string>` | Subset of candidate names, ordered by LLM |
| `Reasoning` | `string?` | LLM's reasoning |
| `Source` | `ActorSelectionSource` | `LLM`, `Cache`, `Scoring`, `Fallback` (enum) |
| `RawModelOutput` | `string` | Raw LLM response, for debug logs |
| `PromptSystem` | `string` | For diagnostics |
| `PromptUser` | `string` | For diagnostics |

### `ActorSelectionSource` enum

```csharp
public enum ActorSelectionSource
{
    LLM,       // Fresh LLM call succeeded and parsed
    Cache,     // Reused cached ordering (fingerprint match), no LLM call
    Scoring,   // No model configured for RolePlayActorSelection; base scoring path only
    Fallback   // LLM call failed/timed out; scoring order preserved, logged with ErrorMessage
}
```

---

## New Computed Record: `AvailableCharacter` (engine-internal)

Private nested record inside `RolePlayEngineService`:

```csharp
private sealed record AvailableCharacter(
    string Name,
    string? Role,
    bool IsInScene,
    AffinityStatus AffinityStatus,    // Resolved per R11
    bool? TimeOfDayMatch,
    bool IsAvailable,
    string? AffinityDetails);
```

`AffinityStatus` is a private enum mirroring `AffinityType` plus a `None` value, kept internal to the engine because it's only consumed by `ScoreActorForAutoSelection` and the `ActorCandidateInfo` builder.

---

## Updated Entity: `RolePlaySession` (additive)

**File**: `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`

### New Properties

```csharp
public Dictionary<string, CharacterTurnOverride> CharacterTurnOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
public List<string>? LastActorOrdering { get; set; }   // Transient (not persisted) — cache of LLM-ordered names
public string? LastContextFingerprint { get; set; }    // Transient (not persisted) — composite fingerprint
```

### Out-of-scope modifications

- `RolePlaySession.SceneContinueBatchSize` already exists (default 3 per L46 of `RolePlaySession.cs`). No new column needed.
- Should `LastActorOrdering` and `LastContextFingerprint` be persisted across session reloads? Spec decision: transient (not persisted). They are recomputed on first overflow click after a fresh session load using the scoring base path, then cached in memory for the duration of the loaded session. Re-loading from DB invalidates the cache (LLM gets a fresh call on the next overflow click). This avoids the complexity of cache invalidation across DB writes; the first click after a load takes the LLM call latency, subsequent clicks use the cache.

### Business Rules

- `CharacterTurnOverrides` keyed by `CharacterRole`-resolved name (case-insensitive); editor UI adds/tweaks on the workspace panel
- Persona ("You") is excluded from the dictionary — UI prevents adding a "You" entry
- `LastActorOrdering == null` triggers a fresh LLM/scoring call regardless of fingerprint

---

## Validation Summary Table

| Field | Validation | Action on violation |
|---|---|---|
| `CharacterLocationAffinity.LocationName` | Required non-empty | Editor surfaces error; default to first scenario location name |
| `CharacterLocationAffinity.AffinityType` | Defined enum value | Deserialize defaults to `None`; never persisted invalid |
| `CharacterLocationAffinity.TimeOfDay` | Defined enum value or null | As above |
| `ResponsePriority` | Clamp to [0, 100] | `Math.Clamp(value, 0, 100)` on save |
| `SceneContinueBatchSize` | Clamp to [1, 6] | Already in `RolePlaySession`; new UI uses same range |
| `Character.LocationAffinities` | Multiple per location allowed | Conflict resolver in `ResolveAvailableCharacters` handles precedence |
| `SemanticEventRecord.ActorName` | Nullable, no validation on load | Backfilled by idempotent startup migration (R7) |

---

## State Transition Notes

- **Time-of-day manual override**: `TimeOfDayManuallySet` flips `true` on manual UI change; flips `false` on user selecting "Auto" in the workspace dropdown. No intermediate state.
- **Location eventual consistency**: one-turn lag — background LLM job writes `CurrentSceneLocation` while foreground reads previous turn's value. No state lock added (per R2 dedupe guarantees one job per session at a time).
- **Cache fingerprint**: changes atomically with each LLM selection completion (engine writes both `LastActorOrdering` and `LastContextFingerprint` together); cache miss is a recomputation, never an inconsistent stale lookup.