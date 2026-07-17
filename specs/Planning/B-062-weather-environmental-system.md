# B-062 — Weather & Environmental System for RP Engine

**State:** designed
**Priority:** low
**Scope:** large

---

## 1. Overview

Add a full weather and environmental atmosphere system to the roleplay engine. Weather becomes a **first-class narrative driver** — it affects pacing, intensity, narrative gate conditions, theme affinity scores, and is injected as atmospheric context into every prompt. It is both **LLM-detected** (automatically inferred from narrative text) and **theme-directed** (via phase guidance markers), with **manual override** in the UI.

---

## 2. Domain Model — New Types

All new files in `DreamGenClone.Domain/RolePlay/`.

### 2.1 `WeatherCondition.cs`

```csharp
public enum WeatherCondition
{
    ClearSky,       // bright sun, no clouds
    PartlyCloudy,   // mix of sun and cloud
    Overcast,       // grey sky, no direct sun
    Foggy,          // low visibility, muffled sounds
    Drizzle,        // light intermittent rain
    Rain,           // steady rain
    HeavyRain,      // downpour, thunder possible
    Storm,          // thunder, lightning, heavy wind
    Snow,           // light to moderate snowfall
    HeavySnow,      // blizzard conditions
    Windy,          // strong winds, no precipitation
    Humid,          // heavy, oppressive air
    Hot,            // intense heat
    Cold,           // bitter cold
}
```

### 2.2 `WeatherIntensity.cs`

```csharp
public enum WeatherIntensity
{
    Light = 0,
    Moderate = 1,
    Heavy = 2,
    Extreme = 3,
}
```

### 2.3 `WeatherState.cs`

```csharp
public sealed class WeatherState
{
    public WeatherCondition Condition { get; set; } = WeatherCondition.ClearSky;
    public WeatherIntensity Intensity { get; set; } = WeatherIntensity.Light;

    /// <summary>Free-text atmospheric description (LLM-generated or manual).</summary>
    public string? Description { get; set; }

    /// <summary>Turns this weather has persisted. Used by transition engine.</summary>
    public int TurnsActive { get; set; }

    /// <summary>When true, auto-transition is suppressed (user or theme pinned).</summary>
    public bool IsPinned { get; set; }

    /// <summary>Source: "default" | "theme" | "llm-detected" | "scenario" | "manual"</summary>
    public string Source { get; set; } = "default";
}
```

### 2.4 `RPThemeWeatherAffinity.cs`

```csharp
public sealed class RPThemeWeatherAffinity
{
    public string ThemeId { get; set; } = string.Empty;
    public WeatherCondition Condition { get; set; }
    public int ScoreBonus { get; set; }
    public string Rationale { get; set; } = string.Empty;
}
```

### 2.5 NarrativeGateMetricKeys — new constant

Add to existing file:

```csharp
public const string WeatherSeverity = "WeatherSeverity";
```

Maps to a computed severity value (0–100) derived from `WeatherState.Condition × WeatherState.Intensity`.

---

## 3. Extend Existing Models

### 3.1 `AdaptiveScenarioState.cs`

Add:

```csharp
public WeatherState? CurrentWeather { get; set; }
public Dictionary<string, WeatherState> LocationWeatherOverrides { get; set; }
    = new(StringComparer.OrdinalIgnoreCase);
```

### 3.2 `Location.cs` (Scenario domain)

Add:

```csharp
public string? TypicalWeather { get; set; }
public string? WeatherDescription { get; set; }
```

### 3.3 `Setting.cs` (Scenario domain)

Add:

```csharp
public string? DefaultWeatherDescription { get; set; }
```

---

## 4. Theme Marker Integration

Themes declare weather in `PhaseGuidance` text using the marker syntax `[Weather:<condition>[:<intensity>]]`.

### 4.1 Marker Syntax

```
[Weather:clear-sky:light]
[Weather:storm:heavy]
[Weather:rain:moderate]
[Weather:foggy]
[Weather:snow:light]
```

### 4.2 Resolution Precedence (in `WeatherResolver.cs`)

1. **Theme marker override** — `[Weather:*]` in current phase guidance
2. **Manual pin** — `WeatherState.IsPinned == true`
3. **LLM-detected weather** — from background job
4. **Location typical weather** — `Location.TypicalWeather`
5. **Scenario default weather** — `Setting.DefaultWeatherDescription`
6. **System default** — `ClearSky / Light`

---

## 5. Prompt Injection — `WeatherInjector`

New file: `DreamGenClone.Web/Application/RolePlay/Injectors/WeatherInjector.cs`

```csharp
public sealed class WeatherInjector : IPromptInjector
{
    public string Id => "weather-atmosphere";
    public int Priority => 13; // TimeLocationInjector=10, ThemeContractInjector=30

    public bool ShouldFire(PromptInjectionContext context)
        => context.Intent != PromptIntent.Narrative;

    public string BuildText(PromptInjectionContext context)
    {
        var weather = context.Session.AdaptiveState.CurrentWeather;
        if (weather is null) return string.Empty;

        // Build text block with condition, intensity, description, and writing directives.
        // Direct the LLM to reflect weather in sensory descriptions, mood, and character behavior.
    }
}
```

Injected text block includes:
- Current weather condition + intensity label
- Free-text atmospheric description (if available)
- Weather Writing Directives — sensory reflection, mood influence, character reactions

---

## 6. LLM Detection — Background Job

### 6.1 New Files

| File | Location |
|---|---|
| `WeatherDetectionJobPayload.cs` | `DreamGenClone.Web/Application/RolePlay/` |
| `IWeatherDetectionService.cs` | `DreamGenClone.Web/Application/RolePlay/` |
| `WeatherDetectionService.cs` | `DreamGenClone.Web/Application/RolePlay/` |
| `WeatherDetectionJobHandler.cs` | `DreamGenClone.Web/Application/RolePlay/` |

### 6.2 Payload

```csharp
public sealed class WeatherDetectionJobPayload
{
    public required string SessionId { get; init; }
    public required IReadOnlyList<string> RecentInteractionSummaries { get; init; }
    public string? CurrentLocation { get; init; }
    public string? CurrentTimeOfDay { get; init; }
    public WeatherCondition? PreviousCondition { get; init; }
}
```

### 6.3 LLM Prompt

```
Given this roleplay excerpt and the current time of day and location,
infer the most likely weather conditions. Consider continuity with
previous weather if provided.

Output strict JSON only:
{
  "condition": "ClearSky|PartlyCloudy|Overcast|...",
  "intensity": "Light|Moderate|Heavy|Extreme",
  "description": "A short atmospheric phrase like 'gentle afternoon rain'"
}
```

### 6.4 Feature Flag

Add to `RolePlayDecisionOptions`:

```csharp
public bool EnableWeatherServices { get; set; } = false;
```

### 6.5 Debug Events

The job writes `WeatherDetectionCompleted` (Info) or `WeatherDetectionFailed` (Warning) to `RolePlayDebugEvents`.

---

## 7. Weather Transition Engine

New file: `DreamGenClone.Domain/RolePlay/WeatherTransitionEngine.cs`

Pure logic — no IO, testable.

### 7.1 Transition Rules

1. If theme has `[Weather:*]` marker → apply immediately (highest priority)
2. If `IsPinned` → no change
3. Weather must persist for minimum `MinTurnsBeforeChange` (configurable, default 3)
4. After minimum, each turn rolls for change based on phase probability table
5. Phase transitions increase change probability significantly
6. Climax phase has dramatic weather shift on entry
7. Weather changes follow gradual shift chains (e.g. ClearSky → PartlyCloudy → Overcast → Drizzle → Rain)

### 7.2 Phase Probability Table

| Phase | Base Change % per turn | Climax/Entry Boost | Notes |
|---|---|---|---|
| Opening | 5% | — | Establish weather |
| BuildUp | 8% | — | Weather evolves with tension |
| Committed | 5% | — | Relatively stable |
| Approaching | 12% | — | Weather mirrors growing tension |
| Climax | 20% | +40% on entry | Dramatic weather shift likely |
| Reset | 10% | — | Storm breaks, calm returns |

### 7.3 Shift Chains

Gradual shifts follow allowed transitions:

```
ClearSky ↔ PartlyCloudy ↔ Overcast ↔ Drizzle ↔ Rain ↔ HeavyRain
ClearSky ↔ Windy ↔ Storm
ClearSky ↔ Hot ↔ Humid
ClearSky ↔ Cold ↔ Snow ↔ HeavySnow
Overcast ↔ Foggy
```

Direct jumps (e.g. ClearSky → Storm) are allowed only for theme markers, phase transitions, or after >= 8 turns.

---

## 8. Session Outcome Impacts

### 8.1 Narrative Gate Modulation

New metric `WeatherSeverity` (0–100) available for gate rules.

**Computation:**
```
WeatherSeverity = ConditionSeverity(condition) × (intensity + 1) × 8
```

| Condition | Base Severity |
|---|---|
| ClearSky | 0 |
| PartlyCloudy | 1 |
| Overcast, Foggy | 2 |
| Drizzle, Windy, Humid | 3 |
| Rain, Hot, Cold | 4 |
| HeavyRain, Snow | 5 |
| Storm, HeavySnow | 6 |

Example gate rule:
```
FromPhase: BuildUp
ToPhase: Committed
MetricKey: WeatherSeverity
Comparator: >=
Threshold: 30
```
→ "The storm must be at least moderate intensity before commitment."

### 8.2 Intensity Floor/Ceiling Modulation

Applied in `RolePlayStyleResolver.ResolveEffectiveStyle` after existing floor/ceiling logic:

| Weather | Floor Nudge | Ceiling Nudge |
|---|---|---|
| Storm, HeavyRain | +1 | — |
| Cold, Snow | +1 | — |
| Foggy, Overcast | — | -1 |
| Hot, Humid | — | +1 |
| ClearSky, Warm | -1 | — |

### 8.3 Pacing Modulation

Applied in `SceneDirectionResolver` as a modifier tier between theme markers and phase defaults:

| Weather | Pacing Nudge |
|---|---|
| Storm, HeavySnow | +1 toward Fast (urgency, seek shelter) |
| Hot, Humid | +1 toward Slow (languid, oppressive) |
| Cold, Snow | +1 toward Fast (need to move) |
| ClearSky, Warm | Neutral |
| Foggy | +1 toward Slow (muffled, cautious) |
| Rain, Drizzle | Neutral |

### 8.4 Theme Affinity Score Modulation

`RPThemeWeatherAffinity` records define score bonuses when a theme's preferred weather is active. Applied during theme score computation in `RolePlayAdaptiveStateService`:

```csharp
// For each theme with weather affinities:
if (currentWeather is not null)
{
    var bonus = theme.WeatherAffinities
        .Where(wa => wa.Condition == currentWeather.Condition)
        .Sum(wa => wa.ScoreBonus);
    score += bonus;
}
```

---

## 9. Database Changes

### 9.1 Migration

Add columns to `RolePlayV2AdaptiveStates` (following the existing pattern in `SqlitePersistence.cs`):

```sql
ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CurrentWeatherJson TEXT NULL;
ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN LocationWeatherOverridesJson TEXT NOT NULL DEFAULT '{}';
```

### 9.2 Persistence

Update `RolePlayStateRepository.SaveAdaptiveStateAsync` to serialize/deserialize `CurrentWeatherJson` and `LocationWeatherOverridesJson`, matching the pattern used for `CharacterLocationsJson` and `SemanticDeltaBreakdownsJson`.

---

## 10. UI — Weather Widget

### 10.1 New Component

`DreamGenClone.Web/Components/Pages/RolePlayWorkspace/SessionWeather.razor`

Features:
- **Display**: Icon + condition label + intensity badge (☀️ Clear, ⛅ Partly Cloudy, 🌧️ Rain, ⛈️ Storm, ❄️ Snow, etc.)
- **Source badge**: "Theme" | "LLM" | "Scenario" | "Manual"
- **Manual override**: Dropdown with all conditions + intensity sliders
- **Pin toggle**: Lock weather, preventing auto-transitions

### 10.2 Placement

In the session workspace sidebar/toolbar, alongside existing session controls (intensity pin, behavior mode, etc.)

---

## 11. File Manifest

### New Files

| File | Layer |
|---|---|
| `DreamGenClone.Domain/RolePlay/WeatherCondition.cs` | Domain |
| `DreamGenClone.Domain/RolePlay/WeatherIntensity.cs` | Domain |
| `DreamGenClone.Domain/RolePlay/WeatherState.cs` | Domain |
| `DreamGenClone.Domain/RolePlay/WeatherTransitionEngine.cs` | Domain |
| `DreamGenClone.Domain/RolePlay/WeatherResolver.cs` | Domain |
| `DreamGenClone.Domain/RolePlay/RPThemeWeatherAffinity.cs` | Domain |
| `DreamGenClone.Web/Application/RolePlay/Injectors/WeatherInjector.cs` | Application |
| `DreamGenClone.Web/Application/RolePlay/WeatherDetectionService.cs` | Application |
| `DreamGenClone.Web/Application/RolePlay/IWeatherDetectionService.cs` | Application |
| `DreamGenClone.Web/Application/RolePlay/WeatherDetectionJobPayload.cs` | Application |
| `DreamGenClone.Web/Application/RolePlay/WeatherDetectionJobHandler.cs` | Application |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace/SessionWeather.razor` | Web UI |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace/SessionWeather.razor.css` | Web UI |

### Modified Files

| File | Changes |
|---|---|
| `AdaptiveScenarioState.cs` | Add `CurrentWeather`, `LocationWeatherOverrides` |
| `Location.cs` | Add `TypicalWeather`, `WeatherDescription` |
| `Setting.cs` | Add `DefaultWeatherDescription` |
| `NarrativeGateMetricKeys.cs` | Add `WeatherSeverity` |
| `NarrativeGateRule.cs` | No change — reuse existing MetricKey/Comparator/Threshold |
| `SceneDirectionResolver.cs` | Weather-based pacing nudge |
| `RolePlayStyleResolver.cs` | Weather-based intensity floor/ceiling nudge |
| `RolePlayAdaptiveStateService.cs` | Theme affinity weather bonus |
| `RolePlayStateRepository.cs` | Save/load weather columns |
| `SqlitePersistence.cs` | Migration DDL |
| `RolePlayDecisionOptions.cs` | `EnableWeatherServices` flag |
| `RPThemeModels.cs` | Add `WeatherAffinities` list to `RPTheme` |
| `PromptInjectionContext.cs` | Maybe — if weather access needed, already available via `Session.AdaptiveState` |
| `LocationDetectionService.cs` | Possibly share LLM prompt patterns |

---

## 12. Integration Flow

```
User submits turn
    │
    ├─► LocationDetectionJob (existing)
    │
    ├─► WeatherDetectionJob (NEW)
    │     └─► LLM infers weather from recent text + location + time
    │           └─► Saves to AdaptiveState.CurrentWeather
    │
    ├─► WeatherTransitionEngine (NEW)
    │     └─► Theme marker override? → apply
    │     └─► Pinned? → skip
    │     └─► Min turns elapsed? → roll probabilistic shift
    │     └─► Phase transition? → high-probability dramatic shift
    │
    ├─► SceneDirectionResolver (modified)
    │     └─► Weather nudges Pacing (one additional tier)
    │
    ├─► RolePlayStyleResolver (modified)
    │     └─► Weather nudges Intensity floor/ceiling
    │
    ├─► NarrativeGateEvaluator (modified)
    │     └─► WeatherSeverity metric available for gate rules
    │
    ├─► ThemeScore component
    │     └─► WeatherAffinity score bonuses applied
    │
    └─► SceneDirectionCoordinator
          └─► WeatherInjector fires (priority 13)
                └─► Injects weather + atmospheric directives into prompt
```

---

## 13. Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Weather as JSON column vs discrete columns | JSON column (`CurrentWeatherJson`) | Matches existing pattern (`CharacterLocationsJson`, `SemanticDeltaBreakdownsJson`); weather is always loaded/saved as a unit. Discrete columns only if query-by-weather is needed. |
| Transition engine location | Domain layer | Pure logic, no IO — testable, no dependency on infrastructure. |
| Theme marker priority vs LLM detection | Theme marker wins | Theme authors explicitly control weather; LLM detection is a fallback/seed. Consistent with `SceneDirectionResolver` marker precedence. |
| Weather affinity: per-theme vs per-profile | Per-theme (`RPThemeWeatherAffinity`) | Weather affinity is a thematic quality; profiles compose themes, not weather. |
| Pacing/Intensity nudges: hard vs soft | Soft — additive modifier | Weather should influence but not override; phase defaults and theme markers still dominate. |

---

## 14. Future Considerations

- **Seasonal weather tables**: Weather could be driven by a calendar/season system, with different probability distributions per season.
- **Indoor/outdoor awareness**: Weather matters less indoors. The `IsActorInScene` tri-state could modulate weather injection weight.
- **Weather-driven location affinity**: Characters could have preferred weather for each location (e.g. "gardening in the rain").
- **Per-location microclimates**: `LocationWeatherOverrides` supports this already — a garden could be rainy while the house is dry.
- **Weather history graph**: Show weather changes over session timeline in the adaptive panel.
