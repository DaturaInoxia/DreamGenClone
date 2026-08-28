# Quickstart: RP Session Opening Period

**Feature**: `001-opening-period`  
**Date**: 2026-06-22

## Prerequisites

- .NET 9 SDK
- Existing DreamGenClone solution built and running
- SQLite database with existing scenarios

## Migration (One-Time)

Apply the DB migration to add the `OpeningGuidanceText` column and seed existing scenarios:

```sql
-- Add column
ALTER TABLE Scenarios ADD COLUMN OpeningGuidanceText TEXT;

-- Seed all existing scenarios with default opening guidance
UPDATE Scenarios SET OpeningGuidanceText = 'Focus on the couple''s relationship and their current life together. Include a brief sense of their intimate life from her point of view — the rhythm of it, what she feels about it, what she wants or doesn''t get — grounding these details in the character profiles and their descriptions. Describe their routines, interactions, and daily rhythms. Establish the setting, mood, and any relevant history. Other characters remain in the background.';
```

Run via the dbquery tool:
```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/seed_opening_guidance.sql
```

## Code Changes (3 files)

### 1. `RolePlayContinuationService.cs` — Opening Period Gate

**Constant** (line ~34):
```csharp
private const int OpeningPeriodTurnCount = 3;
```

**Gate** (wrap theme-guidance block at ~line 928):
```csharp
if (session.AdaptiveState.ObservedTurnCount > OpeningPeriodTurnCount)
{
    // existing: AppendActiveThemeContract, AppendThemeHardConstraints,
    //           AppendThemeAIGuidance, secondary theme blend
}
else
{
    // Opening period: inject OpeningGuidanceText from scenario
    var guidance = scenario.OpeningGuidanceText ?? DefaultOpeningGuidanceText;
    sb.AppendLine($"HARD CONSTRAINT \u2014 Opening Period Direction: {guidance}");
}
```

### 2. `RolePlayContinuationService.cs` — Remove Old OPF

**Delete** the old peripheral-focus block (~lines 1330-1348):
```csharp
// REMOVED: HARD CONSTRAINT — Opening Peripheral Focus
```

### 3. `RolePlayEngineService.cs` — OtherMan Exclusion

**Change** from `totalInteractions < 6` to `ObservedTurnCount <= 3` (~line 2213):
```csharp
if (session.AdaptiveState.ObservedTurnCount <= 3
    && string.Equals(role, "OtherMan", StringComparison.OrdinalIgnoreCase))
```

## Verification

1. **Build**: `dotnet build DreamGenClone.sln` — 0 errors
2. **Create new session**: OtherMan does not appear in turns 1-3
3. **Turn 4**: OtherMan eligible, theme guidance present in prompt
4. **Existing sessions mid-lifecycle**: No change — opening period does not re-run

## Key Files

| File | What Changes |
|------|-------------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Opening period constant, gate, guidance injection, OPF removal |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | OtherMan turn-based exclusion |
| `Scenarios` table (SQLite) | New `OpeningGuidanceText` column + seed data |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | Migration SQL (ALTER TABLE + UPDATE) |

## Default Opening Guidance Text

```
Focus on the couple's relationship and their current life together. Include a brief sense of their intimate life from her point of view — the rhythm of it, what she feels about it, what she wants or doesn't get — grounding these details in the character profiles and their descriptions. Describe their routines, interactions, and daily rhythms. Establish the setting, mood, and any relevant history. Other characters remain in the background.
```
