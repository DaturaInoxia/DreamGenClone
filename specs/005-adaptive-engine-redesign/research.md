# Research: Adaptive Engine Redesign

**Phase**: 0 — Outline & Research
**Date**: 2026-04-10
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

---

## R-001: Current Theme Scoring Architecture

**Decision**: Replace the hardcoded `ThemeRule[]` array in `RolePlayAdaptiveStateService` with a data-driven `ThemeCatalog` table in SQLite.

**Rationale**: The current implementation embeds 10 theme definitions (id, label, keywords[], weight, category) as a static C# array. Adding, editing, or disabling a theme requires code changes. A data-driven catalog enables runtime CRUD via the UI and satisfies FR-001 through FR-010.

**Alternatives Considered**:
- JSON config file — rejected: SQLite already used for all other profiles; adding a second data store adds complexity
- In-memory-only catalog with save-to-disk — rejected: SQLite already supports this pattern uniformly

**Current Code**: `RolePlayAdaptiveStateService.cs` (Lines ~18-80): static `ThemeRule[]` with 10 entries. `EnsureThemeCatalog()` prunes session state to match static catalog.

**Migration Path**: Convert each `ThemeRule` to a `ThemeCatalogEntry` row. Add `EnsureThemeCatalogFromDb()` that reads from SQLite. First run seeds from the same 10 defaults.

---

## R-002: Stat Rename Strategy (Arousal→Desire, Inhibition→Restraint, Trust→Connection, Agency→Dominance)

**Decision**: Rename stats at the domain model level and add a legacy-name-mapping dictionary for backward-compatible deserialization. Persist old stat names in sessions that already exist; normalize on load.

**Rationale**: Renaming improves domain clarity. A mapping dictionary handles existing persisted data without schema migration. System.Text.Json custom converter or post-deserialization fixup normalizes old names.

**Alternatives Considered**:
- Double-write both old and new names — rejected: increases storage and confusion
- SQL migration to rewrite all session JSON — rejected: fragile for blob-stored JSON fields; normalize-on-load is safer

**Affected Files**:
- `AdaptiveStatCatalog.cs` — stat name constants
- `RolePlayAdaptiveStateService.cs` — references "Arousal", "Inhibition" by string
- `RolePlayStyleResolver.cs` — references "Arousal" stat by string
- All tests referencing stat names

**Legacy Mapping**:
```csharp
private static readonly Dictionary<string, string> LegacyStatNames = new(StringComparer.OrdinalIgnoreCase)
{
    ["Arousal"] = "Desire",
    ["Inhibition"] = "Restraint",
    ["Trust"] = "Connection",
    ["Agency"] = "Dominance"
};
```

---

## R-003: RankingProfile → ThemeProfile Rename

**Decision**: Rename `RankingProfile` to `ThemeProfile` across the entire codebase. Rename the SQLite table from `RankingProfiles` to `ThemeProfiles` with an `ALTER TABLE RENAME` migration.

**Rationale**: "ThemeProfile" accurately describes the entity's purpose (grouping theme preferences). "RankingProfile" is a legacy name from an earlier design iteration.

**Alternatives Considered**:
- Keep "RankingProfile" internally and only rename in UI — rejected: creates permanent naming divergence between UI and code
- Create a new ThemeProfiles table and migrate data — rejected: SQLite supports `ALTER TABLE RENAME TO` directly

**Affected Entities**:
- `RankingProfile.cs` → `ThemeProfile.cs` (Domain)
- `RankingCriteriaService.cs` → `ThemePreferenceService.cs` or similar
- `SqlitePersistence.cs` — table rename + all CRUD methods
- `RolePlaySession.SelectedRankingProfileId` → `SelectedThemeProfileId`
- `Scenario.DefaultRankingProfileId` → `DefaultThemeProfileId`
- `RolePlayContinuationService.cs` — validation and prompt assembly references
- `RolePlayAssistantService.cs` — context line output
- `RolePlayAssistantPrompts.cs` — documentation text
- Blazor components: `RankingProfiles.razor` → `ThemeProfiles.razor`

**Migration SQL**:
```sql
ALTER TABLE RankingProfiles RENAME TO ThemeProfiles;
```

---

## R-004: StyleProfile Extension (ThemeAffinities, EscalatingThemeIds, StatBias)

**Decision**: Add three new columns to the `StyleProfiles` table: `ThemeAffinities` (JSON text), `EscalatingThemeIds` (JSON text), `StatBias` (JSON text). Use JSON serialization for dictionary/list types — matching existing patterns in the codebase.

**Rationale**: StyleProfile needs to influence theme scoring (affinities), escalation behavior (which themes escalate), and stat initialization (bias). JSON columns in SQLite are the established pattern in this codebase for complex-typed fields.

**Alternatives Considered**:
- Separate join tables — rejected: over-normalized for single-user local app with <10 profiles
- Separate affinity table — rejected: adds query complexity for no benefit at this scale

**Schema Addition**:
```sql
ALTER TABLE StyleProfiles ADD COLUMN ThemeAffinities TEXT NOT NULL DEFAULT '{}';
ALTER TABLE StyleProfiles ADD COLUMN EscalatingThemeIds TEXT NOT NULL DEFAULT '[]';
ALTER TABLE StyleProfiles ADD COLUMN StatBias TEXT NOT NULL DEFAULT '{}';
```

**Domain Model Changes**:
```csharp
public sealed class StyleProfile
{
    // ... existing: Id, Name, Description, Example, RuleOfThumb
    public Dictionary<string, int> ThemeAffinities { get; set; } = new(); // theme-id → weight (-5..+5)
    public List<string> EscalatingThemeIds { get; set; } = [];            // theme-ids that escalate
    public Dictionary<string, int> StatBias { get; set; } = new();        // stat-name → additive bias
}
```

---

## R-005: Scenario Seeding Pipeline (SeedFromScenarioAsync)

**Decision**: Add `SeedFromScenarioAsync(RolePlaySession, Scenario, CancellationToken)` to `IRolePlayAdaptiveStateService`. Called from `RolePlayEngineService.CreateSessionAsync` after session creation but before first interaction.

**Rationale**: The spec requires scenario-specific seed state (FR-020). Currently `CreateSessionAsync` sets `SelectedRankingProfileId` but does not populate `AdaptiveState.ThemeTracker` or initial stat values. The seeding method will:
1. Read `Scenario.BaseStatProfileId` → resolve base stats → write to `AdaptiveState.Stats`
2. Read `Scenario.Style.StyleProfileId` → load StyleProfile → apply StatBias additively
3. Read per-character `BaseStats` overrides → merge additively
4. Initialize `ThemeTrackerState` from active catalog entries (setting Score=0, Blocked flag from HardDealBreaker themes)

**Alternatives Considered**:
- Seed lazily on first interaction — rejected: initial interaction would have stale/empty state, poor UX
- Seed in Scenario creation — rejected: seeding is session-specific, not scenario-specific

**Call Site**: `RolePlayEngineService.CreateSessionAsync`, after session object creation, before returning.

---

## R-006: HardDealBreaker Permanent Block Behavior

**Decision**: When a ThemePreference has `Tier = HardDealBreaker`, the corresponding `ThemeTrackerItem` gets `Blocked = true` at seed time. Blocked themes are **never scored** — the scoring loop skips them entirely. A `SuppressedHitCount` counter increments each time content would have matched (for debug visibility). Log at `Debug` level.

**Rationale**: Hard dealbreakers represent absolute content boundaries. Scoring them (even to zero) risks leaking influence into adjacent logic. Permanent block with a suppression counter provides auditability without scoring leakage.

**Implementation**:
```csharp
// In ThemeTrackerItem (domain model)
public bool Blocked { get; set; }
public int SuppressedHitCount { get; set; }

// In scoring loop
if (item.Blocked) { item.SuppressedHitCount++; continue; }
```

---

## R-007: Profile-Driven Style Resolution

**Decision**: Refactor `RolePlayStyleResolver.ResolveEffectiveStyle` to consume profile data instead of hardcoded escalation checks. The method currently checks `IsEscalatingTheme()` against 5 hardcoded theme IDs. Replace with `StyleProfile.EscalatingThemeIds` list.

**Rationale**: Hardcoded escalation themes cannot be customized per style profile. Data-driven escalation enables different style profiles to define different escalation behaviors.

**Current Code** (hardcoded):
```csharp
private static bool IsEscalatingTheme(string themeId) =>
    themeId is "dominance" or "power-dynamics" or "forbidden-risk"
            or "humiliation" or "infidelity";
```

**New Code** (profile-driven):
```csharp
private static bool IsEscalatingTheme(string themeId, IReadOnlyList<string> escalatingThemeIds) =>
    escalatingThemeIds.Contains(themeId, StringComparer.OrdinalIgnoreCase);
```

**Alternatives Considered**:
- Keep hardcoded list as fallback — rejected: creates two code paths; seed "Sultry" profile with the current 5 IDs instead

---

## R-008: Theme Catalog Validation Rules

**Decision**: Theme catalog entry IDs must match `^[a-z0-9]+(-[a-z0-9]+)*$`, max 50 characters, unique across all entries (including disabled). Validation at service layer (IThemeCatalogService). Auto-suggest slug from Name; editable before first save, then locked.

**Rationale**: Slugs ensure stable references from ThemePreference, StyleProfile.ThemeAffinities, and EscalatingThemeIds. Locking after creation prevents orphaned references. Service-layer validation ensures consistency regardless of entry point (UI or API).

**Validation**:
```csharp
private static readonly Regex CatalogIdPattern = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

public static bool IsValidCatalogId(string id) =>
    !string.IsNullOrWhiteSpace(id) && id.Length <= 50 && CatalogIdPattern.IsMatch(id);
```

---

## R-009: ThemePreference Migration (Name → CatalogId Linking)

**Decision**: Existing `ThemePreference` records have `Name` (freetext string) but no `CatalogId` foreign key. Migration strategy: on startup/first-load, match existing `ThemePreference.Name` against `ThemeCatalogEntry.Label` (case-insensitive). If match found, set `CatalogId`. If no match, flag as `Unlinked` and log at `Information` level. Unlinked preferences remain functional but display a warning badge in the UI.

**Rationale**: Destructive migration (deleting unlinked) risks losing user customizations. Validate-on-load with flagging preserves data while guiding users to resolve mismatches.

**Schema Addition**:
```sql
ALTER TABLE ThemePreferences ADD COLUMN CatalogId TEXT NOT NULL DEFAULT '';
```

---

## R-010: StatAffinities Default Values for Built-in Themes

**Decision**: Each of the 10 built-in theme catalog entries will have curated default `StatAffinities` as defined in the spec clarifications. These are seeded into the `ThemeCatalog` table on first run.

**Defaults** (theme-id → stat → modifier):
| Theme | Desire | Restraint | Connection | Dominance |
|---|---|---|---|---|
| intimacy | +2 | 0 | +2 | 0 |
| dominance | 0 | -1 | 0 | +3 |
| forbidden-risk | +1 | -2 | 0 | +1 |
| emotional-vulnerability | 0 | +1 | +3 | -1 |
| romantic-tension | +2 | +1 | +1 | 0 |
| power-dynamics | 0 | -1 | -1 | +3 |
| humiliation | -1 | -2 | -2 | +2 |
| infidelity | +1 | -2 | -1 | 0 |
| obsessive-devotion | +2 | 0 | +2 | -1 |
| reluctant-submission | +1 | +2 | 0 | -2 |

---

## R-011: MustHave Theme Tier Behavior

**Decision**: `MustHave` themes get a +3 persistent affinity bonus applied during `SeedFromScenarioAsync`. This bonus is additive with StyleProfile.ThemeAffinities and InteractionEvidence scoring. The bonus **does not** bypass natural scoring — it primes the theme to score higher when content matches.

**Rationale**: MustHave is a strong preference signal, not a forced override. It accelerates convergence toward preferred themes without artificially inflating scores beyond what content supports.

---

## R-012: Existing Persistence Pattern Compliance

**Decision**: All new persistence code follows the established `SqlitePersistence.cs` patterns:
- `CREATE TABLE IF NOT EXISTS` in `EnsureTables()`
- `ALTER TABLE` with try/catch for idempotent column additions
- UPSERT via `ON CONFLICT(Id) DO UPDATE SET`
- All date fields stored as ISO 8601 strings
- Parameterized queries with named `$param` syntax
- `Save*Async` / `Load*Async` / `LoadAll*Async` / `Delete*Async` pattern

**Rationale**: Consistency with existing codebase patterns avoids introducing new conventions and reduces cognitive load for future maintenance.
