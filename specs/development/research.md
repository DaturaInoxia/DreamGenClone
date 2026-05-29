# Research: B-042 — Unify Character Stats Profiles with Encounter Behavior Profiles

*Phase 0 output — all NEEDS CLARIFICATION items resolved*

---

## R1: SQLite Schema Migration Pattern (Two Tables → One)

**Decision**: In-place migration using the existing `SqlitePersistence` migration pattern — add the new `CharacterProfiles` table, run a data migration to INSERT rows from both source tables, then leave the old tables in place for one release as read-only historical reference (no FK dependencies). Add `CharacterEncounterProfileIdsJson` column to `RolePlayV2AdaptiveStates` in the same migration pass.

**Rationale**: `SqlitePersistence.cs` already has a pattern for ALTER TABLE column addition (lines ~1508–1515 where `HusbandAwarenessProfileId` was added). The same approach applies here:
1. `CREATE TABLE IF NOT EXISTS CharacterProfiles (...)` on app startup
2. Migration guard: `INSERT OR IGNORE INTO CharacterProfiles SELECT ... FROM BaseStatProfiles WHERE ...`  
3. Migration guard: `INSERT OR IGNORE INTO CharacterProfiles SELECT ... FROM HusbandAwarenessProfiles WHERE ...`  
4. `ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CharacterEncounterProfileIdsJson TEXT NULL` (guarded with `PRAGMA table_info` check)
5. After data migration: session load code reads `HusbandAwarenessProfileId` and synthesizes a `CharacterEncounterProfileIds` entry for backward compat during the same startup

**Old tables**: `BaseStatProfiles` and `HusbandAwarenessProfiles` are retained (read-only, no new writes) until a follow-up cleanup release. `EnsureDefaultsAsync` calls for them are removed so no new seed data is written.

**Alternatives considered**:
- Full DROP + recreate: rejected — risk of data loss if migration logic has bugs; old tables are cheap to keep
- Background migration job: overkill for local app with small data volume

---

## R2: BehavioralDimensionCatalog Pattern (code-defined static class)

**Decision**: Implement `BehavioralDimensionCatalog` as a `static class` in `DreamGenClone.Domain/StoryAnalysis/` following the same pattern as `AdaptiveStatCatalog`. It exposes:
- `IReadOnlyList<BehavioralDimension> GetDimensions(string targetRole)` — returns dimension definitions for a given role
- `BehavioralDimension? FindDimension(string targetRole, string name)` — single dimension lookup
- `string ResolveTierText(string targetRole, string name, int value)` — resolves tier text for a value
- Static readonly list of `BehavioralDimension` records (all roles) defined as field initializers

`BehavioralDimension` is a `readonly record struct` (or `sealed record`) with `Name`, `TargetRole`, `Tier1Text`, `Tier2Text`, `Tier3Text`, `Tier4Text` properties.

Both the Blazor profile CRUD form (for live preview) and `CharacterBehavioralFrameGenerator` (for prompt injection) call this same catalog — single source of truth.

**Rationale**: Matches existing codebase conventions. The `AdaptiveStatCatalog` in `DreamGenClone.Domain` is the established pattern for code-defined enumerations with engine meaning. Keeping it in Domain means all layers can reference it without cross-layer violations.

**Alternatives considered**:
- JSON file loaded at startup: adds file I/O complexity, no benefit for a small static dataset
- DB table with admin UI: explicitly out of scope per spec FR-003; would enable dimension text editing which creates consistency risks with the engine

---

## R3: Blazor Live Preview Pattern (range sliders → text update)

**Decision**: Use Blazor's `@oninput` event on range `<input>` elements (not `@onchange`) so the preview updates on every slider movement, not just on blur/release. Store all dimension values in a `Dictionary<string, int>` in component state. A computed property `PreviewText` calls `BehavioralDimensionCatalog.ResolveTierText` for each dimension and concatenates the results plus AdditionalNotes. StateHasChanged is called automatically by the `@oninput` handler updating the dictionary.

**Implementation note**: The existing `ThemeProfiles.razor` base-stats form already uses `GetBaseStatValue()/SetBaseStatValue()` helpers — the same helper pattern applies for `GetEncounterStatValue()/SetEncounterStatValue()`. The live preview panel is a `<div>` in the same form column showing the computed text.

**Rationale**: Native Blazor Server event binding; no JavaScript interop needed. The `@oninput` pattern is already used for real-time feedback in other parts of the codebase.

**Alternatives considered**:
- Debounced JS interop: unnecessary overhead for pure text computation
- Timer-based polling: fragile, adds latency

---

## R4: AdaptiveScenarioState JSON Serialization for Dictionary<string,string>

**Decision**: `Dictionary<string, string>` serializes correctly with `System.Text.Json` without any custom converters — it produces a standard JSON object `{"charId1":"profileId1"}`. The existing `CharacterStats` dictionary in `AdaptiveScenarioState` (keyed by CharacterId) confirms this pattern already works. The new `CharacterEncounterProfileIds` field follows the same pattern.

**Migration for existing sessions**: On session load in `RolePlayStateRepository`, if `CharacterEncounterProfileIdsJson` is NULL but `HusbandAwarenessProfileId` is set, synthesize a `CharacterEncounterProfileIds` entry: look up the husband character ID in the session's character list and map `{ husbandCharId → HusbandAwarenessProfileId }`. This is a one-time in-memory migration; the session is re-persisted with the new column populated.

**Session creation write site**: `RolePlayEngineService.cs` line 296 currently assigns `HusbandAwarenessProfileId`. This becomes a loop over `request.CharacterEncounterProfileIds` to populate `AdaptiveState.CharacterEncounterProfileIds`.

**Rationale**: Minimal change — no custom serialization; follows the same conventions as the existing snapshot dictionary.

---

## R5: HusbandAwarenessProfileId Write Sites (3 confirmed by research)

All three write sites identified — all must be updated:

1. **`RolePlayEngineService.cs` line 296** — session creation: replace with loop over `request.CharacterEncounterProfileIds`
2. **`RolePlayWorkspace.razor` line 1441** — mid-session change: replace with per-character picker that writes to `CharacterEncounterProfileIds[charId]`
3. **`SemanticInteractionAnalysisJobHandler.cs` line 234** — state preservation: replace `HusbandAwarenessProfileId` copy with `CharacterEncounterProfileIds` copy

---

## R6: Prompt Injection Pattern for Multi-Character Frames

**Decision**: Replace the single `HusbandAwarenessFrame` string with `IReadOnlyDictionary<string, string> CharacterBehavioralFrames` (keyed by character name/label). At injection sites:

```csharp
// Both in RolePlayAssistantPrompts.cs (early guidance section) and
// RolePlayContinuationService.cs (before writing directive):
foreach (var (characterLabel, frameText) in guidanceContext.CharacterBehavioralFrames)
{
    if (!string.IsNullOrWhiteSpace(frameText))
        sb.AppendLine($"HARD CONSTRAINT — {characterLabel} behavioral frame: {frameText}");
}
```

The `characterLabel` is the character's display name (e.g., "Wife — Sarah", "Husband — Michael") so the LLM knows which character each constraint applies to. The frames dictionary is populated by `ScenarioGuidanceGenerator.BuildCharacterBehavioralFramesAsync()` which iterates over the session's bound encounter profile IDs.

**Rationale**: Matching the existing double-injection pattern (early + immediately before writing directive) preserves proven behavior. Labeling each frame with character name ensures the LLM correctly attributes constraints to the right character.

---

## R7: Profile Entity Field Mapping (Old → New)

### HusbandAwarenessProfile → CharacterProfile (Husband role)

| Old Field | New Field | Notes |
|---|---|---|
| Id | Id | Preserved verbatim |
| Name | Name | Preserved |
| Description | Description | Preserved |
| AwarenessLevel | EncounterStats["Awareness"] | Direct value copy |
| AcceptanceLevel | EncounterStats["Acceptance"] | Direct value copy |
| VoyeurismLevel | EncounterStats["Voyeurism"] | Direct value copy |
| ParticipationLevel | EncounterStats["Participation"] | Direct value copy |
| HumiliationDesire | *DROPPED* | Dead code — never used in generation |
| EncouragementLevel | EncounterStats["Encouragement"] | Direct value copy |
| RiskTolerance | EncounterStats["RiskTolerance"] | Direct value copy |
| Notes | AdditionalNotes | Content preserved; no longer a bypass trigger |
| — | TargetRole = "Husband" | Set during migration |
| — | CharacterStats | All 7 stats defaulted to 50 (midpoint) — user must update |
| CreatedUtc | CreatedUtc | Preserved |
| UpdatedUtc | UpdatedUtc | Preserved |

### BaseStatProfile → CharacterProfile

| Old Field | New Field | Notes |
|---|---|---|
| Id | Id | Preserved verbatim |
| Name | Name | Preserved |
| Description | Description | Preserved |
| TargetGender | TargetGender | Preserved |
| TargetRole | TargetRole | Preserved |
| DefaultStatsJson | CharacterStatsJson | Deserialized, re-serialized |
| — | EncounterStatsJson | Empty dict `{}` — user fills encounter dims |
| CreatedUtc | CreatedUtc | Preserved |
| UpdatedUtc | UpdatedUtc | Preserved |

**Special case**: "Balanced Baseline" profile (TargetRole=Any/Unknown with non-canonical stat names) → DELETE during migration (FR-014).

---

## R8: Test Coverage Gaps to Address

Existing tests that must be updated:
- `AdaptiveScenarioStateV2RoundTripTests.cs` — replace `HusbandAwarenessProfileId` refs with `CharacterEncounterProfileIds`
- `ScenarioGuidanceGeneratorTests.cs` — replace husband-only frame tests with multi-character tests
- `SessionThemeSelectionsTests.cs` — update assertion `Assert.Equal("awareness-99", session.AdaptiveState.HusbandAwarenessProfileId)`
- `RolePlaySessionBaseStatInitializationTests.cs` — replace `BaseStatProfile` with `CharacterProfile`

New tests to add:
- `BehavioralDimensionCatalogTests.cs` — tier resolution for each role's dimensions (boundary conditions: 20, 21, 50, 51, 75, 76)
- `CharacterBehavioralFrameGeneratorTests.cs` — frame text generation including AdditionalNotes append and FullOverride cases
- Session backward-compat migration test — session with `HusbandAwarenessProfileId` loads correctly and synthesizes `CharacterEncounterProfileIds`
