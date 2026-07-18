# Debug 014 — Characters Excluded by Location Affinity Appear in Prompts

**Created:** 2026-07-17

## Report

Sam and Dean are excluded from "Husband and Wife Trailer" by scenario location affinity, but their behavioral frames appear in prompts anyway. The AI reads their tendencies ("young, eager, impulsive") and writes them into the scene. Location detection then places them at the trailer, creating a feedback loop.

## Analysis

`BuildPromptViaBuilderAsync` never checks location affinities when building the character list. Characters excluded from the current scene location still appear in `scenarioCharacters` and `charDetails`, so their behavioral frames get generated and injected into prompts.

## Plan

After the opening couple filter, add a location affinity filter that removes characters whose `LocationAffinities` include an `Excluded` entry matching the current scene location.

```csharp
// Filter characters excluded from current scene location by affinity
var currentLocation = session.AdaptiveState.CurrentSceneLocation 
    ?? context.Scenario.DefaultStartingLocationName;
if (!string.IsNullOrWhiteSpace(currentLocation) && scenario is not null)
{
    var excludedIds = scenario.Characters
        .Where(c => c.LocationAffinities.Any(a => 
            a.AffinityType == AffinityType.Excluded && 
            string.Equals(a.LocationName, currentLocation, StringComparison.OrdinalIgnoreCase)))
        .Select(c => c.Id)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (excludedIds.Count > 0)
        scenarioCharacters = scenarioCharacters.Where(c => !excludedIds.Contains(c.Id)).ToList();
}
```

**Files:** `RolePlayContinuationService.cs` — ~12 lines

## Resolution

Added location affinity exclusion filter after opening couple filter.

## Validated

[ ] Pending user confirmation
