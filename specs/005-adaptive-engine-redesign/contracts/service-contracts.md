# Service Contracts: Adaptive Engine Redesign

**Context**: DreamGenClone is a local-first Blazor Server application with no external API surface. These contracts define the internal service interfaces that compose the adaptive engine feature.

---

## IThemeCatalogService

**Layer**: Application (interface) → Infrastructure (implementation)
**Purpose**: CRUD + seed operations for the data-driven theme catalog.

```csharp
public interface IThemeCatalogService
{
    /// <summary>Retrieve a single catalog entry by its slug ID.</summary>
    Task<ThemeCatalogEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Retrieve all catalog entries. Set includeDisabled to also return soft-deleted entries.</summary>
    Task<IReadOnlyList<ThemeCatalogEntry>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);

    /// <summary>Insert or update a catalog entry. Validates ID format + uniqueness.</summary>
    /// <exception cref="ArgumentException">If ID format invalid or entry is built-in and deletion attempted.</exception>
    Task SaveAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Delete a catalog entry. Throws if IsBuiltIn is true.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Idempotent seed of the 10 built-in theme entries. Called on application startup.</summary>
    Task SeedDefaultsAsync(CancellationToken cancellationToken = default);
}
```

**Invariants**:
- `GetAllAsync(false)` returns only `IsEnabled == true` entries
- `SaveAsync` rejects IDs that don't match `^[a-z0-9]+(-[a-z0-9]+)*$`
- `DeleteAsync` throws `InvalidOperationException` for built-in entries
- `SeedDefaultsAsync` is safe to call multiple times (INSERT OR IGNORE pattern)

---

## IRolePlayAdaptiveStateService (extended)

**Layer**: Application (interface) → Web/Application (implementation)
**Purpose**: Session-level adaptive state management — seeding and per-interaction updates.

```csharp
public interface IRolePlayAdaptiveStateService
{
    /// <summary>Score themes and update stats based on a new interaction.</summary>
    Task<RolePlayAdaptiveState> UpdateFromInteractionAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initialize session adaptive state from scenario configuration.
    /// Sets base stats, applies style profile stat bias, seeds theme tracker,
    /// blocks HardDealBreaker themes, and applies MustHave affinity bonuses.
    /// </summary>
    Task SeedFromScenarioAsync(
        RolePlaySession session,
        Scenario scenario,
        CancellationToken cancellationToken = default);
}
```

**Seeding Order** (SeedFromScenarioAsync):
1. Load active theme catalog entries → initialize ThemeTrackerState
2. Resolve ThemeProfile → mark `Blocked = true` for HardDealBreaker themes
3. Apply MustHave +3 affinity bonus
4. Load BaseStatProfile → set initial stat values
5. Load StyleProfile → apply StatBias additively
6. Merge per-character BaseStats overrides additively

**Scoring Invariants** (UpdateFromInteractionAsync):
- Blocked themes are never scored; SuppressedHitCount incremented instead
- Per-theme score capped at 12 (existing behavior preserved)
- StyleProfile.ThemeAffinities applied additively to keyword scoring
- Stats updated using renamed keys (Desire, Restraint, Connection, Dominance)

---

## IThemePreferenceService (renamed from RankingCriteriaService)

**Layer**: Application (interface) → Infrastructure (implementation)
**Purpose**: CRUD for ThemePreference records within a ThemeProfile.

```csharp
public interface IThemePreferenceService
{
    Task<ThemePreference?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ThemePreference>> ListByProfileAsync(string profileId, CancellationToken cancellationToken = default);
    Task SaveAsync(ThemePreference preference, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Auto-link ThemePreferences to catalog entries by matching Name to Label.
    /// Flags unlinked preferences. Called on profile load.
    /// </summary>
    Task AutoLinkToCatalogAsync(string profileId, CancellationToken cancellationToken = default);
}
```

**Invariants**:
- `AutoLinkToCatalogAsync` is non-destructive — sets CatalogId on match, logs unlinked at Information level
- `ListByProfileAsync` returns ordered by UpdatedUtc descending (existing behavior)
