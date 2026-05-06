# Research: Finishing Move System – Catalog and Matrix Redesign

**Branch**: `007-finishing-move-catalog`  
**Phase**: 0 – Unknowns resolved

---

## 1. Band eligibility storage and matching

**Question**: The spec stores eligibility as comma-separated band keys (e.g., `"0-29,30-59"`). Does the existing `MatchesNumericBand` helper already handle this, or does a new helper need to wrap it?

**Finding**: `MatchesNumericBand` in `RolePlayWorkspace.razor` (line 4971) handles:
- null/empty → match all
- `any`/`all`/`*` → match all
- `|` (pipe) as OR separator
- Named bands (`Low`, `Medium`, `High`)
- Numeric ranges (`min-max`)
- Prefix operators (`>=`, `<=`, `>`, `<`)

It does **not** handle comma as an OR separator. The existing steer position and other matrix columns all use single band values per field.

**Decision**: Introduce a `MatchesBandEligibility(string? eligibleBands, double statValue)` helper in `RolePlayWorkspace.razor` that splits on comma and delegates each part to `MatchesNumericBand`. This is a thin wrapper, satisfies FR-010 (single reusable helper), and keeps storage as comma-separated strings consistent with the spec.

**Rationale**: Aligns with the spec assumption without modifying the existing `MatchesNumericBand` contract or storage of existing matrix rows.

---

## 2. OtherManDominance stat name in adaptive state

**Question**: The existing finishing move matrix code reads `GetAverageAdaptiveStat(state, "Dominance")` for the dominance dimension. The rename to `OtherManDominanceBand` implies the stat name should shift to `OtherManDominance`. How is this resolved in practice?

**Finding**: The steer position matrix (existing) already uses:
```csharp
var otherManDominance = GetAverageAdaptiveStatOrFallback(
    state, dominance, "OtherManDominance", "OtherManDom", "RivalDominance", "BullDominance");
```

This fallback pattern handles sessions where the stat key is stored under a legacy name by falling back to the plain `dominance` average.

**Decision**: Update `AppendFinishingMoveMatrixContextAsync` to replace `GetAverageAdaptiveStat(state, "Dominance")` with `GetAverageAdaptiveStatOrFallback(state, dominance, "OtherManDominance", "OtherManDom", "RivalDominance", "BullDominance")` — matching the steer position pattern exactly.

**Rationale**: Reuses existing fallback infrastructure, prevents breakage on sessions that have `Dominance` stored under the old key, and accurately targets the OtherMan-specific dominance dimension.

---

## 3. Schema migration approach: drop-and-recreate vs ALTER TABLE

**Question**: SQLite does not support column rename via `ALTER TABLE RENAME COLUMN` on some older SQLite versions bundled with .NET. Can `ALTER TABLE RENAME COLUMN` be used safely, or is a drop-and-recreate required?

**Finding**: SQLite 3.25.0+ (released 2018) supports `ALTER TABLE RENAME COLUMN`. The Microsoft.Data.Sqlite package bundled with .NET 9 uses SQLite 3.43+ (shipped 2023). However, the existing migration in `RPThemeService.cs` (line 2763) uses the archive-and-drop pattern for all schema migrations, establishing a clear project convention.

**Decision**: Follow the existing project migration convention — detect `DominanceBand` column via `TableHasColumnAsync`, archive the existing table data into `RPFinishingMoveMatrixRows_Archived_v2`, drop `RPFinishingMoveMatrixRows`, then recreate it with `OtherManDominanceBand`. The seed service will re-populate. Update band value ranges for seed data from `75-100/50-74/0-49` to `60-100/30-59/0-29`.

**Rationale**: Consistent with project conventions; avoids unknown edge cases with `RENAME COLUMN`; the spec explicitly accepts data loss on matrix rows as intentional ("start over" decision).

---

## 4. EnsureSupplementalTablesAsync vs separate init methods

**Question**: Should the five new `CREATE TABLE IF NOT EXISTS` statements go into `EnsureSupplementalTablesAsync` in `RPThemeService.cs`, or into a separate method?

**Finding**: `EnsureSupplementalTablesAsync` already contains all supplemental table creation (RPFinishingMoveMatrixRows, RPSteerPositionMatrixRows, and others). It is called from `InitializeAsync()`.

**Decision**: Add all five new `CREATE TABLE IF NOT EXISTS` blocks inside `EnsureSupplementalTablesAsync`, after the existing statements. Add the new migration call (`MigrateFinishingMoveMatrixToV2Async`) at the top of `EnsureSupplementalTablesAsync`, before table creation.

**Rationale**: One cohesive place for supplemental table lifecycle; follows existing pattern; migration runs before `CREATE TABLE IF NOT EXISTS` so the new schema exists before seeds run.

---

## 5. Catalog filter query location: service layer vs workspace

**Question**: Should band eligibility filtering happen in the service layer (SQL WHERE clause) or in the workspace at application time?

**Finding**: The existing matrix row filtering is done in-memory in `RolePlayWorkspace.razor` — list all enabled rows, then filter by band match in LINQ. The steer position matrix follows the same pattern. SQLite-side filtering would require dynamic SQL or per-column band JSON parsing.

**Decision**: Filter in-memory in `RolePlayWorkspace.razor`, following the exact same pattern as matrix rows. Load all enabled entries per catalog type, filter with `MatchesBandEligibility`, emit section if any entries remain.

**Rationale**: Consistent with project patterns; catalog tables are small (tens of entries); SQL WHERE on comma-separated string eligibility fields would require SQLite LIKE or JSON parsing with no performance benefit.

---

## 6. Facial Types section suppression check

**Question**: How should the Facial Types section be suppressed when no matched Location entry has `Category = Facial`?

**Finding**: The spec (FR-008) states the section must be suppressed when no eligible Location has Category = Facial. The workspace assembles prompt sections sequentially. Location eligibility is computed before Facial Types.

**Decision**: After filtering `RPFinishLocations` entries for the current stats, check if any have `Category == "Facial"`. Store a local `bool hasFacialLocation`. When emitting the Facial Types section, skip it entirely if `hasFacialLocation == false`.

**Rationale**: Simple boolean gate; no additional query; keeps assembly logic sequential and readable.

---

## 7. Receptivity and HisControl band eligibility column scope

**Question**: The spec states `RPFinishReceptivityLevel` uses only Desire+SelfRespect bands and `RPFinishHisControlLevel` uses only OtherManDominance band. Should omitted band columns still be stored (as empty strings) or omitted from the schema entirely?

**Decision**: Omit irrelevant columns from the schema entirely. `RPFinishReceptivityLevels` has `EligibleDesireBands` and `EligibleSelfRespectBands` only. `RPFinishHisControlLevels` has `EligibleOtherManDominanceBands` only. The UI forms render only the columns the entity actually has.

**Rationale**: Eliminates dead columns; prevents curator confusion about why a column has no effect; domain models are cleaner.

---

## All NEEDS CLARIFICATION Items: Resolved

| # | Unknown | Decision |
|---|---------|----------|
| 1 | Band eligibility storage + matching helper | Comma-separated string; `MatchesBandEligibility` wrapper around `MatchesNumericBand` |
| 2 | OtherManDominance stat name | Use `GetAverageAdaptiveStatOrFallback` with `"OtherManDominance"` primary + legacy fallbacks |
| 3 | Migration approach | Archive-and-drop following project convention |
| 4 | New table DDL location | Inside `EnsureSupplementalTablesAsync` |
| 5 | Catalog filter location | In-memory in workspace, matching matrix pattern |
| 6 | Facial Types suppression | Boolean gate from Location eligibility pass |
| 7 | Receptivity/HisControl column scope | Omit irrelevant band columns entirely from schema |
