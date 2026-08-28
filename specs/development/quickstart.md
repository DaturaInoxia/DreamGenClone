# Quickstart: B-042 — Unify Character Stats Profiles with Encounter Behavior Profiles

*Phase 1 output — implementation order, key gotchas, and test strategy*

---

## Recommended Implementation Order

Follow this strict bottom-up order. Each step is a buildable checkpoint. Do not skip steps or combine across layers.

### Step 1 — Domain: New entities and catalog (no breaking changes)

**Files to create**:
- `DreamGenClone.Domain/StoryAnalysis/CharacterProfile.cs`
- `DreamGenClone.Domain/StoryAnalysis/BehavioralDimensionCatalog.cs`

**Mark as Obsolete** (do not delete yet):
- `DreamGenClone.Domain/StoryAnalysis/BaseStatProfile.cs` — add `[Obsolete("Replaced by CharacterProfile — B-042")]`
- `DreamGenClone.Domain/StoryAnalysis/HusbandAwarenessProfile.cs` — add `[Obsolete("Replaced by CharacterProfile — B-042")]`

**Key gotcha**: `BehavioralDimension` should be a `sealed record` (not struct) to avoid value-copy issues when used in collections. The catalog fields can be `private static readonly IReadOnlyList<BehavioralDimension>` with all 14 dimension definitions inline.

**Build check**: Solution builds without errors after this step.

---

### Step 2 — Domain: Update AdaptiveScenarioState

**File**: `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`

- Replace `public string? HusbandAwarenessProfileId { get; set; }` with:
  ```csharp
  public Dictionary<string, string> CharacterEncounterProfileIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  ```

**Key gotcha**: `CharacterEncounterProfileIds` must be initialized to an empty dictionary (not null) so the JSON serializer round-trips correctly. The existing `CharacterStats` property uses the same initialization pattern.

**Build check**: This will cause compile errors in files that reference `HusbandAwarenessProfileId` — expected. Fix all compile errors before proceeding.

Files that reference `HusbandAwarenessProfileId` (all must be updated as part of this step):
- `DreamGenClone.Infrastructure/RolePlay/RolePlayEngineService.cs` (line ~296)
- `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` (line ~1441)
- `DreamGenClone.Infrastructure/RolePlay/SemanticInteractionAnalysisJobHandler.cs` (line ~234)
- `DreamGenClone.Tests/RolePlay/AdaptiveScenarioStateV2RoundTripTests.cs`
- `DreamGenClone.Tests/RolePlay/SessionThemeSelectionsTests.cs`

---

### Step 3 — Application: Update contracts

**Files**:
- `DreamGenClone.Application/RolePlay/RolePlayContracts.cs` — replace `HusbandAwarenessProfileId` with `CharacterEncounterProfileIds + Characters`; replace `HusbandAwarenessFrame` with `CharacterBehavioralFrames`
- `DreamGenClone.Application/StoryAnalysis/Models/ScenarioEngineContracts.cs` — same replacements

**Create new interfaces**:
- `DreamGenClone.Application/StoryAnalysis/Abstractions/ICharacterProfileService.cs`
- `DreamGenClone.Application/StoryAnalysis/Abstractions/IBehavioralFrameGenerator.cs`

**Mark as Obsolete**:
- `IBaseStatProfileService`
- `IHusbandAwarenessProfileService`

**Build check**: Compile errors will appear in Infrastructure and Web layers that use the old contract types. Expected — fix in subsequent steps.

---

### Step 4 — Infrastructure: Persistence layer

**File**: `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`

4a. Add `CharacterProfiles` table creation (in the `EnsureTablesAsync` / startup section)  
4b. Add `SaveCharacterProfileAsync`, `LoadCharacterProfileAsync`, `LoadAllCharacterProfilesAsync`, `DeleteCharacterProfileAsync`  
4c. Add migration logic (in order per data-model.md):
  - Delete "Balanced Baseline" BaseStatProfile
  - Migrate BaseStatProfiles → CharacterProfiles (INSERT OR IGNORE)
  - Migrate HusbandAwarenessProfiles → CharacterProfiles (INSERT OR IGNORE with json_object for EncounterStats)
  - ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CharacterEncounterProfileIdsJson (guarded with PRAGMA check)
4d. Update `RolePlayStateRepository` to serialize/deserialize `CharacterEncounterProfileIds` from the new column; add backward-compat migration in the load path for sessions with `HusbandAwarenessProfileId` set

**Key gotcha**: The `json_object()` SQL function for the HusbandAwarenessProfile migration requires SQLite 3.38+. Verify with `dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/check_sqlite_version.sql`. If too old, do the migration in C# code (load profiles, map to CharacterProfile objects, save).

**Key gotcha**: `INSERT OR IGNORE` on migration means re-running startup is safe. Do NOT use `INSERT OR REPLACE` — that would overwrite user edits to migrated profiles.

---

### Step 5 — Infrastructure: New services

**Create**:
- `DreamGenClone.Infrastructure/StoryAnalysis/CharacterProfileService.cs` — implements `ICharacterProfileService`, includes `EnsureDefaultsAsync` that seeds all 25 archetypes
- `DreamGenClone.Infrastructure/StoryAnalysis/CharacterBehavioralFrameGenerator.cs` — implements `IBehavioralFrameGenerator`, uses `BehavioralDimensionCatalog` for tier resolution

**Update**:
- `DreamGenClone.Infrastructure/StoryAnalysis/ScenarioGuidanceContextFactory.cs` — replace `HusbandAwarenessProfileId` → `CharacterEncounterProfileIds + Characters` flow; replace frame field reference
- `DreamGenClone.Infrastructure/RolePlay/ScenarioGuidanceGenerator.cs` — remove `BuildHusbandAwarenessInterpretationAsync`; the new `IBehavioralFrameGenerator` handles this
- `DreamGenClone.Infrastructure/RolePlay/RolePlayEngineService.cs` — update write site (loop over `request.CharacterEncounterProfileIds` → `session.AdaptiveState.CharacterEncounterProfileIds`)
- `DreamGenClone.Infrastructure/RolePlay/SemanticInteractionAnalysisJobHandler.cs` — update state preservation to copy `CharacterEncounterProfileIds` instead of `HusbandAwarenessProfileId`

**Mark as Obsolete**:
- `DreamGenClone.Infrastructure/StoryAnalysis/BaseStatProfileService.cs`
- `DreamGenClone.Infrastructure/StoryAnalysis/HusbandAwarenessProfileService.cs`

---

### Step 6 — Web: Prompt injection

**Files** (update both injection sites per contracts/ScenarioGuidanceContracts.md):
- `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` — update `BuildScenarioGuidanceSection`
- `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — update HARD CONSTRAINT block before writing directive

**Build check**: Solution builds and all existing tests that don't test profile selection should still pass.

---

### Step 7 — Web: ThemeProfiles.razor

Replace the two separate tabs (`base-stats` and existing husband-awareness profiles tab) with a single `character-profiles` tab.

**UI Structure**:
- Left column: filterable list (filter dropdown: All / Husband / Wife / OtherMan)
- Right column: edit form with two groups:
  - **Character Stats** (7 sliders, same as current base-stats form)
  - **Encounter Behavior** (role-specific dimension sliders, only shown when TargetRole != "Any")
  - **Additional Notes** textarea
  - **Full Override** checkbox (shown only when AdditionalNotes is non-empty)
  - **Live Preview** panel showing generated frame text, updates on every slider input via `@oninput`

**Key gotcha**: The live preview should call `BehavioralDimensionCatalog.ResolveTierText()` directly in the Blazor component (it's a synchronous static call, no service needed). Do NOT make an async service call on every slider move.

**Key gotcha**: When the user changes `TargetRole` in the form, clear `EncounterStats` dictionary and re-initialize with the new role's dimensions at default value 50. Otherwise the stored dimension keys won't match the new role.

---

### Step 8 — Web: RolePlayCreate.razor

Replace the separate husband-awareness profile picker and per-character base stat profile pickers with a single unified profile picker per character.

**Change**: In Step 3 of the session creation wizard, for each character:
- Single `<select>` showing `CharacterProfile` entries filtered by character's role
- On selection: preview shows the profile's canonical stats AND behavioral dimensions
- On "Apply": seeds character's stats from `CharacterProfile.CharacterStats` AND stores the profile ID in the session's `CharacterEncounterProfileIds`

**Field rename**: `_awarenessProfileId` → `_characterEncounterProfileIds` (Dictionary<characterId, profileId>)

**Key gotcha**: The current `ApplyCharacterStatProfile()` method applies only `DefaultStats`. The new version must: (1) apply `CharacterStats` to the character's stat fields, AND (2) add the profile ID to `_characterEncounterProfileIds`. Both happen on the "Apply" button click.

---

### Step 9 — Web: RolePlayWorkspace.razor

Replace the single `HusbandAwarenessProfileId` change handler with a per-character profile switcher in the adaptive panel.

**Change**: For each character shown in the adaptive panel, add a profile picker showing profiles filtered by character role. On change: update `_session.AdaptiveState.CharacterEncounterProfileIds[charId]`.

---

### Step 10 — Web: DI registration (Program.cs)

Remove old service registrations, add new ones per contracts/ScenarioGuidanceContracts.md.

---

### Step 11 — Tests

Run the full test suite and fix failures. Expected failures are all in existing tests that reference `HusbandAwarenessProfileId` — all already identified in research.md R8.

Create new test file `DreamGenClone.Tests/StoryAnalysis/BehavioralDimensionCatalogTests.cs` covering:
- Tier 1 at boundary (value=20)
- Tier 2 at boundary (value=21, value=50)
- Tier 3 at boundary (value=51, value=75)
- Tier 4 at boundary (value=76, value=100)
- All 3 roles resolve without null returns
- Unknown dimension name returns empty string (not exception)

---

### Step 12 — Seed archetypes

The `EnsureDefaultsAsync` in `CharacterProfileService` is called from app startup. After first run, verify with:
```
dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/check_character_profiles.sql
```

Expected: 25 profiles (8 Husband + 9 Wife + 8 OtherMan).

---

## Key Gotchas Summary

| # | Gotcha | Mitigation |
|---|---|---|
| 1 | `INSERT OR IGNORE` vs `INSERT OR REPLACE` in migration | Use `INSERT OR IGNORE` only — preserves user edits to migrated data |
| 2 | `json_object()` SQL requires SQLite 3.38+ | Check version; fallback to C# migration if old SQLite |
| 3 | Live preview performance in Blazor | Use synchronous `BehavioralDimensionCatalog` calls (no async), no debounce needed |
| 4 | TargetRole change clears EncounterStats | Wipe and re-initialize encounter dims on role change in the form |
| 5 | `CharacterEncounterProfileIds` must not be null | Initialize to `new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)` in property initializer |
| 6 | HusbandAwarenessProfile migration gets neutral (50) canonical stats | Document in UI tooltip; existing migrated husband profiles need manual stat updates |
| 7 | FullOverride + empty AdditionalNotes → use generated text | Enforce this in `CharacterBehavioralFrameGenerator`, not in the entity itself |
| 8 | Old Obsolete services still registered at DI time | Remove old registrations in Program.cs in Step 10 — compile errors will catch stragglers |
| 9 | SemanticInteractionAnalysisJobHandler preserves profile IDs across state saves | Must copy entire `CharacterEncounterProfileIds` dictionary, not just one field |
| 10 | Tests may run while app is running (file lock) | Stop the web app before running tests — `dotnet test` will fail on locked DLLs |

---

## Verification Checklist

Before marking B-042 as implemented:

- [ ] `dotnet build DreamGenClone.sln` — zero errors, zero warnings for B-042 code
- [ ] `dotnet test DreamGenClone.Tests` — all tests pass
- [ ] DB: 25 profiles in `CharacterProfiles` table (verify with dbquery)
- [ ] DB: No rows lost — old `BaseStatProfiles` count matches migrated count minus "Balanced Baseline"
- [ ] DB: All 4 existing husband awareness profiles appear in `CharacterProfiles` with `TargetRole="Husband"`
- [ ] UI: "Character Profiles" tab shows in ThemeProfiles page (no old tabs)
- [ ] UI: Profile form shows live preview updating on slider move
- [ ] UI: Session creation picker shows unified profiles per character role
- [ ] Prompt: Continuation prompt contains HARD CONSTRAINT blocks for each character with bound profile
- [ ] Prompt: Each block is labeled with character name
- [ ] Prompt: Character with no bound profile produces no HARD CONSTRAINT block
- [ ] Session: Existing session with `HusbandAwarenessProfileId` loads without error and generates husband frame
