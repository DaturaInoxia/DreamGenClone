# Issue 2 Deep Dive: First-Turn Character Stat Delta Magnitude

Date: 2026-04-15
Scope: Runtime investigation for first-character stat deltas in RolePlay V2

## Why this file exists

This document captures the full investigation trail (code path + runtime evidence) so we do not need a full codebase re-scan for the same question.

## Session and ID Mapping

User-referenced IDs:
- Session handle provided by user: 355ac0a7d8184c9884586ce59b47821e
- Interaction: 3b0275f1-7383-40d8-8ae3-36a972c37662

Runtime DB evidence:
- Interaction 3b0275f1-7383-40d8-8ae3-36a972c37662 belongs to internal session id 0663ffe3-ba9e-4823-b390-28a31e29c1f9 (Session title: RP1).

## Exact Runtime Evidence for Becky Delta

Adaptive debug event metadata for interaction 3b0275f1-7383-40d8-8ae3-36a972c37662 recorded:
- actorKey: Becky
- statDeltas:
  - Desire: +3
  - Restraint: +3
  - Tension: +4
  - Connection: -3
  - Dominance: -3
  - Loyalty: -4
  - SelfRespect: -2
- statDeltaReasons confirms additive contributors per stat.

Per-stat additive composition from statDeltaReasons:
- Desire = keyword:desire(+1) + infidelity-brief-disappearance(+1) + infidelity-public-facade(+1) = +3
- Restraint = infidelity-brief-disappearance(+1) + infidelity-public-discovery(+1) + infidelity-public-facade(+1) = +3
- Tension = infidelity-brief-disappearance(+1) + infidelity-public-discovery(+2) + infidelity-public-facade(+1) = +4
- Connection = infidelity-brief-disappearance(-1) + infidelity-public-discovery(-1) + infidelity-public-facade(-1) = -3
- Dominance = infidelity-brief-disappearance(-1) + infidelity-public-discovery(-1) + infidelity-public-facade(-1) = -3
- Loyalty = infidelity-brief-disappearance(-1) + infidelity-public-discovery(-2) + infidelity-public-facade(-1) = -4
- SelfRespect = keyword:selfrespect-positive(+1) + infidelity-brief-disappearance(-1) + infidelity-public-discovery(-1) + infidelity-public-facade(-1) = -2

## Why UI values look mismatched vs initial baseline

User baseline snapshot for Becky (scenario baseline):
- Desire 40, Restraint 85, Tension 30, Connection 90, Dominance 45, Loyalty 95, SelfRespect 80

PromptBuilt (before Becky interaction) already showed Becky at:
- Desire 44, Restraint 91, Tension 39, Connection 83, Dominance 42, Loyalty 85, SelfRespect 73

After Becky interaction (using statDeltas above), UI showed:
- Desire 47, Restraint 94, Tension 43, Connection 80, Dominance 39, Loyalty 81, SelfRespect 71

This is internally consistent:
- 44 + 3 = 47
- 91 + 3 = 94
- 39 + 4 = 43
- 83 - 3 = 80
- 42 - 3 = 39
- 85 - 4 = 81
- 73 - 2 = 71

So the apparent mismatch is caused by a pre-interaction shift from baseline to pre-Becky state, then a second shift from Becky interaction.

## Where the pre-interaction shift comes from

During session creation:
1. Scenario/base profile stats are loaded into CharacterStats.
2. SeedFromScenarioAsync runs and can mutate stats before first actor turn by applying:
   - StyleProfile.StatBias (all characters)
   - Theme StatAffinities for scored themes (all characters)

This happens before first non-narrative actor turn.

## Clarifying question answers

### Are keywords coming from theme profile keywords in database tables?

Yes.

When RP theme subsystem is active, runtime theme entries are loaded from RP themes assigned to selected RP theme profile. Theme keywords come from DB table RPThemeKeywords and are flattened into runtime ThemeCatalogEntry.Keywords.

Core tables:
- RPThemes
- RPThemeProfileThemeAssignments
- RPThemeKeywords
- RPThemeStatAffinities

### How do keyword names map to Character Stats if keyword keys are not stat names?

Keywords do not map directly to stats.

Flow:
1. Keywords score themes (theme evidence signal).
2. Scored themes then apply their StatAffinities (which are stat-name keyed) to the acting character.
3. Separately, fixed stat keyword buckets (desire/restraint/tension/etc) also apply direct stat deltas.

### How does StatAffinities apply exactly?

For each scored theme with positive signal in that interaction:
- Iterate theme StatAffinities entries (statName, value).
- Normalize statName to canonical stat (legacy aliases supported).
- Convert raw affinity value to per-interaction delta via normalization:
  - scaledMagnitude = ceil(abs(value) / 3)
  - clamp magnitude to 1..2
  - restore sign
- Add delta to actor stat.

Result: multiple scored themes can stack on the same stat in one interaction.

## Current behavior that drives high first-turn deltas

- Multi-theme stacking is allowed in one turn.
- First-turn/early-turn cap for stat deltas does not exist.
- Early-phase guard currently only suppresses intensity escalation, not stat mutation.

## Option analysis for mitigation

### Option 1: Early-turn per-stat cap

Definition:
- For first N non-narrative actor interactions, cap absolute delta per stat (example: max 1 or 2).

Pros:
- Directly addresses first-turn spikes.
- Predictable and easy to explain.

Cons:
- Can hide meaningful strong signals early.
- May feel too flat if cap is too strict.

### Option 2: Limit affinity stacking to top themes

Definition:
- Only apply StatAffinities from top K scored themes in interaction (example: K=1 or K=2).

Pros:
- Preserves theme-driven behavior while reducing additive bursts.
- Keeps strongest narrative signals.

Cons:
- Requires deterministic tie handling.
- Can drop useful secondary nuance.

### Option 3: Per-interaction global budget clamp

Definition:
- Enforce total absolute delta budget per actor turn (example: sum(abs(deltas)) <= B).

Pros:
- Prevents extreme total movement even if many stats are touched.
- Smooths volatility across all turns.

Cons:
- Requires allocation logic when budget exceeded.
- Can make individual stat movements less transparent unless reason payload includes post-clamp notes.

## Additional implementation approaches

1. Global affinity scale-down for all turns
- Increase normalization divisor (for example from /3 to /4 or /5), reducing all affinity effects.

2. Seed-time damping
- Reduce or cap SeedFromScenarioAsync stat mutations, especially before any actor interaction.

3. Contextual gating for high-impact negative stats
- Require stronger evidence before large Loyalty/SelfRespect drops (example: explicit betrayal markers).

4. Non-linear response curve
- Apply diminishing returns as same-direction deltas repeat in short window.

## Recommended combined direction (practical)

If we combine 1 + 2 + 3, a robust starting recipe is:
1. Early-turn cap for first 3 actor turns: per-stat abs delta <= 2.
2. Affinity stacking limited to top 2 scored themes per interaction.
3. Global per-turn budget clamp (example: sum abs deltas <= 10).
4. Add a debug payload section with pre-clamp vs post-clamp deltas for explainability.

This gives stability without removing adaptive behavior.

## Concrete Policy Table (implemented)

| Policy Area | Rule | Exact Value |
| --- | --- | --- |
| Theme-affinity stacking | Max themes allowed to apply stat affinities per interaction | `1` (top theme only) |
| Theme-affinity phase gate | BuildUp per-stat theme-affinity cap | `0` |
| Theme-affinity phase gate | Committed per-stat theme-affinity cap | `1` |
| Theme-affinity phase gate | Approaching per-stat theme-affinity cap | `1` |
| Theme-affinity phase gate | Climax per-stat theme-affinity cap | `2` |
| Theme-affinity phase gate | Reset per-stat theme-affinity cap | `0` |
| Early-turn per-stat cap | Applies to first actor interactions | first `3` actor turns |
| Early-turn per-stat cap | Max absolute per-stat delta in early turns | `2` |
| Global per-turn budget | Max total absolute stat movement in one interaction | `10` |

Notes:
- Theme scoring/selection still uses full theme set; only theme-to-stat affinity application is limited to top-1.
- Policy cap reasons are appended to debug reason payload when a cap changes the raw deltas.

## Test Matrix (implemented)

- BuildUp theme-affinity suppression:
  - Given theme keywords match and theme has stat affinities,
  - Then BuildUp cap `0` prevents theme-affinity stat mutation.
- Top-1 theme-affinity application:
  - Given multiple matching themes with stat affinities,
  - Then only top ranked theme contributes affinity-based stat deltas.
- Early-turn + global budget caps:
  - Given high-keyword density interaction,
  - Then per-stat absolute deltas are capped to `2` and total absolute movement capped to `10`.

## Code anchors for future follow-up

- Session creation baseline + seeding:
  - DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
- Scenario seeding mutations:
  - DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs (SeedFromScenarioAsync)
- Interaction stat mutation + reasons:
  - DreamGenClone.Web/Application/RolePlay/RolePlayAdaptiveStateService.cs (UpdateFromInteractionAsync)
- Stat normalization catalog:
  - DreamGenClone.Application/StoryAnalysis/AdaptiveStatCatalog.cs
- RP theme persistence tables and schema:
  - DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs
  - DreamGenClone.Infrastructure/RolePlay/RPThemeService.cs
- Debug event viewer page:
  - DreamGenClone.Web/Components/Pages/RolePlayDebug.razor
