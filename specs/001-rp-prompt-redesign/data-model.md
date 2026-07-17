# Phase 1 Data Model: RP Prompt Redesign

**Branch**: `001-rp-prompt-redesign` | **Date**: 2026-07-17

This document defines the entities, fields, validation rules, and state transitions introduced by the RP Prompt Redesign. All persisted config is UI-backed (repo Hard Rule) with fail-fast diagnostics on missing/invalid values.

---

## Domain Layer (`DreamGenClone.Domain/RolePlay/`)

### `PromptZone` (enum)

```csharp
public enum PromptZone { A, B, C }
```

- **A** = Primacy (scene grounding, never trimmed)
- **B** = Context (world + history, trimmable per priority)
- **C** = Recency (directives + instruction, never trimmed except where noted)

### `PromptVariant` (enum)

```csharp
public enum PromptVariant { Character, Narrative }
```

### `ActorProfileKind` (enum)

```csharp
public enum ActorProfileKind { Player, NpcPresent, NpcNonPresent, Narrative, Custom }
```

### `PromptSlotId` (enum)

```csharp
public enum PromptSlotId
{
    SceneAnchor = 1,           // Zone A, order 1
    ActorAssignment = 2,       // Zone A, order 2
    TurnContext = 3,           // Zone A, order 3
    SceneLocationLock = 4,     // Zone A, order 4
    WorldState = 5,            // Zone A, order 4a (conditional sub-slot)
    CharacterData = 6,         // Zone B, order 5
    ScenarioContext = 7,       // Zone B, order 6
    CurrentLocation = 8,       // Zone B, order 7
    WritingStyle = 9,          // Zone B, order 8
    InteractionHistory = 10,   // Zone B, order 9
    SessionMemory = 11,        // Zone B, order 10
    SceneContinuityAnchor = 12,// Zone B, order 11
    ThemeContract = 13,        // Zone C, order 12
    BehavioralFrames = 14,     // Zone C, order 13
    ScenarioGuidance = 15,     // Zone C, order 14
    IntensityPacing = 16,      // Zone C, order 15
    UserDirection = 17,        // Zone C, order 16 (conditional)
    FinalInstruction = 18      // Zone C, order 17
}
```

**Validation**: Startup asserts exactly 17 mandatory slots registered (WorldState is conditional, not counted in the 17). Zone/order mapping is frozen per spec contract.

---

## Application Layer (`DreamGenClone.Web/Application/RolePlay/Prompts/`)

### `IPromptSlot` (interface)

```csharp
public interface IPromptSlot
{
    PromptSlotId Id { get; }
    PromptZone Zone { get; }
    int Order { get; }
    bool IsTrimEligible { get; }
    bool ShouldWrite(PromptBuildContext context);
    Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct);
    string Trim(string text, int maxChars);
}
```

**Validation**:
- `WriteAsync` MUST NOT throw for a context where `ShouldWrite` returned true (fail-fast contract).
- Result MUST NOT contain leading/trailing newlines (builder handles spacing).
- `Trim` MUST be idempotent and MUST NOT produce empty output from non-empty input.

### `PromptBuildContext` (immutable record)

```csharp
public sealed record PromptBuildContext
{
    public required RolePlaySession Session { get; init; }
    public required ActorProfile ActorProfile { get; init; }
    public required PromptVariant Variant { get; init; }
    public required string Phase { get; init; }           // current NarrativePhase name
    public required int? TurnIndex { get; init; }
    public required int? PositionInTurn { get; init; }
    public required int? TurnActorCount { get; init; }
    public required string PromptText { get; init; }      // user direction (may be generic default)
    public required int MaxPromptChars { get; init; }     // resolved, fail-fast if missing
    public WorldStateData? WorldState { get; init; }      // null until B-062
    public required ResolvedScenarioData Scenario { get; init; }
    public required ResolvedThemeData Theme { get; init; }
    public required ResolvedIntensityData Intensity { get; init; }
    public required ResolvedWritingStyleData WritingStyle { get; init; }
    public required IReadOnlyList<EncounterSummaryRecord> EncounterSummaries { get; init; }
    public required IReadOnlyList<RolePlayInteraction> RecentInteractions { get; init; }
}
```

**Validation**: All `required` properties must be populated by the builder before any slot runs. `MaxPromptChars` must be > 0 — fail fast with diagnostic if missing/invalid (FR-004).

### `ActorProfile` (record)

```csharp
public sealed record ActorProfile
{
    public required ActorProfileKind Kind { get; init; }
    public required string ActorName { get; init; }
    public required string ActorRole { get; init; }
    public required IReadOnlyList<string> PresentCharacterIds { get; init; }
    public required IReadOnlyList<string> AllCharacterIds { get; init; }
}
```

### `WorldStateData` (record, conditional)

```csharp
public sealed record WorldStateData
{
    public int DayNumber { get; init; }
    public int? TotalDays { get; init; }
    public string? DayOfWeek { get; init; }
    public string? TimePhase { get; init; }
    public string? SpecificTime { get; init; }
    public string? WeatherCondition { get; init; }
    public decimal? TemperatureCelsius { get; init; }
    public string? HumidityDescription { get; init; }
    public string? WorldRhythm { get; init; }
    public string? TemporalPressure { get; init; }
}
```

### `RolePlayPromptBuilder`

```csharp
public sealed class RolePlayPromptBuilder
{
    public RolePlayPromptBuilder(
        IEnumerable<IPromptSlot> slots,
        PromptBudgetEnforcer budgetEnforcer,
        ILogger<RolePlayPromptBuilder> logger);

    public async Task<string> BuildAsync(PromptBuildContext context, CancellationToken ct);
}
```

**Behavior**:
1. Sort slots by `Zone` then `Order`.
2. For each slot: if `ShouldWrite(context)` is true, call `WriteAsync` and append.
3. After all slots produce text, run `PromptBudgetEnforcer.Enforce(slotsText, context.MaxPromptChars)`.
4. Log at Information: `SessionId`, `Actor`, `Phase`, `Chars`, `SlotsFired` (FR-037).
5. Log at Warning on any trim: `SessionId`, `Actor`, `PreTrimChars`, `PostTrimChars`, `TrimmedSlots` (FR-030).

### `PromptBudgetEnforcer`

```csharp
public sealed class PromptBudgetEnforcer
{
    public BudgetEnforcementResult Enforce(
        IReadOnlyList<SlotText> slotTexts,
        int maxPromptChars);
}

public sealed record SlotText(PromptSlotId SlotId, string Text, bool IsTrimEligible);
public sealed record BudgetEnforcementResult(
    string FinalText,
    int PreTrimChars,
    int PostTrimChars,
    IReadOnlyList<string> TrimmedSlots);
```

**Trim priority** (FR-029): Slot 9 (oldest history) → Slot 5 (non-present char data) → Slot 6 (scenario metadata) → Slot 10 (session memory) → Slot 7 (location) → Slot 11 (continuity) → Slot 8 (writing style, last resort). Never trim: Slots 1-4, 4a, 12, 15, 16 (when present), 17.

---

## Session Config (`DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`)

New nullable properties (all seeded at session creation, fail-fast at runtime if missing/invalid):

| Property | Type | FR | Recommended Seed |
|----------|------|----|----|
| `MaxPromptChars` | `int?` | FR-004 | 35000 |
| `ContextWindowTurns` | `int?` | FR-015 | 8 |
| `ScenarioCompressionTurnThreshold` | `int?` | FR-012 | 10 |
| `HistoryFullDetailTurnBand` | `int?` | FR-015 | 3 |
| `HistoryNarrativeOnlyTurnBand` | `int?` | FR-015 | 3 |
| `SessionMemoryLongTermTurnThreshold` | `int?` | FR-016 | 10 |

**Validation**: All must be > 0 when non-null. Runtime fails fast with diagnostic (session ID, property name) if null or <= 0.

**State transitions**: None — these are static session config, set at creation, editable via UI (out of scope for this feature per "Out of Scope: UI changes for prompt configuration").

---

## Persistence (`DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`)

### `Sessions` table migrations (idempotent `ALTER TABLE`)

```sql
ALTER TABLE Sessions ADD COLUMN MaxPromptChars INTEGER NULL;
ALTER TABLE Sessions ADD COLUMN ContextWindowTurns INTEGER NULL;
ALTER TABLE Sessions ADD COLUMN ScenarioCompressionTurnThreshold INTEGER NULL;
ALTER TABLE Sessions ADD COLUMN HistoryFullDetailTurnBand INTEGER NULL;
ALTER TABLE Sessions ADD COLUMN HistoryNarrativeOnlyTurnBand INTEGER NULL;
ALTER TABLE Sessions ADD COLUMN SessionMemoryLongTermTurnThreshold INTEGER NULL;
```

Pattern matches existing `MaxMilestonesToInject` migration at `SqlitePersistence.cs:1227-1235`. `SessionService.SaveRolePlayAsync` extended to persist the new columns.

### New `PhaseRuleOfThumb` table

```sql
CREATE TABLE IF NOT EXISTS PhaseRuleOfThumb (
    Id TEXT NOT NULL PRIMARY KEY,
    Phase TEXT NOT NULL,
    RuleOfThumbText TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    UNIQUE(Phase)
);
```

Seeded with 6 rows (Opening, BuildUp, Committed, Approaching, Climax, Reset) from GAP-6 content. `IPhaseRuleOfThumbRepository` provides `GetByPhaseAsync(string phase)`.

---

## Encounter Enrichment (`DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs`)

### Enrichment prompt I/O (no schema change)

**Input** (to enrichment LLM):
- Narrative response text (primary source, FR-035)
- Character responses for the encounter (emotional/POV detail)
- Character name, encounter number, scene location

**Output** (stored in `EncounterSummaryRecord.LlmSummary`):
- 3-5 sentence first-person memory capturing 6 dimensions (FR-033):
  1. What happened (plot)
  2. What the character felt (emotional texture)
  3. What they learned (sexual self-knowledge)
  4. What changed (relationship dynamic)
  5. What risk was taken (near-miss/discovery)
  6. What the other character now knows

**Validation**: SC-009 requires at least 4 of 6 dimensions present in the output. The enrichment prompt explicitly requests all 6.

---

## Encounter Detection (`DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`)

### `TryDetectEncounterBoundaryAsync` — secondary signals (FR-034)

**State transition**: `AdaptiveScenarioState.CurrentEncounterNumber` increments on detection. No new state fields.

**New signals** (evaluated after existing LLM inference, OR-ed with primary signal):
1. Scene change after intimacy (location transition within N turns of `WasInSexScene=true`)
2. Significant time passage (time-skip markers in narrative response)
3. Explicit encounter boundary language ("when it was over", "after they dressed")
4. Phase transition Climax → Reset (always fires encounter summary)

**Validation**: Each secondary signal logs at Debug when evaluated and at Information when fired. No silent detection — every detection writes a `RolePlayDebugEventRecord` with `EventKind="EncounterBoundaryDetected"` and `MetadataJson.Signal` indicating which signal fired.

---

## Validation Summary

| Rule | Source | Enforcement |
|------|--------|-------------|
| Exactly 17 slots, frozen zone/order | FR-001, spec contract | Startup assertion in `RolePlayPromptBuilder` constructor |
| `MaxPromptChars` > 0, no default | FR-004 | `PromptBuildContext` construction fail-fast |
| All compression thresholds > 0, no default | FR-012a | `PromptBuildContext` construction fail-fast |
| Phase Rule-of-Thumb present | FR-014 | `WritingStyleSlot.WriteAsync` fail-fast |
| Profile default Rule-of-Thumb present | FR-014 | `WritingStyleSlot.WriteAsync` fail-fast |
| Actor found in roster | Edge Case | `ActorProfileResolver` fail-fast |
| Each slot independently testable | FR-036, SC-008 | Slot contract tests in `DreamGenClone.Tests/RolePlay/Prompts/` |
| No duplicate content categories | FR-027, SC-002 | `PromptBuilderTests` asserts single occurrence |
| No residual legacy path | SC-010 | `LegacyRemovalTests` asserts `BuildPromptAsync` deleted |
