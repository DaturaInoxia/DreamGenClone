# Quickstart: Adaptive Engine Redesign

**Feature Branch**: `005-adaptive-engine-redesign`
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Data Model**: [data-model.md](data-model.md)

---

## What This Feature Does

Replaces the hardcoded theme scoring engine with a data-driven Theme Catalog and introduces profile-driven style resolution. Renames stats and profile types for domain clarity.

## Key Changes at a Glance

| Area | Before | After |
|---|---|---|
| Theme definitions | Hardcoded `ThemeRule[]` (10 entries) | SQLite `ThemeCatalog` table with CRUD UI |
| Theme scoring | Static keyword matching | Catalog-driven + StyleProfile affinity modifiers |
| Escalation check | 5 hardcoded theme IDs | `StyleProfile.EscalatingThemeIds` (configurable) |
| Stat names | Arousal, Inhibition, Trust, Agency | Desire, Restraint, Connection, Dominance |
| Profile naming | RankingProfile | ThemeProfile |
| Session seeding | None (empty initial state) | `SeedFromScenarioAsync` (stats + themes) |
| HardDealBreaker | Blocks scoring | Blocks scoring + tracks SuppressedHitCount |
| StyleProfile | 4 text fields | + ThemeAffinities, EscalatingThemeIds, StatBias |

## Implementation Order

### Phase 1: Foundation (no behavior change)
1. **Stat rename** — Update `AdaptiveStatCatalog`, add legacy mapping. Update all string references.
2. **RankingProfile → ThemeProfile rename** — Domain model, persistence, session/scenario fields, Blazor components.
3. **ThemeCatalogEntry domain model** — New entity in `DreamGenClone.Domain`.
4. **ThemeCatalog SQLite table** — `CREATE TABLE` + seed 10 defaults in `EnsureTables()`.
5. **IThemeCatalogService** — Interface in Application, implementation in Infrastructure.
6. **Tests** — ThemeCatalogService CRUD + seed + validation.

### Phase 2: Scoring Pipeline
7. **ThemePreference.CatalogId** — Schema migration, auto-link logic.
8. **StyleProfile extensions** — 3 new columns, update save/load, seed "Sultry" defaults.
9. **SeedFromScenarioAsync** — New method on IRolePlayAdaptiveStateService. Wire into CreateSessionAsync.
10. **AdaptiveStateService refactor** — Replace hardcoded catalog with IThemeCatalogService injection, apply affinity modifiers.
11. **HardDealBreaker blocking** — Blocked flag, SuppressedHitCount, skip-in-scoring.
12. **Tests** — Seeding, affinity scoring, blocked themes.

### Phase 3: Style Resolution
13. **Profile-driven escalation** — Replace `IsEscalatingTheme()` hardcoded list with StyleProfile.EscalatingThemeIds.
14. **MustHave affinity bonus** — +3 persistent bonus during seeding.
15. **Debug panel updates** — Show SuppressedHitCount, Blocked icon for dealbreaker themes.
16. **Tests** — Style resolver profile-driven tests.

### Phase 4: UI
17. **Theme Catalog management UI** — New tab on ThemeProfiles.razor (or dedicated component).
18. **Unlinked preference badge** — Warning display in ThemePreference list.
19. **Terminology updates** — Assistant prompts, context lines, documentation strings.

## Development Environment

```powershell
# Build
dotnet build DreamGenClone.sln

# Run tests
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj

# Run app (hot reload)
dotnet watch --project DreamGenClone.Web/DreamGenClone.csproj
```

## Key Files to Understand First

| File | Why |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs` | Core scoring engine being refactored |
| `DreamGenClone.Web/Application/RolePlay/RolePlayStyleResolver.cs` | Style resolution with escalation logic |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Session creation (seeding call site) |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | All persistence (table schemas + CRUD) |
| `DreamGenClone.Web/Domain/RolePlay/RolePlayAdaptiveState.cs` | ThemeTrackerState + ThemeTrackerItem models |

## Testing Strategy

- **Unit tests**: All new services (`ThemeCatalogService`, scoring with affinities, seeding pipeline)
- **Rename safety**: Existing tests updated with new stat/profile names; compile = pass
- **Legacy compat**: Deserialize sessions with old stat names → verify normalization
- **Edge cases**: Empty catalog, all themes blocked, unlinked preferences, duplicate IDs
