# Service Interface Contract: IRPThemeService Additions

**Branch**: `007-finishing-move-catalog`  
**File**: `DreamGenClone.Application/RolePlay/IRPThemeService.cs`

This document lists all new method signatures added to `IRPThemeService` by this feature, plus the two updated matrix methods.

---

## Updated Matrix Methods

The following methods have their internal implementation updated (renamed column, new band ranges) but their **signatures are unchanged**:

```csharp
Task<RPFinishingMoveMatrixRow> SaveFinishingMoveMatrixRowAsync(RPFinishingMoveMatrixRow row, CancellationToken cancellationToken = default);
Task<IReadOnlyList<RPFinishingMoveMatrixRow>> ListFinishingMoveMatrixRowsAsync(CancellationToken cancellationToken = default);
Task<bool> DeleteFinishingMoveMatrixRowAsync(string rowId, CancellationToken cancellationToken = default);
Task<int> ImportFinishingMoveMatrixRowsFromJsonAsync(string json, bool replaceExisting = false, CancellationToken cancellationToken = default);
```

---

## New Methods: RPFinishLocation

```csharp
Task<RPFinishLocation> SaveFinishLocationAsync(RPFinishLocation entry, CancellationToken cancellationToken = default);
Task<IReadOnlyList<RPFinishLocation>> ListFinishLocationsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
Task<bool> DeleteFinishLocationAsync(string entryId, CancellationToken cancellationToken = default);
```

---

## New Methods: RPFinishFacialType

```csharp
Task<RPFinishFacialType> SaveFinishFacialTypeAsync(RPFinishFacialType entry, CancellationToken cancellationToken = default);
Task<IReadOnlyList<RPFinishFacialType>> ListFinishFacialTypesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
Task<bool> DeleteFinishFacialTypeAsync(string entryId, CancellationToken cancellationToken = default);
```

---

## New Methods: RPFinishReceptivityLevel

```csharp
Task<RPFinishReceptivityLevel> SaveFinishReceptivityLevelAsync(RPFinishReceptivityLevel entry, CancellationToken cancellationToken = default);
Task<IReadOnlyList<RPFinishReceptivityLevel>> ListFinishReceptivityLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
Task<bool> DeleteFinishReceptivityLevelAsync(string entryId, CancellationToken cancellationToken = default);
```

---

## New Methods: RPFinishHisControlLevel

```csharp
Task<RPFinishHisControlLevel> SaveFinishHisControlLevelAsync(RPFinishHisControlLevel entry, CancellationToken cancellationToken = default);
Task<IReadOnlyList<RPFinishHisControlLevel>> ListFinishHisControlLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
Task<bool> DeleteFinishHisControlLevelAsync(string entryId, CancellationToken cancellationToken = default);
```

---

## New Methods: RPFinishTransitionAction

```csharp
Task<RPFinishTransitionAction> SaveFinishTransitionActionAsync(RPFinishTransitionAction entry, CancellationToken cancellationToken = default);
Task<IReadOnlyList<RPFinishTransitionAction>> ListFinishTransitionActionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
Task<bool> DeleteFinishTransitionActionAsync(string entryId, CancellationToken cancellationToken = default);
```

---

## Notes

- All Save methods use upsert-on-Id (INSERT … ON CONFLICT DO UPDATE), matching the existing service pattern.
- All List methods accept `includeDisabled = false` as the default to exclude disabled entries from prompt queries. The UI passes `includeDisabled: true` to show all entries in management tables.
- No Import methods are added for the five new catalog types (no JSON batch import requirement); can be added later if needed.
