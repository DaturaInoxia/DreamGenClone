# Quickstart: B-041 — Session Memory Context (Intimate Encounter History Injection)

*Phase 1 output — implementation order, key gotchas, and verification steps*

---

## Recommended Implementation Order

Follow this strict bottom-up order. Each step is a buildable checkpoint. Build the solution after each step before proceeding.

---

### Step 1 — Domain: New entity and enum (no breaking changes)

**Files to create**:
- `DreamGenClone.Domain/RolePlay/EncounterSummaryRecord.cs`
  - `EncounterSummaryType` enum (`PhaseMilestone`, `ArcCompletion`)
  - `EncounterSummaryRecord` class with all properties from data-model.md
  - Computed properties `ActiveSummary` and `IsEnhanced`

**Key gotcha**: Initialize `CharacterStatsSnapshotJson` to `"{}"` not `null`. Initialize `PositionIdsJson` to `null` (not empty string) — the DB column is nullable. Use `= [];` for list initializers (C# 12 collection expression, consistent with the rest of this codebase).

**Build check**: Domain project builds with 0 errors.

---

### Step 2 — Domain: Update `AdaptiveScenarioState`

**File**: `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`

Add property:
```csharp
public List<EncounterSummaryRecord> EncounterSummaries { get; set; } = [];
```

**Key gotcha**: Place it near `ScenarioHistory` and `PairwiseStats` — these are the other loaded-from-DB list properties. The `EncounterSummaries` list is NOT serialized to `CharacterSnapshotsJson` or any existing JSON column — it is loaded separately by `LoadEncounterSummariesAsync`.

**Build check**: Domain project builds with 0 errors. No other files reference `EncounterSummaries` yet — no cascading errors expected.

---

### Step 3 — Web/Domain: Update `RolePlaySession`

**File**: `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`

Add property:
```csharp
public int? MaxMilestonesToInject { get; set; }
```

**Build check**: Web project builds with 0 errors.

---

### Step 4 — Infrastructure/Config: New `RolePlayMemoryOptions`

**File**: `DreamGenClone.Infrastructure/Configuration/RolePlayMemoryOptions.cs`

Add constant + properties per data-model.md. No existing files need changes at this step.

**Build check**: Infrastructure project builds with 0 errors.

---

### Step 5 — Application: New interfaces and job payload

**Files to create**:
- `DreamGenClone.Application/RolePlay/Abstractions/IEncounterSummaryService.cs` (see contracts/)
- `DreamGenClone.Application/RolePlay/EncounterSummaryJobPayload.cs`

**Update existing**:
- `DreamGenClone.Web/Application/BackgroundJobs/BackgroundJobTypes.cs` — add `EncounterSummaryEnhancement` constant

**Key gotcha**: Find `BackgroundJobTypes` by searching for `SemanticInteractionAnalysis` constant — it's in the same file. Add the new constant in alphabetical order.

**Build check**: Application and Web projects build with 0 errors.

---

### Step 6 — Infrastructure/Persistence: Table creation and Sessions migration

**File**: `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`

6a. In `EnsureAdaptiveStateSchemaAsync` (near `RolePlayV2SemanticEvents` table creation ~L975), add:
   - `CREATE TABLE IF NOT EXISTS RolePlayV2EncounterSummaries (...)` with index
6b. In the `Sessions` column migration section (near the `HusbandAwarenessProfileId` ALTER TABLE pattern), add:
   - `PRAGMA table_info(Sessions)` guard → `ALTER TABLE Sessions ADD COLUMN MaxMilestonesToInject INTEGER NULL`
6c. Add CRUD methods:
   - `SaveEncounterSummaryAsync(EncounterSummaryRecord, CancellationToken)`
   - `UpdateEncounterSummaryLlmAsync(string id, string llmSummary, DateTime llmEnhancedUtc, CancellationToken)`
   - `LoadEncounterSummariesAsync(string sessionId, int maxMilestones, int currentCycleIndex, CancellationToken)` → `IReadOnlyList<EncounterSummaryRecord>`

**Key gotcha**: The `PRAGMA table_info` guard pattern — search for the existing `PRAGMA table_info(Sessions)` usage to find the correct location and copy the exact guard structure. Do not add a second `PRAGMA table_info(Sessions)` block; extend the existing one.

**Key gotcha**: `OccurredUtc` must be stored as `value.ToString("O")` (round-trip ISO 8601) and parsed with `DateTime.Parse(..., null, System.Globalization.DateTimeStyles.RoundtripKind)`. This is consistent with all other timestamp columns in this codebase.

**Build check**: Infrastructure project builds with 0 errors.

---

### Step 7 — Infrastructure/Repository: Load summaries into `AdaptiveScenarioState`

**File**: `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs`

7a. Add `LoadEncounterSummariesAsync` method that calls `SqlitePersistence.LoadEncounterSummariesAsync`
7b. In `LoadAdaptiveStateAsync` (or `LoadStateAsync`), after `LoadScenarioHistoryAsync`, call `LoadEncounterSummariesAsync` and assign to `state.EncounterSummaries`

**Key gotcha**: `maxMilestones` is NOT known at load time (requires the session's `MaxMilestonesToInject` or the global option). Load ALL milestones at load time (no `LIMIT`) and filter at injection time in `RolePlayContinuationService`. This avoids a circular dependency where the repository needs the options class. Alternatively, pass `maxMilestones` as a parameter — check how `LoadScenarioHistoryAsync` handles this to stay consistent.

**Also update**: `RolePlayStateRepository.SaveSessionAsync` — ensure `MaxMilestonesToInject` is written to the `Sessions` table (if the `Sessions` save path goes through this method).

**Build check**: Infrastructure project builds with 0 errors.

---

### Step 8 — Infrastructure: `EncounterSummaryService` (template generator + save)

**File**: `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryService.cs`

Implements `IEncounterSummaryService`. Methods:

- `GenerateTemplatesAsync(NarrativePhaseTransitionEvent, AdaptiveScenarioState, CancellationToken)` → `List<EncounterSummaryRecord>`
  - Iterates `v2State.CharacterSnapshots`
  - Determines `SummaryType` from `transitionEvent.ToPhase` (Reset = ArcCompletion, else PhaseMilestone)
  - Builds template text per character (see research.md R4)
  - Returns list (does NOT save — caller saves)

- `SaveAsync(EncounterSummaryRecord, CancellationToken)` → calls persistence layer

- `UpdateLlmSummaryAsync(string summaryId, string llmSummary, DateTime enhancedUtc, CancellationToken)` → calls persistence layer

**Key gotcha**: `GenerateTemplatesAsync` MUST NOT throw when `CharacterSnapshots` is empty. Guard: `if (v2State.CharacterSnapshots is { Count: 0 }) return [];` and log a Debug-level message.

**Key gotcha**: Character name is NOT on `CharacterStatProfileV2` directly (it stores `CharacterId`). You need to look up the character name from the `session.Characters` list. The session object must be passed to (or available in) the service — pass it as a parameter.

**Build check**: Infrastructure project builds with 0 errors.

---

### Step 9 — Infrastructure: `EncounterSummaryJobHandler` (LLM arc completion enrichment)

**File**: `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryJobHandler.cs`

Implements `IBackgroundJobHandler`. `JobType = BackgroundJobTypes.EncounterSummaryEnhancement`.

`HandleAsync(payloadJson, cancellationToken)`:
1. Deserialize `EncounterSummaryJobPayload`
2. Load summary row from DB (by `SummaryId`)
3. If `SummaryType != ArcCompletion`, log Warning and return (safety guard)
4. Load arc interactions: all `RolePlayInteractions` for the session where `CycleIndex == summary.CycleIndex`, ordered by creation time, last 30 only (token budget)
5. Look up character name and role from session
6. Build LLM prompt (see research.md R5 template)
7. Call LLM service (same model manager used by semantic analysis — `ISemanticEventInferenceService` or equivalent inference service)
8. Write `LlmSummary` + `LlmEnhancedUtc` via `_encounterSummaryService.UpdateLlmSummaryAsync`
9. On any exception: log `Warning` with `summaryId` + `characterId`, return normally

**Key gotcha**: The arc interactions load needs a way to filter by `CycleIndex`. If `RolePlayInteractions` table does not have a `CycleIndex` column, use the `OccurredUtc` range from the arc start transition event — but check `RolePlayInteractions` schema first. If `CycleIndex` is present, use it. See `RolePlayStateRepository.LoadInteractionsAsync` for the existing query pattern.

**Build check**: Infrastructure project builds with 0 errors.

---

### Step 10 — Web/Engine: Hook in `RolePlayEngineService`

**File**: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

After `await _stateRepository.SaveTransitionEventAsync(lifecycle.TransitionEvent, cancellationToken)` (~L2892):

```csharp
// Generate and save encounter summaries per character
var summaries = await _encounterSummaryService.GenerateTemplatesAsync(
    lifecycle.TransitionEvent, v2State, session, cancellationToken);
foreach (var summary in summaries)
{
    await _encounterSummaryService.SaveAsync(summary, cancellationToken);
    v2State.EncounterSummaries.Add(summary);
}

// Enqueue LLM enhancement for arc completions
if (lifecycle.TransitionEvent.ToPhase == NarrativePhase.Reset
    && _memoryOptions.Value.EnableLlmSummaryEnhancement)
{
    foreach (var arcSummary in summaries)
    {
        var payload = JsonSerializer.Serialize(new EncounterSummaryJobPayload
        {
            SessionId = session.SessionId,
            SummaryId = arcSummary.Id,
            CharacterId = arcSummary.CharacterId
        });
        _backgroundJobQueue!.Enqueue(
            BackgroundJobTypes.EncounterSummaryEnhancement,
            payload,
            dedupeKey: $"enc-summary:{arcSummary.Id}:{arcSummary.CharacterId}");
    }
}
```

**Key gotcha**: `IEncounterSummaryService` and `IOptions<RolePlayMemoryOptions>` must be injected into `RolePlayEngineService` constructor. Search for other injected options in that constructor to add in the right place.

**Build check**: Web project builds with 0 errors.

---

### Step 11 — Web/Prompt: Inject "Session Memory" block

**File**: `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`

In `BuildPromptAsync`, after the Recent Interaction History block:

```csharp
// Session Memory block — prior arc summaries + current-arc phase milestones
var effectiveMaxMilestones = session.MaxMilestonesToInject ?? _memoryOptions.Value.MaxMilestonesToInject;
var arcCompletions = v2State.EncounterSummaries
    .Where(s => s.SummaryType == EncounterSummaryType.ArcCompletion)
    .OrderBy(s => s.OccurredUtc)
    .ToList();
var milestones = v2State.EncounterSummaries
    .Where(s => s.SummaryType == EncounterSummaryType.PhaseMilestone
             && s.CycleIndex == v2State.CycleIndex)
    .OrderByDescending(s => s.OccurredUtc)
    .Take(effectiveMaxMilestones)
    .OrderBy(s => s.OccurredUtc)
    .ToList();

if (arcCompletions.Any() || milestones.Any())
{
    sb.AppendLine("Session Memory:");
    foreach (var grp in arcCompletions.GroupBy(s => s.CycleIndex).OrderBy(g => g.Key))
    {
        foreach (var entry in grp.OrderBy(s => s.CharacterId))
        {
            var charName = ResolveCharacterName(session, entry.CharacterId);
            sb.AppendLine($"[Arc {entry.CycleIndex + 1} Complete — {charName}]");
            sb.AppendLine(entry.ActiveSummary);
            sb.AppendLine();
        }
    }
    foreach (var entry in milestones)
    {
        var charName = ResolveCharacterName(session, entry.CharacterId);
        sb.AppendLine($"[{entry.FromPhase} → {entry.ToPhase} — {charName}]");
        sb.AppendLine(entry.ActiveSummary);
    }
    sb.AppendLine();
}
```

**Key gotcha**: `IOptions<RolePlayMemoryOptions>` must be injected into `RolePlayContinuationService`. Add to constructor injection.

**Build check**: Web project builds with 0 errors.

---

### Step 12 — Web/UI: Session creation override field

**File**: Session creation `.razor` component

Add a nullable int input for `MaxMilestonesToInject` (optional, labeled "Max phase milestones in memory (leave blank for global default)"). Wire to `CreateRolePlaySessionRequest.MaxMilestonesToInject`.

Update `CreateRolePlaySessionRequest`:
```csharp
public int? MaxMilestonesToInject { get; set; }
```

Update session creation handler in `RolePlayEngineService.CreateSessionAsync` to write `request.MaxMilestonesToInject` to `session.MaxMilestonesToInject`.

---

### Step 13 — Program.cs and appsettings

**File**: `DreamGenClone.Web/Program.cs`
- Add: `builder.Services.Configure<RolePlayMemoryOptions>(builder.Configuration.GetSection(RolePlayMemoryOptions.SectionName));`
- Add: `builder.Services.AddScoped<IBackgroundJobHandler, EncounterSummaryJobHandler>();`
- Add: `builder.Services.AddScoped<IEncounterSummaryService, EncounterSummaryService>();`

**File**: `DreamGenClone.Web/appsettings.Development.json`
- Add `"RolePlayMemory"` section per data-model.md

**Build check**: Full solution builds with 0 errors.

---

### Step 14 — Tests

**Files to create in `DreamGenClone.Tests/RolePlay/`**:

- `EncounterSummaryTemplateGeneratorTests.cs`
  - Generates non-empty `TemplateSummary` for each `NarrativePhase` → `NarrativePhase` combination
  - Empty `CharacterSnapshots` returns empty list without throwing
  - `ArcCompletion` sets `SummaryType` correctly when `ToPhase == Reset`

- `EncounterSummaryRecordTests.cs`
  - `ActiveSummary` returns `LlmSummary` when set
  - `ActiveSummary` returns `TemplateSummary` when `LlmSummary` is null
  - `IsEnhanced` is false until `LlmSummary` is set

- `EncounterSummaryInjectionTests.cs`
  - Injection block absent when `EncounterSummaries` is empty
  - Injection block respects `MaxMilestonesToInject` limit
  - All `ArcCompletion` entries appear regardless of `MaxMilestonesToInject`
  - Session-level override takes precedence over global default
  - `MaxMilestonesToInject = 0` produces no milestones; arc completions still appear

Run all tests: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj -v minimal`
Expected: 607+ pass, 0 new failures.

---

## Manual Verification

1. Start the app: `.\helpers\start-webapp.ps1`
2. Create a new RP session with a scenario that has characters
3. Use the `dotnet run --project artifacts/tmp/dbquery` tool to confirm `RolePlayV2EncounterSummaries` table exists and is empty
4. Trigger 2–3 continuations to advance through BuildUp phase
5. Force a phase transition to Approaching — verify via dbquery that `PhaseMilestone` rows appear (one per character)
6. Complete a full arc through Climax to Reset — verify `ArcCompletion` rows appear with `TemplateSummary` populated
7. Wait ~30 seconds for LLM job to complete — verify `LlmSummary` is populated in the rows
8. Trigger a continuation in the second arc — inspect the prompt diagnostic log (or enable verbose logging) and verify "Session Memory:" block appears with arc 1 prose

**dbquery check** (after step 6):
```sql
SELECT SummaryType, CharacterId, FromPhase, ToPhase, CycleIndex,
       substr(TemplateSummary, 1, 80), LlmSummary IS NOT NULL
FROM RolePlayV2EncounterSummaries
WHERE SessionId = '<your-session-id>'
ORDER BY OccurredUtc;
```

Run as: `dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/check_encounter_summaries.sql`
