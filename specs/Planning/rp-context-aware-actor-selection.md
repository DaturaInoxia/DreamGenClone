# RP Engine: Context-Aware Actor Selection — Design Specification

**Date:** 2026-06-23
**Status:** Design — initial draft
**Related:** `rp-turn-interaction-cohesion-design.md` (turn prompt structure), `RolePlayEngineService.cs` `ResolveSceneContinueActorsAsync()`

---

## 1. Problem Statement

The overflow continue ("...") button auto-selects characters using a simple recency + location sort:

```
OrderByDescending(InScene)
→ ThenBy(LastSeenIndex < 0 ? int.MinValue : LastSeenIndex)
→ ThenBy(ScenarioOrder)
```

This ignores the rich context already tracked on the session:
- Narrative phase (Opening → BuildUp → Committed → Approaching → Climax → Reset)
- Per-character stats (Desire, Restraint, Dominance, Loyalty, SelfRespect)
- Character roles (Wife, Husband, OtherMan, etc.)
- Active themes and their scores
- Semantic events (what just happened thematically)
- Encounter participation (who is actively in a sexual encounter)
- Pairwise relationship stats

The result: every overflow click produces the same character rotation regardless of what's happening in the story. Characters with no narrative reason to speak get equal priority, and the most dramatically relevant character may be buried at position N.

---

## 2. Design Principle: Multi-Factor Scoring Over Simple Sort

Replace the current three-key ordering with a **weighted scoring function** that produces a single `priorityScore` per character per turn. Characters are sorted by score descending; the top N are selected.

```
Score = LocationBase + RecencyBoost + PhaseStatContribution 
        + RolePhasePriority + ThemeRelevanceBoost 
        + SemanticEventBoost + EncounterParticipationBoost
        + ScenarioOrderTiebreaker
```

Each factor is independently tunable and phase-aware. The total is a simple arithmetic sum — no machine learning, no LLM calls.

---

## 3. Factor Definitions

### 3.1 Location Base (`weight: 1000`)

| Condition | Value |
|---|---|
| Character is `InScene` (location matches `CurrentSceneLocation`) | 1000 |
| Location services disabled or no location data | 500 (neutral — all characters get same base) |
| Character is out-of-scene | 0 |

Keeps the existing `InScene` dominance. Out-of-scene characters can still be selected if other factors push them high enough (e.g., a semantic event just referenced them).

### 3.2 Recency Boost (`weight: 0–200`)

How recently the character spoke. Ensures quiet characters get a turn.

| Condition | Value |
|---|---|
| Never spoken in session | 200 |
| Last spoke > 6 interactions ago | 180 |
| Last spoke 4–6 ago | 120 |
| Last spoke 2–3 ago | 60 |
| Last spoke in last interaction | 0 |

Computed from `session.Interactions` — find the last non-excluded interaction by this character.

### 3.3 Phase Stat Contribution (`weight: 0–100`)

Uses each character's `CharacterStatProfileV2` (Desire, Restraint, Dominance) and weights them differently per phase.

| Phase | Formula | Rationale |
|---|---|---|
| **Opening** | `0` | No stats matter yet — establishing basics |
| **BuildUp** | `Desire × 0.5 - Restraint × 0.3 + Dominance × 0.2` | High-desire, low-restraint characters push forward |
| **Committed** | `Desire × 0.4 - Restraint × 0.5 + Dominance × 0.3` | Restraint matters most — who hesitates? |
| **Approaching** | `Desire × 0.3 - Restraint × 0.4 + Dominance × 0.5` | Dominance rising — who takes charge? |
| **Climax** | `Dominance × 0.7 + Desire × 0.3` | Dominance dominates — who drives the act? Restraint irrelevant |
| **Reset** | `Desire × 0.2 + Restraint × 0.3` | Quiet re-entry — restraint dominates |

Values are clamped to `[0, 100]`. If `CharacterStats` entry is missing for a character, contribution is `50` (neutral).

### 3.4 Role Phase Priority (`weight: 0–100`)

Each character role gets a phase-appropriate base priority. Uses `CharacterStatProfileV2.CharacterRole` (normalized via `CharacterRoleCatalog`).

| Role | Opening | BuildUp | Committed | Approaching | Climax | Reset |
|---|---|---|---|---|---|---|
| **Husband / Persona** | 100 | 30 | 20 | 10 | 10 | 50 |
| **Wife** | 80 | 90 | 90 | 90 | 80 | 50 |
| **OtherMan** | 0 (excluded) | 40 | 70 | 90 | 100 | 50 |
| **Other / Unknown** | 50 | 50 | 50 | 50 | 50 | 50 |

**Opening exception**: `OtherMan` role returns `0` and is excluded entirely from the candidate list (existing behavior preserved).

### 3.5 Theme Relevance Boost (`weight: 0–50`)

If a character is linked to the active `PrimaryThemeId` or `SecondaryThemeId`, they get a boost.

A character is considered "theme-linked" when:
- The active theme's fit rules (`ScenarioFitRules`) reference the character's role
- OR the character has been flagged in recent semantic events matching the active theme

| Condition | Value |
|---|---|
| Linked to primary theme | 50 |
| Linked to secondary theme | 30 |
| No theme link | 0 |

*Implementation note: theme-to-character linkage requires reading the active theme's `CharacterRole` or `RoleWeight` fit rules. For V1, a simple heuristic: check if the character's name or role appears in the theme's `FitRules.CharacterRole` or in `SemanticEvents` matching the active theme ID.*

### 3.6 Semantic Event Boost (`weight: 0–30`)

If a character was involved in recent semantic events (last 3), they get a priority boost for the next turn. This creates natural narrative flow — the character who just did something important stays in focus.

| Condition | Value |
|---|---|
| Character mentioned in latest semantic event | 30 |
| Character mentioned in last 3 events | 20 |
| No recent events | 0 |

Semantic events live in `AdaptiveState.SemanticEvents`. Check `ActorName` field of each event record.

### 3.7 Encounter Participation Boost (`weight: 0–100`)

During Climax phase, characters who are actively in a sexual encounter should be prioritized over those who aren't.

| Condition | Value |
|---|---|
| Climax phase AND `IsCharacterHavingSex(name)` is true | 80 |
| Climax phase AND not having sex | 0 |
| Not Climax phase | 0 |

Uses `AdaptiveState.IsCharacterHavingSex(characterName)`.

### 3.8 Scenario Order Tiebreaker (`weight: 0–10`)

Preserves deterministic fallback ordering from the scenario definition when all other factors are equal.

| Position | Value |
|---|---|
| 1st in scenario | 10 |
| 2nd | 9 |
| 3rd | 8 |
| ... | ... |
| Nth | `max(0, 10 - N)` |

---

## 4. Persona Handling

The persona (POV character / "You") is not in `sceneCharacterNames` but is added separately. The persona's scoring follows the same factors with these differences:

- **Recency**: based on `InteractionType.User` or `ActorName == personaName`
- **Role**: uses `"Husband"` or the persona's explicit `CharacterRole`
- **Stats**: uses persona's `CharacterStatProfileV2` if one exists
- **Theme relevance**: persona is always linked to the active theme (narrative POV)
- **Position in turn**: persona is always last before Narrative (per `rp-turn-interaction-cohesion-design.md`)

The persona insertion rule from the existing design is preserved:
- First 6 interactions → `Insert(0)` (persona leads to establish setup)
- After 6, even `ObservedTurnCount` → `Add()` (appended, last before narrative)
- After 6, odd `ObservedTurnCount` → skip

---

## 5. Batch Selection

After scoring all candidates, select the top N where N = `session.SceneContinueBatchSize` (clamped to available count).

**Edge cases:**
- If multiple characters have the same score, scenario order breaks ties
- If the persona is excluded by turn parity but would have been in the top N, the next-highest NPC replaces them
- If all NPCs score 0 (shouldn't happen), fall back to `ResolveDefaultContinueActor`

---

## 6. Code Changes

### 6.1 New Method: `ScoreActorForAutoSelection`

Add to `RolePlayEngineService.cs`:

```csharp
private double ScoreActorForAutoSelection(
    RolePlaySession session,
    string actorName,
    string? characterRole,
    bool inScene,
    int lastSeenIndex,
    int scenarioOrder,
    List<string> recentSemanticActorNames)
```

Returns a single `double` combining all factors above.

### 6.2 Modified Method: `ResolveSceneContinueActorsAsync`

Replace the current ordering block:

```csharp
// BEFORE:
var ordered = eligibleCharacterNames
    .Select((name, scenarioOrder) => new { Name = name, ... })
    .OrderByDescending(x => x.InScene)
    .ThenBy(x => x.LastSeenIndex < 0 ? int.MinValue : x.LastSeenIndex)
    .ThenBy(x => x.ScenarioOrder)
    .Select(x => x.Name)
    .ToList();

// AFTER:
var recentSemanticActorNames = ExtractRecentSemanticActorNames(session.AdaptiveState.SemanticEvents, 3);
var ordered = eligibleCharacterNames
    .Select((name, scenarioOrder) =>
    {
        session.AdaptiveState.CharacterStats.TryGetValue(name, out var stats);
        var inScene = IsActorInCurrentScene(session, name, currentSceneLocation);
        var lastSeenIndex = recentActors.FindLastIndex(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        var role = stats?.CharacterRole;
        
        return new
        {
            Name = name,
            Score = ScoreActorForAutoSelection(
                session, name, role, inScene, lastSeenIndex, scenarioOrder, recentSemanticActorNames),
            Reason = BuildScoreExplanation(...)
        };
    })
    .OrderByDescending(x => x.Score)
    .Select(x => x.Name)
    .ToList();
```

### 6.3 Helper: Phase-Weighted Stat Contribution

```csharp
private static double ComputePhaseStatContribution(
    NarrativePhase phase,
    CharacterStatProfileV2? stats)
{
    if (stats is null) return 50;
    
    var (desireW, restraintW, dominanceW) = phase switch
    {
        NarrativePhase.Buildup    => (0.5, -0.3, 0.2),
        NarrativePhase.Committed  => (0.4, -0.5, 0.3),
        NarrativePhase.Approaching => (0.3, -0.4, 0.5),
        NarrativePhase.Climax     => (0.3,  0.0, 0.7),
        NarrativePhase.Reset      => (0.2,  0.3, 0.0),
        _ => (0.0, 0.0, 0.0)  // Opening
    };
    
    var raw = stats.Desire * desireW 
            + stats.Restraint * restraintW 
            + stats.Dominance * dominanceW;
    
    return Math.Clamp(raw, 0, 100);
}
```

### 6.4 Helper: Role Phase Priority

```csharp
private static double GetRolePhasePriority(string? role, NarrativePhase phase)
{
    var normalizedRole = DreamGenClone.Domain.StoryAnalysis.CharacterRoleCatalog.Normalize(role ?? string.Empty);
    
    return (normalizedRole, phase) switch
    {
        ("Husband", NarrativePhase.Opening) => 100,
        ("Husband", NarrativePhase.Buildup) => 30,
        ("Husband", NarrativePhase.Committed) => 20,
        ("Husband", NarrativePhase.Approaching) => 10,
        ("Husband", NarrativePhase.Climax) => 10,
        ("Husband", _) => 50,
        
        ("Wife", NarrativePhase.Opening) => 80,
        ("Wife", NarrativePhase.Buildup) => 90,
        ("Wife", NarrativePhase.Committed) => 90,
        ("Wife", NarrativePhase.Approaching) => 90,
        ("Wife", NarrativePhase.Climax) => 80,
        ("Wife", _) => 50,
        
        ("OtherMan", NarrativePhase.Opening) => 0,    // excluded
        ("OtherMan", NarrativePhase.Buildup) => 40,
        ("OtherMan", NarrativePhase.Committed) => 70,
        ("OtherMan", NarrativePhase.Approaching) => 90,
        ("OtherMan", NarrativePhase.Climax) => 100,
        ("OtherMan", _) => 50,
        
        _ => 50
    };
}
```

### 6.5 New Model: `CharacterTurnOverride`

In `DreamGenClone.Web.Domain.RolePlay` (new file `CharacterTurnOverride.cs`):

```csharp
namespace DreamGenClone.Web.Domain.RolePlay;

/// <summary>
/// Per-character overrides for auto-continue turn participation.
/// Stored on <see cref="RolePlaySession.CharacterTurnOverrides"/>.
/// Null entries = use phase-aware scoring defaults.
/// </summary>
public sealed class CharacterTurnOverride
{
    /// <summary>Character name as it appears in interactions.</summary>
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>
    /// 0 = never auto-select. 1–100 = manual priority boost added on top of phase scoring.
    /// Null = use phase-aware scoring with no manual boost.
    /// </summary>
    public int? ResponsePriority { get; set; }

    /// <summary>When false, character is excluded from auto-continue selection entirely.</summary>
    public bool ParticipateInAutoContinue { get; set; } = true;

    /// <summary>
    /// Preferred position within a turn. Null = auto (score determines position).
    /// "First" forces the character to position 0 if selected.
    /// "Last" forces the character to the last NPC position (before persona/narrative).
    /// </summary>
    public PreferredTurnPosition? PreferredPosition { get; set; }
}

public enum PreferredTurnPosition
{
    First = 0,
    Last = 1
}
```

### 6.6 Session Property

Add to `RolePlaySession.cs`:

```csharp
/// <summary>
/// Per-character overrides for auto-continue turn selection and ordering.
/// Keyed by character name (case-insensitive).
/// </summary>
public Dictionary<string, CharacterTurnOverride> CharacterTurnOverrides { get; set; }
    = new(StringComparer.OrdinalIgnoreCase);
```

### 6.7 Wire Into Scoring

In `ScoreActorForAutoSelection`, after computing the base score:

```csharp
if (session.CharacterTurnOverrides.TryGetValue(actorName, out var override) && override is not null)
{
    if (!override.ParticipateInAutoContinue)
        return -1;  // Force exclusion
    
    if (override.ResponsePriority.HasValue)
        score += override.ResponsePriority.Value;  // Manual boost
}
```

---

## 7. UI Changes

### 7.1 Scenario Tab — Per-Character Controls

In the Scenario settings tab of `RolePlayWorkspace.razor`, where characters are listed, add per-character controls:

```
┌─────────────────────────────────────────────┐
│ Character: Becky (Wife)                      │
│  ☑ Auto-participate       Priority: [50]    │
│  Position: [Auto ▼]                         │
├─────────────────────────────────────────────┤
│ Character: Dean (OtherMan)                   │
│  ☑ Auto-participate       Priority: [80]    │
│  Position: [Auto ▼]                         │
└─────────────────────────────────────────────┘
```

- **Auto-participate checkbox** → toggles `ParticipateInAutoContinue`
- **Priority slider/input** (0–100, nullable) → sets `ResponsePriority`
- **Position dropdown** (Auto / First / Last) → sets `PreferredPosition`

### 7.2 Settings Panel — Batch Size & Phase Weight Presets

In the Behaviour section of the settings panel:

```
┌─────────────────────────────────────┐
│ Auto-Continue                        │
│  Batch size: [3]  (1–6)             │
│  Phase weighting: [Default ▼]       │
│    • Default (balanced)             │
│    • Stat-heavy (Desire drives)     │
│    • Role-heavy (roles drive)       │
│    • Pure recency (current behavior)│
└─────────────────────────────────────┘
```

The "Phase weighting" preset would modify the stat-weight coefficients in `ComputePhaseStatContribution` and the role-priority table in `GetRolePhasePriority`. This is a stretch goal for V2.

---

## 8. Verification & Testing

### 8.1 Unit Tests

| Test | What It Checks |
|---|---|
| `OpeningPhase_WifeAndPersonaLead` | Opening: persona 100, Wife 80 > OtherMan excluded |
| `BuildUpPhase_HighDesireCharacterBoosted` | High-Desire character scores higher than low-Desire |
| `ClimaxPhase_DominanceWeightsHeavily` | High-Dominance character beats high-Desire in Climax |
| `ClimaxPhase_EncounterParticipantBoosted` | Having-sex character scores higher than non-participant |
| `SemanticEvent_BoostsInvolvedCharacters` | Character in recent event gets +30 |
| `ThemeLinkedCharacter_GetsBoost` | Character linked to primary theme gets +50 |
| `CharacterTurnOverride_ParticipateFalse_Excludes` | Override with `ParticipateInAutoContinue=false` excludes character |
| `CharacterTurnOverride_PriorityAddsToScore` | Override with `ResponsePriority=100` pushes character ahead |
| `Recency_NeverSpoken_GetsMaxBoost` | Character who never spoke gets 200 recency boost |
| `Persona_Opening_FirstSix_InsertedAtFront` | First 6 interactions: persona at position 0 |
| `Persona_PostOpening_EvenTurn_Appended` | After 6, even turn: persona appended |
| `Persona_PostOpening_OddTurn_Skipped` | After 6, odd turn: persona skipped |
| `ScenarioOrderTiebreaker_EqualScores` | Equal-scoring characters ordered by scenario definition |

### 8.2 Integration Tests

Create a session with 4 characters (Wife, Husband, OtherMan, Friend), run multiple overflow continue turns, and verify:
- Opening phase: persona + Wife lead, OtherMan excluded
- BuildUp: Wife high, OtherMan rising, persona low
- Approaching: OtherMan and Wife compete for top
- Climax: Dominance-driven ordering, encounter participants lead

### 8.3 Debug Logging

Extend the existing `OverflowActorSelection` debug event to include per-candidate score breakdown:

```json
{
  "candidates": [
    {
      "rank": 1,
      "name": "Becky",
      "score": 1520,
      "breakdown": {
        "locationBase": 1000,
        "recencyBoost": 200,
        "phaseStatContribution": 85,
        "rolePhasePriority": 90,
        "themeRelevanceBoost": 50,
        "semanticEventBoost": 30,
        "encounterParticipationBoost": 0,
        "scenarioOrderTiebreaker": 10,
        "manualOverride": 0
      }
    }
  ]
}
```

---

## 9. Migration & Backward Compatibility

| Aspect | Strategy |
|---|---|
| **Existing sessions** | No migration needed. `CharacterTurnOverrides` starts empty; scoring uses defaults. |
| **Null stats** | Characters without `CharacterStatProfileV2` get `50` for stat contribution (neutral). |
| **Null role** | Characters without `CharacterRole` get `50` for role priority (neutral). |
| **No semantic events** | All characters get 0 semantic boost. |
| **No themes** | All characters get 0 theme boost. |
| **Location services disabled** | All characters get `500` location base (neutral). |
| **Fallback** | If scoring produces all zeros, existing `ResolveDefaultContinueActor` is used. |

---

## 10. Future Considerations (V2+)

- **Configurable weight presets** — let users save named weight profiles (e.g., "Drama-focused", "Comedy-paced")
- **LLM-assisted ordering** — use a lightweight model call to suggest which 2–3 characters should speak next based on narrative context
- **Per-character "talkativeness" drift** — a character's `ResponsePriority` could drift based on their stat changes (higher Desire → more talkative)
- **Group conversation dynamics** — if two characters have high pairwise interaction stats, they should be more likely to appear in the same turn
- **Narrative pacing sliders** — controls like "character turnover rate" (how quickly focus shifts between characters)
