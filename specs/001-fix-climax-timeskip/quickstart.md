# Quickstart: Fix Climax Time-Skip System

## Build

```bash
dotnet build DreamGenClone.sln
```

## Run Tests

```bash
dotnet test DreamGenClone.Tests --filter "MultiEncounterTimeSkip"
```

## Manual Test

1. Start web app
2. Load a multi-encounter Climax session (e.g., `exhibitionism-v2` theme with `[ClimaxMode:multi-encounter]`)
3. Continue until encounter boundary fires (orgasm or interruption detected)
4. **Verify**: First actor's prompt uses `PromptIntent.Instruction` with directive text:
   > Close the current encounter naturally. Then advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life.
5. **Verify**: Subsequent turns do NOT contain the directive (no persistent re-injection)
6. **Verify**: Typing a user steer before boundary skips engine injection
7. **Verify**: `TimeSkipPending` remains true when skipped, retries next turn

## Debug Events

- `MultiEncounterTimeSkipDirectiveInjected` — directive injected into first actor
- `MultiEncounterTimeSkipSkippedDueToUserInstruction` — injection skipped, will retry
- `EncounterBoundaryAdvanced` — boundary detected (existing, unchanged)
- `EncounterBoundaryNoDetection` — detection ran, no boundary found (existing)
- `EncounterBoundaryBelowMin` — below 4-interaction minimum (existing)

## Key Files

| File | Change |
|------|--------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Remove Instruction interaction injection; add first-actor `PromptIntent.Instruction` with directive; add `HasRecentUserInstruction` helper |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | No changes (re-injection already guarded by `intent != PromptIntent.Instruction`) |
| `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` | New test file |
