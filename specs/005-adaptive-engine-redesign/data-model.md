# Data Model: Adaptive Engine Redesign

**Phase**: 1 — Design & Contracts
**Date**: 2026-04-10
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

---

## Entity Overview

```text
ThemeCatalogEntry ──────────┐
                            │ referenced by CatalogId
ThemeProfile ──┐            │
               │ ProfileId  │
ThemePreference ────────────┘
                            │
StyleProfile ───────────────┤ ThemeAffinities keys, EscalatingThemeIds
                            │
RolePlaySession ────────────┤ SelectedThemeProfileId, SelectedStyleProfileId
               │            │
RolePlayAdaptiveState       │
  ├── ThemeTrackerState ────┘ Per-theme scoring keyed by CatalogId
  └── Stats (Dictionary<string, int>)
                            │
Scenario ───────────────────┘ DefaultThemeProfileId, Style.StyleProfileId
  ├── Character[] (BaseStats)
  └── Style (references profile IDs)
```

---

## New Entities

### ThemeCatalogEntry

**Purpose**: A single theme definition in the data-driven catalog. Replaces the hardcoded `ThemeRule` struct.

| Field | Type | Constraints | Description |
|---|---|---|---|
| Id | `string` | PK, slug format `^[a-z0-9]+(-[a-z0-9]+)*$`, max 50, immutable after create | Stable identifier used in all references |
| Label | `string` | Required, max 100 | Human-readable display name |
| Description | `string` | Default `""` | Optional editorial description |
| Keywords | `List<string>` | JSON-serialized | Keywords for content matching |
| Weight | `int` | Default 1, range 1–10 | Multiplier for keyword scoring |
| Category | `string` | Default `""` | Grouping label (e.g., "Power", "Emotional") |
| StatAffinities | `Dictionary<string, int>` | JSON-serialized, values -5..+5 | Per-stat modifiers when theme scores high |
| IsEnabled | `bool` | Default `true` | Soft-delete / disable toggle |
| IsBuiltIn | `bool` | Default `false` | `true` for the 10 seed entries; prevents deletion |
| CreatedUtc | `DateTime` | Auto-set | Creation timestamp |
| UpdatedUtc | `DateTime` | Auto-set | Last modification timestamp |

**Validation Rules**:
- `Id` must be unique across all entries (including disabled)
- `Id` is auto-suggested from `Label` (slugified) but editable before first save, then locked
- `Keywords` may not be empty for enabled entries
- `Weight` clamped to 1–10

**State Transitions**: None (CRUD only; IsEnabled toggles active status)

---

## Modified Entities

### ThemeProfile (renamed from RankingProfile)

**Purpose**: Groups a set of ThemePreference records that define a user's content boundary profile.

| Field | Type | Change | Description |
|---|---|---|---|
| Id | `string` | Unchanged | PK (GUID hex) |
| Name | `string` | Unchanged | Display name |
| IsDefault | `bool` | Unchanged | Default profile flag |
| CreatedUtc | `DateTime` | Unchanged | |
| UpdatedUtc | `DateTime` | Unchanged | |

**Changes**: Rename only — `RankingProfile` → `ThemeProfile`, table `RankingProfiles` → `ThemeProfiles`.

---

### ThemePreference (existing, extended)

**Purpose**: A single theme preference within a ThemeProfile, now linked to a catalog entry.

| Field | Type | Change | Description |
|---|---|---|---|
| Id | `string` | Unchanged | PK (GUID hex) |
| ProfileId | `string` | Unchanged | FK → ThemeProfile.Id |
| Name | `string` | Unchanged | Legacy display name |
| Description | `string` | Unchanged | |
| Tier | `ThemeTier` | Unchanged | `MustHave`, `Like`, `Neutral`, `Dislike`, `HardDealBreaker` |
| CatalogId | `string` | **NEW** | FK → ThemeCatalogEntry.Id. Default `""` (unlinked) |
| CreatedUtc | `DateTime` | Unchanged | |
| UpdatedUtc | `DateTime` | Unchanged | |

**Validation Rules**:
- If `CatalogId` is non-empty, it must reference an existing ThemeCatalogEntry
- Unlinked preferences (`CatalogId == ""`) display a warning badge in UI
- On startup, auto-link by matching `Name` against `ThemeCatalogEntry.Label` (case-insensitive)

---

### StyleProfile (existing, extended)

**Purpose**: Prose guidance profile, now with theme-influence and stat-bias fields.

| Field | Type | Change | Description |
|---|---|---|---|
| Id | `string` | Unchanged | PK (GUID hex) |
| Name | `string` | Unchanged | Display name |
| Description | `string` | Unchanged | Prose description |
| Example | `string` | Unchanged | Example prose |
| RuleOfThumb | `string` | Unchanged | Writing guideline |
| ThemeAffinities | `Dictionary<string, int>` | **NEW** | theme-id → weight (-5..+5). Applied additively to theme scoring |
| EscalatingThemeIds | `List<string>` | **NEW** | Theme IDs that trigger escalation delta in style resolution |
| StatBias | `Dictionary<string, int>` | **NEW** | stat-name → additive bias. Applied during seeding after base stats |
| CreatedUtc | `DateTime` | Unchanged | |
| UpdatedUtc | `DateTime` | Unchanged | |

**Seed Data** ("Sultry" profile — the only existing default):
```csharp
ThemeAffinities = new() { ["intimacy"] = 2, ["romantic-tension"] = 2, ["emotional-vulnerability"] = 1 },
EscalatingThemeIds = ["dominance", "power-dynamics", "forbidden-risk", "humiliation", "infidelity"],
StatBias = new() { ["Desire"] = 1, ["Connection"] = 1 }
```

---

### ThemeTrackerItem (existing, extended)

**Purpose**: Per-theme runtime scoring state within a session's AdaptiveState.

| Field | Type | Change | Description |
|---|---|---|---|
| Score | `int` | Unchanged | Accumulated theme score |
| ThemeScoreBreakdown | `ThemeScoreBreakdown` | Unchanged | 4-signal breakdown |
| Blocked | `bool` | **NEW** | `true` if HardDealBreaker; never scored |
| SuppressedHitCount | `int` | **NEW** | Count of keyword hits on a blocked theme (debug only) |

---

### RolePlaySession (existing, renamed field)

| Field | Type | Change | Description |
|---|---|---|---|
| SelectedRankingProfileId | `string?` | **RENAME** → `SelectedThemeProfileId` | FK → ThemeProfile.Id |
| *(all other fields)* | | Unchanged | |

**Legacy Deserialization**: JSON converter or post-deserialize fixup maps `SelectedRankingProfileId` → `SelectedThemeProfileId`.

---

### Scenario (existing, renamed field)

| Field | Type | Change | Description |
|---|---|---|---|
| DefaultRankingProfileId | `string?` | **RENAME** → `DefaultThemeProfileId` | FK → ThemeProfile.Id |
| *(all other fields)* | | Unchanged | |

---

### Stats Dictionary Keys (renamed)

| Old Name | New Name |
|---|---|
| `Arousal` | `Desire` |
| `Inhibition` | `Restraint` |
| `Trust` | `Connection` |
| `Agency` | `Dominance` |

**Legacy Mapping**: `AdaptiveStatCatalog.NormalizeLegacyStatName(string)` method returns the new name for old names (case-insensitive), passthrough for already-current names.

---

## New Interface

### IRolePlayAdaptiveStateService (extended)

```csharp
public interface IRolePlayAdaptiveStateService
{
    Task<RolePlayAdaptiveState> UpdateFromInteractionAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken = default);

    Task SeedFromScenarioAsync(
        RolePlaySession session,
        Scenario scenario,
        CancellationToken cancellationToken = default);  // NEW
}
```

### IThemeCatalogService (new)

```csharp
public interface IThemeCatalogService
{
    Task<ThemeCatalogEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ThemeCatalogEntry>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
    Task SaveAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);  // blocked for IsBuiltIn
    Task SeedDefaultsAsync(CancellationToken cancellationToken = default);       // idempotent first-run seed
}
```

---

## SQLite Schema Changes

### New Table: ThemeCatalog

```sql
CREATE TABLE IF NOT EXISTS ThemeCatalog (
    Id TEXT PRIMARY KEY,
    Label TEXT NOT NULL,
    Description TEXT NOT NULL DEFAULT '',
    Keywords TEXT NOT NULL DEFAULT '[]',
    Weight INTEGER NOT NULL DEFAULT 1,
    Category TEXT NOT NULL DEFAULT '',
    StatAffinities TEXT NOT NULL DEFAULT '{}',
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    IsBuiltIn INTEGER NOT NULL DEFAULT 0,
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
```

### Migrations (idempotent ALTER TABLE)

```sql
-- Rename RankingProfiles → ThemeProfiles
ALTER TABLE RankingProfiles RENAME TO ThemeProfiles;

-- Add CatalogId to ThemePreferences
ALTER TABLE ThemePreferences ADD COLUMN CatalogId TEXT NOT NULL DEFAULT '';

-- Extend StyleProfiles
ALTER TABLE StyleProfiles ADD COLUMN ThemeAffinities TEXT NOT NULL DEFAULT '{}';
ALTER TABLE StyleProfiles ADD COLUMN EscalatingThemeIds TEXT NOT NULL DEFAULT '[]';
ALTER TABLE StyleProfiles ADD COLUMN StatBias TEXT NOT NULL DEFAULT '{}';
```

**Note**: All ALTER TABLE statements wrapped in try/catch for idempotency (column already exists → ignore), matching existing patterns in `SqlitePersistence.EnsureTables()`.

---

## Relationships

```text
ThemeProfile 1 ──── * ThemePreference  (via ProfileId)
ThemeCatalogEntry 1 ── 0..* ThemePreference  (via CatalogId, optional)
ThemeCatalogEntry 1 ── 0..* StyleProfile.ThemeAffinities  (via dict key)
ThemeCatalogEntry 1 ── 0..* StyleProfile.EscalatingThemeIds  (via list element)
ThemeProfile 1 ── 0..1 RolePlaySession  (via SelectedThemeProfileId)
ThemeProfile 1 ── 0..1 Scenario  (via DefaultThemeProfileId)
StyleProfile 1 ── 0..1 RolePlaySession  (via SelectedStyleProfileId)
StyleProfile 1 ── 0..1 Scenario.Style  (via StyleProfileId)
```
