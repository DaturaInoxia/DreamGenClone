# Implementation Plan: Stat-Driven Character Instruction Text & Encounter Dimension Drift

**Branch**: `001-stat-char-text-drift` | **Date**: 2026-05-30 | **Spec**: [specs/001-stat-char-text-drift/spec.md](spec.md)
**Backlog**: B-043

## Summary

Each canonical stat (5 stats after reducing from 7) has four narrative text bands selected by runtime value. Out-of-neutral stats produce a synthesized per-character constraint sentence injected alongside the behavioral frame on every continuation. Canonical stat changes drift the encounter dimensions that underpin the behavioral frame, keeping the frame coherent with the character's evolving state. Stat reduction (remove Tension, Connection) is a prerequisite step.

**Technical approach**: Extend `CharacterStatProfileV2` with a `RuntimeEncounterStats` dictionary; introduce two new static catalogs (`CharacterStatTextCatalog` and `StatToDimensionMappings`); wire drift into `ApplyTrackedDelta` and the two `DecisionPointService`/UI mutation paths; route runtime snapshots through `ScenarioGuidanceInput` → `IBehavioralFrameGenerator` → prompt injection sites.

## Technical Context

**Language/Version**: C# / .NET 9  
**Primary Dependencies**: Blazor Server, SQLite (raw ADO.NET), xUnit, FluentAssertions, Serilog  
**Storage**: SQLite — `DreamGenClone.Web/data/dreamgenclone.dev.db`; no schema change (new data serialises in existing `CharacterSnapshotsJson` JSON column)  
**Testing**: xUnit + FluentAssertions — `DreamGenClone.Tests/DreamGenClone.Tests.csproj`  
**Target Platform**: Local desktop (Blazor Server, single-user)  
**Project Type**: Feature addition across existing 5-project layered architecture (Domain → Application → Infrastructure → Web → Tests)  
**Performance Goals**: No change from baseline; drift calculation is in-memory arithmetic  
**Constraints**: No EF Core; all DB via raw ADO.NET; no new DB columns; existing JSON column reused  
**Scale/Scope**: Single-user; per-interaction stat mutation pipeline; approximately 30 implementation steps across 5 projects

## Constitution Check

*Re-evaluated post-design: all gates pass.*

- [x] Local-first runtime preserved — no cloud dependencies added; all new computation is in-memory arithmetic and local DB reads
- [x] Module boundaries explicit — new catalogs in `Domain.StoryAnalysis`; new runtime state in `Domain.RolePlay`; infrastructure consumes domain; no layer inversion
- [x] .NET layered architecture with enforced dependency direction — Domain ← Application ← Infrastructure ← Web
- [x] Deterministic state transitions — drift is deterministic (slope × delta, clamped); JSON contract is stable; drift state serializes in existing column
- [x] SQLite persistence — existing column reused; no schema change
- [x] Serilog logging — all new code paths emit structured Serilog logs at appropriate levels (FR-024–FR-026)
- [x] Logging coverage — Information logs at every major execution point; Debug logs for drift calculations

## Project Structure

### Documentation (this feature)

```text
specs/001-stat-char-text-drift/
├── plan.md              # This file
├── research.md          # Phase 0 research findings
├── data-model.md        # Entity definitions, drift rules, state transitions
├── quickstart.md        # Build, test, verification commands
└── tasks.md             # Phase 2 output (speckit.tasks — not yet created)
```

### Source Code

```text
DreamGenClone.Domain/
├── RolePlay/
│   └── CharacterStatProfileV2.cs          [modified] add RuntimeEncounterStats; remove Tension, Connection
└── StoryAnalysis/
    ├── CharacterStatTextCatalog.cs         [new] 4-band text definitions for 5 stats × 3 roles
    └── StatToDimensionMappings.cs          [new] drift rules for Wife and Husband

DreamGenClone.Application/
└── StoryAnalysis/
    ├── AdaptiveStatCatalog.cs              [modified] remove Tension and Connection entries
    └── Models/
        └── ScenarioEngineContracts.cs      [modified] remove AverageTension/AverageConnection; add CharacterRuntimeStats, CharacterStatStateTexts
    └── Abstractions/
        └── IBehavioralFrameGenerator.cs    [modified] add optional characterRuntimeStats parameter

DreamGenClone.Infrastructure/
└── StoryAnalysis/
    ├── CharacterBehavioralFrameGenerator.cs [modified] use RuntimeEncounterStats when present
    ├── ScenarioGuidanceContextFactory.cs   [modified] build CharacterStatStateTexts; pass runtime stats to frame generator
    └── RolePlay/
        └── ScenarioGuidanceGenerator.cs    [modified] remove AverageTension/AverageConnection from cheating formula

DreamGenClone.Web/
├── Application/RolePlay/
│   ├── RolePlayAdaptiveStateService.cs     [modified] wire drift into ApplyTrackedDelta; remove Tension/Connection mutation rules
│   ├── RolePlayContinuationService.cs      [modified] remove AverageTension/AverageConnection from ScenarioGuidanceInput; add CharacterRuntimeStats; inject CharacterStatStateTexts at both prompt sites
│   └── RolePlayAssistantPrompts.cs         [modified] inject stat state text after behavioral frame for each character
└── Components/Pages/
    └── RolePlayWorkspace.razor             [modified] remove average tension/connection local vars

DreamGenClone.Tests/
└── RolePlay/
    ├── StatTextBandResolutionTests.cs       [new]
    ├── EncounterDimensionDriftTests.cs      [new]
    ├── BehavioralFrameWithRuntimeStatsTests.cs [new]
    └── CheatingFormulaSimplificationTests.cs  [new or modified]
```

---

## Implementation Phases

### Phase 1 — Stat Reduction (Prerequisites)

Removes Tension and Connection from all layers. All existing tests must pass after this phase. No new behavior added.

---

**Step 1 — Remove Tension and Connection from AdaptiveStatCatalog**

*File*: `DreamGenClone.Application/StoryAnalysis/AdaptiveStatCatalog.cs`

- Remove the `Tension` and `Connection` entries from the `CanonicalStats` array
- `CanonicalStatNames` derives from `CanonicalStats` automatically — no separate change needed
- `CreateDefaultStatMap()` and `NormalizeComplete()` now produce 5-stat maps

*Validation*: `AdaptiveStatCatalog.CanonicalStatNames` contains exactly `["Desire", "Restraint", "Dominance", "Loyalty", "SelfRespect"]`

---

**Step 2 — Remove Tension and Connection from CharacterStatProfileV2**

*File*: `DreamGenClone.Domain/RolePlay/CharacterStatProfileV2.cs`

- Remove `public int Tension { get; set; }`
- Remove `public int Connection { get; set; }`
- Add `public Dictionary<string, int>? RuntimeEncounterStats { get; set; }`

*Validation*: Build succeeds. Existing CharacterSnapshotsJson with Tension/Connection fields deserialises without error (System.Text.Json ignores unknown properties).

---

**Step 3 — Remove AverageTension and AverageConnection from ScenarioGuidanceInput**

*File*: `DreamGenClone.Application/StoryAnalysis/Models/ScenarioEngineContracts.cs`

- Remove `double AverageTension` from `ScenarioGuidanceInput` record
- Remove `double AverageConnection` from `ScenarioGuidanceInput` record
- Add `IReadOnlyDictionary<string, CharacterStatProfileV2>? CharacterRuntimeStats` to `ScenarioGuidanceInput`
- Add `IReadOnlyDictionary<string, string> CharacterStatStateTexts` to `ScenarioGuidanceContext`

Fix all call sites (compiler-driven): `ScenarioGuidanceInput` is a positional record; all constructors must be updated to remove the two removed parameters and supply `CharacterRuntimeStats: null` for now.

---

**Step 4 — Remove Tension/Connection from ScenarioGuidanceInput construction in RolePlayContinuationService**

*File*: `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`

- Remove `AverageTension:` and `AverageConnection:` arguments from the `new ScenarioGuidanceInput(...)` constructor call (lines ~791–813)
- Pass `CharacterRuntimeStats: null` for now (wired in Phase 5)
- Remove the two local average computation expressions for Tension/Connection

---

**Step 5 — Simplify cheating pressure formula**

*File*: `DreamGenClone.Infrastructure/RolePlay/ScenarioGuidanceGenerator.cs`

- Change: `var cheatingPressure = averageLoyalty - (averageDesire / 2.0) + (averageRestraint / 2.0) - (averageTension / 3.0);`
- To: `var cheatingPressure = averageLoyalty - (averageDesire / 2.0) + (averageRestraint / 2.0);`
- Remove any `averageTension` or `averageConnection` input parameters from the generating method signature; update callers

---

**Step 6 — Remove Tension/Connection mutation rules from RolePlayAdaptiveStateService**

*File*: `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs`

- Remove keyword category entries for Tension (`["fear","caught","risk","panic","nervous"]`) and Connection (`["safe","comfort","trust","reassure"]`)
- Remove theme affinity processing for `Tension` and `Connection` stat names (where `normalized` would have resolved to those names — now `ResolveSupportedStatName` returns unknown, so they are already ignored, but remove the entries from the seeded keyword catalog)
- Remove semantic event mapping entries for Tension and Connection from DB seed data (`RPSemanticStatMapping` table rows)
- Remove any explicit Tension/Connection average calculations in `RolePlayWorkspace.razor` (lines ~2087–2104 for `tension` and `connection` averages)

*Note*: `ResolveSupportedStatName` already ignores unknown stat names. Removing the source entries is clean-up rather than functional change.

---

**Step 7 — Build and verify stat reduction**

Run `dotnet build DreamGenClone.sln -v minimal` and resolve all remaining compile errors. Run `dotnet test` — all existing tests must pass. Verify zero references to `Tension` or `Connection` stat fields in non-test, non-spec code via `grep -r "\"Tension\"\|\"Connection\"" --include="*.cs"`.

---

### Phase 2 — CharacterStatTextCatalog

---

**Step 8 — Create CharacterStatTextCatalog**

*File*: `DreamGenClone.Domain/StoryAnalysis/CharacterStatTextCatalog.cs` (new file)

Implement:
- `record CharacterStatBand(string StatName, string TargetRole, string Band1Text, string Band2Text, string Band3Text, string Band4Text)`
- `static class CharacterStatTextCatalog`
  - `static readonly IReadOnlyList<CharacterStatBand> Entries` — 15 entries (see data-model.md for full text)
  - `static string? ResolveText(string statName, string targetRole, int value)` — returns band text; null for unknown combination
  - `static bool IsNeutralBand(int value)` — returns `value is >= 35 and <= 65`
  - Internal private lookup dictionary keyed by `(StatName, TargetRole)` — case-insensitive

Band threshold resolution:
```csharp
return value <= 20 ? entry.Band1Text
     : value <= 50 ? entry.Band2Text
     : value <= 75 ? entry.Band3Text
     : entry.Band4Text;
```

Log at Debug when resolving (stat, role, value, band).

---

**Step 9 — Unit tests for CharacterStatTextCatalog**

*File*: `DreamGenClone.Tests/RolePlay/StatTextBandResolutionTests.cs` (new file)

Tests:
- Band boundary resolution at values 20, 21, 50, 51, 75, 76, 100
- All 15 combinations return non-null, non-empty string
- `IsNeutralBand(35)` → true; `IsNeutralBand(65)` → true; `IsNeutralBand(34)` → false; `IsNeutralBand(66)` → false
- Unknown stat name → null
- Unknown role → null
- Case-insensitive lookup ("desire" vs "Desire", "wife" vs "Wife")

---

### Phase 3 — StatToDimensionMappings & RuntimeEncounterStats Infrastructure

---

**Step 10 — Create StatToDimensionMappings**

*File*: `DreamGenClone.Domain/StoryAnalysis/StatToDimensionMappings.cs` (new file)

Implement:
- `record DimensionDriftRule(string StatName, string TargetRole, string DimensionName, double Slope, int Floor, int Ceiling)`
- `static class StatToDimensionMappings`
  - `static readonly IReadOnlyList<DimensionDriftRule> AllRules`
  - `static IReadOnlyList<DimensionDriftRule> GetRules(string targetRole)` — returns filtered list; empty for OtherMan
  - `static void ApplyDelta(Dictionary<string, int> encounterStats, string targetRole, string statName, int statDelta)`
    - For each matching rule: `encounterStats[dim] = Math.Clamp(current + (int)Math.Round(rule.Slope * statDelta), rule.Floor, rule.Ceiling)`
    - No-op if `statDelta == 0`
    - Log at Debug for each dimension change

Wife rules (8 — see data-model.md):
- Desire/Exhibitionism +0.30, Desire/DiscoveryCaution -0.20
- Restraint/DiscoveryCaution +0.30, Restraint/Exhibitionism -0.20, Restraint/PostEncounterGuilt +0.15
- SelfRespect/DiscoveryCaution +0.20
- Loyalty/EmotionalEngagement +0.20, Loyalty/PostEncounterGuilt +0.25

Husband rules (6 — see data-model.md):
- Dominance/Acceptance -0.35, Dominance/Voyeurism -0.25, Dominance/Participation -0.20, Dominance/Encouragement -0.25
- SelfRespect/Acceptance -0.20, SelfRespect/Encouragement -0.20

---

**Step 11 — Unit tests for StatToDimensionMappings**

*File*: `DreamGenClone.Tests/RolePlay/EncounterDimensionDriftTests.cs` (new file)

Tests:
- Wife: Desire +10 → Exhibitionism increases by 3, DiscoveryCaution decreases by 2
- Wife: Restraint +10 → DiscoveryCaution increases by 3, Exhibitionism decreases by 2
- Husband: Dominance -8 → Acceptance increases by 2, Voyeurism increases by 2
- Clamp: value already at 100 + positive delta → stays 100
- Clamp: value already at 0 + negative delta → stays 0
- Zero delta → no change to any dimension
- OtherMan → empty rule list; ApplyDelta is no-op

---

**Step 12 — RuntimeEncounterStats initialization helper**

*File*: `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs` (or new helper class)

Add private method `InitializeRuntimeEncounterStatsIfNeeded(CharacterStatProfileV2 snapshot, string targetRole, string characterId, IReadOnlyDictionary<string, string> characterEncounterProfileIds, IReadOnlyDictionary<string, CharacterProfile> profileCache)`:
- If `snapshot.RuntimeEncounterStats` is non-null and non-empty → return (already initialized)
- Look up `characterEncounterProfileIds[characterId]` → `CharacterProfile`
- If profile found → `snapshot.RuntimeEncounterStats = new Dictionary<string, int>(profile.EncounterStats, StringComparer.OrdinalIgnoreCase)`
- If no profile → initialize using `BehavioralDimensionCatalog.GetDimensions(targetRole)` → all values = 50
- Log at Information: "Initialized RuntimeEncounterStats for {CharacterId} from {Source}"

---

### Phase 4 — Wire Drift into AdaptiveStateService

---

**Step 13 — Add drift hook to ApplyTrackedDelta**

*File*: `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs`

After the `CharacterStatProfileV2Accessor.ApplyDelta(profile, statName, delta)` call inside `ApplyTrackedDelta`:
1. Call `InitializeRuntimeEncounterStatsIfNeeded(profile, targetRole, characterId, ...)`
2. Call `StatToDimensionMappings.ApplyDelta(profile.RuntimeEncounterStats!, targetRole, statName, delta)`

The method already has access to the profile and stat name. The character role must be available from the session's character list — inject `IReadOnlyList<ScenarioCharacter>` (or look it up from session context passed to `UpdateFromInteractionAsync`).

*Design note*: `ApplyTrackedDelta` is a local function or private method inside `UpdateFromInteractionAsync`. It already captures session state variables via closure or parameter. Pass the role-lookup source via closure from the outer method.

---

**Step 14 — Wire drift in DecisionPointService and UI path**

*File*: `DreamGenClone.Infrastructure/RolePlay/DecisionPointService.cs` (lines ~579, ~600)

After each `CharacterStatProfileV2Accessor.ApplyDelta(...)` call in `ApplyDeltas()`:
- Call `StatToDimensionMappings.ApplyDelta(snapshot.RuntimeEncounterStats!, targetRole, statName, delta)` with initialization guard as in Step 13.

*File*: `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` (line ~3548 SetStat manual edit path)

After `CharacterStatProfileV2Accessor.SetStat(profile, statName, value)`:
- Compute `delta = value - oldValue`
- Call `StatToDimensionMappings.ApplyDelta(profile.RuntimeEncounterStats!, targetRole, statName, delta)` with initialization guard.

---

### Phase 5 — Behavioral Frame Generator Uses Runtime Stats

---

**Step 15 — Update IBehavioralFrameGenerator interface**

*File*: `DreamGenClone.Application/StoryAnalysis/Abstractions/IBehavioralFrameGenerator.cs`

Change signature:
```csharp
Task<IReadOnlyDictionary<string, string>> GenerateFramesAsync(
    IReadOnlyDictionary<string, string> characterEncounterProfileIds,
    IReadOnlyList<ScenarioCharacter> characters,
    IReadOnlyDictionary<string, CharacterStatProfileV2>? characterRuntimeStats = null,
    CancellationToken cancellationToken = default);
```

---

**Step 16 — Update CharacterBehavioralFrameGenerator**

*File*: `DreamGenClone.Infrastructure/StoryAnalysis/CharacterBehavioralFrameGenerator.cs`

- Accept the new `characterRuntimeStats` parameter in `GenerateFramesAsync`
- In `BuildFrameText()` (or at the call site in `GenerateFramesAsync`): when `characterRuntimeStats` contains a non-null, non-empty `RuntimeEncounterStats` for a character, use `RuntimeEncounterStats` values for dimension stat lookup instead of `profile.EncounterStats`
- Preserve exact fallback: when `RuntimeEncounterStats` is null or empty, use `profile.EncounterStats` as before
- Log at Debug: "Using RuntimeEncounterStats for {CharacterId} frame generation" vs "Using static EncounterStats for {CharacterId} frame generation"

---

**Step 17 — Update ScenarioGuidanceContextFactory**

*File*: `DreamGenClone.Infrastructure/StoryAnalysis/ScenarioGuidanceContextFactory.cs`

In `CreateFromGeneratorAsync`:
1. Forward `input.CharacterRuntimeStats` to `_frameGenerator.GenerateFramesAsync(..., characterRuntimeStats: input.CharacterRuntimeStats, ...)`
2. Build `CharacterStatStateTexts`: for each character in `input.Characters` that has a runtime snapshot in `input.CharacterRuntimeStats`:
   - Collect all stats where `value = CharacterStatProfileV2Accessor.GetStatOrDefault(snapshot, statName, 50)` is NOT in neutral band
   - Resolve band text from `CharacterStatTextCatalog.ResolveText(statName, character.Role, value)` for each
   - Concatenate with `"; "` separator
   - If no out-of-neutral stats → skip this character
3. Return updated `ScenarioGuidanceContext` with `CharacterStatStateTexts`

Log at Information: "Built CharacterStatStateTexts for {Count} characters with out-of-neutral stats"

---

**Step 18 — Pass CharacterRuntimeStats from RolePlayContinuationService**

*File*: `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`

In the `new ScenarioGuidanceInput(...)` construction (previously Step 4, now updated from `null`):

```csharp
CharacterRuntimeStats: session.AdaptiveState.CharacterStats.Count == 0
    ? null
    : session.AdaptiveState.CharacterStats
        .ToDictionary(
            kvp => BuildCharacterLabel(kvp.Key, session), // same label-building as CharacterBehavioralFrames keys
            kvp => kvp.Value,
            StringComparer.OrdinalIgnoreCase)
```

*Note*: The label must match the keys used in `CharacterBehavioralFrames` so that `CharacterStatStateTexts` and `CharacterBehavioralFrames` can be co-located in prompt injection. The character label builder already exists; reuse it.

---

**Step 19 — Unit tests for behavioral frame with runtime stats**

*File*: `DreamGenClone.Tests/RolePlay/BehavioralFrameWithRuntimeStatsTests.cs` (new file)

Tests:
- Frame generation with `RuntimeEncounterStats = null` → uses static `profile.EncounterStats` (existing behavior unchanged)
- Frame generation with `RuntimeEncounterStats` = drifted values → tier text resolves from drifted values, not static profile
- Frame generation with `RuntimeEncounterStats = {}` (empty) → falls back to static profile (per FR-016)
- `ScenarioGuidanceContext.CharacterStatStateTexts` is empty when all stats in neutral band
- `ScenarioGuidanceContext.CharacterStatStateTexts` contains entry for character with out-of-neutral stat

---

### Phase 6 — Synthesized Stat State Text Injection

---

**Step 20 — Inject CharacterStatStateTexts at site 2 in RolePlayContinuationService**

*File*: `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`

At the second HARD CONSTRAINT injection site (~line 1205 in the current implementation):

```csharp
foreach (var (label, frameText) in guidanceContext.CharacterBehavioralFrames)
{
    sb.AppendLine($"HARD CONSTRAINT — enforce in this response: {label} behavioral frame: {frameText}");
    if (guidanceContext.CharacterStatStateTexts.TryGetValue(label, out var statStateText)
        && !string.IsNullOrWhiteSpace(statStateText))
    {
        sb.AppendLine($"HARD CONSTRAINT — enforce in this response: {label} current state: {statStateText}");
    }
}
```

---

**Step 21 — Inject CharacterStatStateTexts at site 1 in RolePlayAssistantPrompts**

*File*: `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs`

In `AppendScenarioGuidance`, after the behavioral frame line for each character:

```csharp
foreach (var (label, frameText) in guidance.CharacterBehavioralFrames)
{
    promptBuilder.AppendLine($"HARD CONSTRAINT — {label} behavioral frame (authoritative, overrides all theme notes and guidance): {frameText}");
    if (guidance.CharacterStatStateTexts.TryGetValue(label, out var statStateText)
        && !string.IsNullOrWhiteSpace(statStateText))
    {
        promptBuilder.AppendLine($"HARD CONSTRAINT — {label} current state (authoritative, overrides all theme notes and guidance): {statStateText}");
    }
}
```

---

**Step 22 — Handle missing CharacterStatStateTexts gracefully**

Both injection sites check `TryGetValue` — no null-reference risk. The `CharacterStatStateTexts` property on `ScenarioGuidanceContext` is typed as `IReadOnlyDictionary<string, string>` (never null); the factory always returns an empty dict when no character has out-of-neutral stats.

Ensure all factory paths (`CreateFromGeneratorAsync` and `CreateFallbackAsync`) populate `CharacterStatStateTexts`:
- `CreateFallbackAsync` → always returns empty `Dictionary<string, string>()` for `CharacterStatStateTexts`

---

**Step 23 — End-to-end smoke test in the app**

Manual verification (no automated test for this step):
1. Start a session with a Wife character with Desire=82, Restraint=12, Loyalty=15
2. Post a turn
3. Check Serilog log output for HARD CONSTRAINT lines
4. Expected: behavioral frame line followed by stat state text line for Wife character

---

### Phase 7 — Persistence Verification

---

**Step 24 — Verify RuntimeEncounterStats serializes in CharacterSnapshotsJson**

After applying a stat delta in a running session, use the DB query tool:

```powershell
dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/dbquery/queries/inspect_runtime_encounter_stats.sql <session-id>
```

Expected: `RuntimeEncounterStats` JSON property present in character snapshot. Values should differ from the bound profile's static `EncounterStats`.

---

**Step 25 — Verify cross-session persistence**

1. Note drifted `RuntimeEncounterStats` values in DB
2. Close and reopen session
3. Post one turn
4. Check prompt log — stat state text must reflect the saved drifted values, not the original profile values

---

**Step 26 — Verify profile rebind resets RuntimeEncounterStats**

1. With drift applied, change the character's encounter profile in the workspace
2. Query DB — `RuntimeEncounterStats` should now match the new profile's `EncounterStats` exactly

---

### Phase 8 — Tests

---

**Step 27 — Cheating formula simplification test**

*File*: `DreamGenClone.Tests/RolePlay/CheatingFormulaSimplificationTests.cs` (new or update existing)

Verify: `Loyalty=70, Desire=60, Restraint=50` → formula produces `65` (moderate-high loyalty pressure). Verify: no `Tension` or `Connection` terms appear in formula.

---

**Step 28 — Stat reduction regression tests**

Verify all existing tests in `DreamGenClone.Tests` pass after removing Tension/Connection. Update any test that references `AverageTension`, `AverageConnection`, or `CharacterStatProfileV2.Tension/.Connection` — either remove the test or update it to reflect the 5-stat model.

---

**Step 29 — Integration test: drift → frame generation pipeline**

*File*: `DreamGenClone.Tests/RolePlay/EncounterDimensionDriftTests.cs` or a new integration test

Test the full path:
1. Start with a Wife character snapshot, Desire=20 (Band1)
2. Apply Desire delta +65 → Desire=85 (Band4)
3. Verify `RuntimeEncounterStats.Exhibitionism` increased by `Round(0.30 × 65)` = 20 (clamped)
4. Verify `ScenarioGuidanceContextFactory` produces `CharacterStatStateTexts` with Band4 Desire text for Wife

---

**Step 30 — Final build and test**

```powershell
dotnet build DreamGenClone.sln -v minimal
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj -v normal
```

Zero build errors. Zero test failures. Zero remaining references to `Tension` or `Connection` in non-spec, non-test C# code.

---

## Complexity Tracking

No constitution violations. Feature adds two new static catalogs and extends an existing runtime model — all within the established pattern of `BehavioralDimensionCatalog`.

---

## Dependency Map

```
Phase 1 (stat reduction)
    │
    ├── Steps 1–7 must complete before any Phase 2+ work
    │
Phase 2 (CharacterStatTextCatalog)
    │
    ├── Step 8 (catalog) → Step 9 (tests)
    │
Phase 3 (StatToDimensionMappings + RuntimeEncounterStats infra)
    │
    ├── Step 10 (mappings) → Step 11 (tests)
    ├── Step 12 (init helper) — depends on Step 10
    │
Phase 4 (wire drift)
    │
    ├── Steps 13–14 — depend on Steps 10, 12
    │
Phase 5 (frame generator)
    │
    ├── Steps 15–16 — depend on Step 13
    ├── Step 17 (factory) — depends on Steps 8, 15, 16
    ├── Step 18 (continuation service) — depends on Step 3
    ├── Step 19 (tests) — depends on Steps 15–17
    │
Phase 6 (injection)
    │
    ├── Steps 20–22 — depend on Steps 17–18
    ├── Step 23 (smoke test) — depends on Steps 20–21
    │
Phase 7 (persistence verification)
    │
    ├── Steps 24–26 — depend on Phase 6 complete
    │
Phase 8 (final tests)
    │
    └── Steps 27–30 — depend on all prior phases
```

