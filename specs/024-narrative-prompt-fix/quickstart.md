# Quickstart: B-024 Narrative Prompt Fix

## What Changes

Five targeted bug fixes in the narrative prompt pipeline. No UI, database, or configuration changes.

## Verify the Fix (Manual)

1. Start a roleplay session with high intensity (Explicit level) and let it reach Climax phase.
2. Trigger a narrative turn (either via the Narrative button or opening scene).
3. In the debug log, confirm `Resolved Intensity` in the narrative prompt is **not** Atmospheric when phase is Climax.

## Verify the Fix (Tests)

```pwsh
cd d:\src\DreamGenClone
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~NarrativeValidation" -v minimal
```

Expected: all `RolePlayContinuationNarrativeValidationTests` pass.

## Key Files

- `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — five fix sites
- `DreamGenClone.Web/Application/RolePlay/IRolePlayContinuationService.cs` — one new method
- `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — two call sites updated
- `DreamGenClone.Tests/RolePlay/RolePlayContinuationNarrativeValidationTests.cs` — new tests
