# Quickstart: Finishing Move System – Catalog and Matrix Redesign

**Branch**: `007-finishing-move-catalog`  
**For**: Developer starting implementation

---

## What this feature does

Replaces the single Finishing Move Matrix with a richer system: the matrix is kept (with a renamed dominance column and updated band ranges) and five new independently-filterable reference catalogs are added. All catalog entries filter by adaptive stat bands at runtime and inject labeled prompt sections during the climax phase.

---

## How to run the solution

```powershell
cd d:\src\DreamGenClone
.\helpers\start-webapp-dev.ps1
```

The app starts on `https://localhost:5001` (or as configured). On first run with an empty database, all seed services execute automatically.

---

## How to build and test

```powershell
# Build
dotnet build DreamGenClone.sln -v minimal

# Run all tests
dotnet test DreamGenClone.sln -v minimal

# Run only RolePlay tests
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "RolePlay" -v minimal
```

---

## Where to start implementation

### Step 1 – Domain models (no dependencies)

Add five new model classes to:
```
DreamGenClone.Domain/RolePlay/RPThemeModels.cs
```

Rename `DominanceBand` → `OtherManDominanceBand` in `RPFinishingMoveMatrixRow` (same file).  
Update the default value from `"50-74"` to `"30-59"`.

Models to add: `RPFinishLocation`, `RPFinishFacialType`, `RPFinishReceptivityLevel`, `RPFinishHisControlLevel`, `RPFinishTransitionAction`.  
See [data-model.md](../data-model.md) for all fields.

### Step 2 – Schema migration + new tables

In `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs`:

1. Add `MigrateFinishingMoveMatrixToV2Async` (private static, same signature as `MigrateLegacyMatrixTablesToGlobalAsync`):
   - Detect `DominanceBand` via `TableHasColumnAsync`
   - Archive → drop → recreation handled by `CREATE TABLE IF NOT EXISTS` below
2. Call it at the top of `EnsureSupplementalTablesAsync`, before existing CREATE TABLE blocks
3. Update the `RPFinishingMoveMatrixRows` CREATE TABLE block: rename column, update UNIQUE constraint
4. Append five new `CREATE TABLE IF NOT EXISTS` blocks after the existing ones

### Step 3 – Interface (parallel with step 2)

Add 15 new method signatures to:
```
DreamGenClone.Application/RolePlay/IRPThemeService.cs
```

See [contracts/IRPThemeService-additions.md](../contracts/IRPThemeService-additions.md) for all signatures.

### Step 4 – Service implementation (after steps 2+3)

In `RPThemeService.cs`:
- Implement all 15 CRUD methods, following the upsert-on-Id pattern of existing methods
- Update `SaveFinishingMoveMatrixRowAsync` and `ListFinishingMoveMatrixRowsAsync` to use `OtherManDominanceBand`
- Update `SaveFinishingMoveRowWithConnectionAsync` (private helper) for the renamed column

### Step 5 – Seed services (after step 4)

Add five new seed service files in `DreamGenClone.Infrastructure/RolePlay/`:
- `RPFinishLocationSeedService.cs`
- `RPFinishFacialTypeSeedService.cs`
- `RPFinishReceptivityLevelSeedService.cs`
- `RPFinishHisControlLevelSeedService.cs`
- `RPFinishTransitionActionSeedService.cs`

Update `FinishingMoveMatrixSeedService.cs`:
- Rename `SeedRow.DominanceBand` parameter
- Update band values from `75-100/50-74/0-49` to `60-100/30-59/0-29`

Register all five new seed services in `DreamGenClone.Web/Program.cs` (lines ~97-99, following the existing pattern).

### Step 6 – UI (parallel with step 5)

In `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor`:
- Add 5 new tabs: Locations, Facial Types, Receptivity Levels, His Control Levels, Transition Actions
- Each tab: display table + inline edit form, with `(any)` shown for empty eligibility fields
- Disabled entries shown greyed out in the table
- Matrix tab: rename "Dominance" header → "Other Man Dominance"; update form labels and bindings from `DominanceBand` → `OtherManDominanceBand`

### Step 7 – Prompt assembly (after step 4)

In `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`:
1. Update `MatchesFinishingMoveRow` → use `row.OtherManDominanceBand`
2. Update `AppendFinishingMoveMatrixContextAsync` → replace `dominance` stat read with `GetAverageAdaptiveStatOrFallback(..., "OtherManDominance", ...)`
3. Add `MatchesBandEligibility(string? eligibleBands, double stat)` helper (splits on comma, delegates to `MatchesNumericBand`)
4. Extend `AppendFinishingMoveMatrixContextAsync` with five catalog sections (after existing matrix section)

### Step 8 – Tests (parallel with step 6)

In `DreamGenClone.Tests/RolePlay/`:
- Add seed service tests for each of the 5 new services
- Update `FinishingMoveMatrixSeedServiceTests.cs` and `RPFinishingMoveMatrixServiceTests.cs` for renamed column + new band values

---

## Key file map

| What | Where |
|------|-------|
| Domain models | `DreamGenClone.Domain/RolePlay/RPThemeModels.cs` |
| Service interface | `DreamGenClone.Application/RolePlay/IRPThemeService.cs` |
| Service implementation | `DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs` |
| Matrix seed service | `DreamGenClone.Infrastructure/RolePlay/FinishingMoveMatrixSeedService.cs` |
| New catalog seed services | `DreamGenClone.Infrastructure/RolePlay/*.SeedService.cs` (5 new files) |
| Startup registration | `DreamGenClone.Web/Program.cs` |
| UI management | `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor` |
| Prompt assembly | `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` |
| Tests | `DreamGenClone.Tests/RolePlay/` |

---

## Verification checklist

- [ ] `dotnet build DreamGenClone.sln` passes with zero warnings
- [ ] `dotnet test` — all existing RP tests pass
- [ ] Fresh database startup: all 5 catalog tabs populated in Theme Profiles → Finishing Moves
- [ ] Matrix tab shows "Other Man Dominance Band" column header; band values are `0-29`/`30-59`/`60-100`
- [ ] RP session in climax phase: log shows labeled sections for each eligible catalog type + matrix row
- [ ] Disabled entry does not appear in prompt and is greyed in UI
- [ ] Empty eligibility field shows `(any)` in UI table
