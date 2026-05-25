# ScenarioPhaseSignal — How It Works

## Overview

`ScenarioPhaseSignal` is a one-time, session-start seed score on each theme tracker item.
It reflects how much the **active scenario's own definition text** "sounds like" each theme,
based on keyword matching. It is computed once inside `InitializeThemeTrackerAsync` and never
updated again during the session.

## What is scanned (left side)

The active scenario's definition fields, each at a different relevance weight:

| Weight | Fields |
|--------|--------|
| **0.6×** | `Openings[*].Text`, `Examples[*].Text` |
| **0.4×** | `Plot.Description`, `Plot.Conflicts[*]`, `Plot.Goals[*]`, `Setting.WorldDescription`, `Setting.EnvironmentalDetails[*]`, `Narrative.NarrativeTone`, `Narrative.ProseStyle`, `Narrative.NarrativeGuidelines[*]`, `Characters[*].Name`, `Characters[*].Description`, `Locations[*].Name`, `Locations[*].Description`, `Objects[*].Name`, `Objects[*].Description` |
| **0.3×** | `Characters[*].BaseStats` key names (e.g. "Desire", "Restraint") |

Openings and examples get the highest weight (0.6×) because they are the most direct
representation of how the scenario actually plays out. Structural/descriptive fields get 0.4×.
Stat names get 0.3× as they are loosely correlated context.

## What it searches for (right side)

Each RPTheme's `Keywords` list — a flat list of strings configured per theme in the Theme
Catalog UI (e.g. `["infidelity", "affair", "disappear", "secret phone"]`). These come from
`RPTheme.Keywords` rows, normalized and deduped, then mapped into `ThemeCatalogEntry.Keywords`.

## The comparison (`ScoreText`)

For each text field × each keyword:

```
text.ToLowerInvariant().Contains(keyword, OrdinalIgnoreCase)
```

Each hit contributes `+Weight` points (the theme's integer `Weight` from its catalog entry,
range 1–10). Each **individual text field** is capped at **+12 points** regardless of how many
keywords match in it. All text fields are summed into a running `total`.

## Optional SteeringProfile multiplier

If the session has a `SteeringProfile` selected and that profile has a `ThemeAffinities[themeId]`
entry, the total is multiplied by:

```
total *= (1.0 + affinityMultiplier × 0.1)
```

A `+3` affinity value boosts the signal by 30%. A `-2` reduces it by 20%.

## Final result

`ScenarioPhaseSignal = clamp(total, 0, 100)`

Added once to `ThemeTrackerItem.Score` and stored on `ThemeTrackerItem.Breakdown.ScenarioPhaseSignal`.

## Code locations

| File | What |
|------|------|
| `RolePlayAdaptiveStateService.cs` | `InitializeThemeTrackerAsync` — loop that calls `ScoreScenarioKeywords` per theme |
| `RolePlayAdaptiveStateService.cs` | `ScoreScenarioKeywords` — field weighting and total accumulation |
| `RolePlayAdaptiveStateService.cs` | `ScoreText` — per-field keyword hit counting and 12-point cap |
| `RolePlayAdaptiveStateService.cs` | `MapRpThemeToCatalogEntry` — where `RPTheme.Keywords` → `ThemeCatalogEntry.Keywords` |
| `RolePlayEngineService.cs` | `ApplyThemeSemiResetAsync` — clears `ScenarioPhaseSignal` to 0 on Reset (stale after arc ends) |
| `ThemeCatalogEntry.cs` | Domain model: `Keywords`, `Weight`, `StatAffinities` |
