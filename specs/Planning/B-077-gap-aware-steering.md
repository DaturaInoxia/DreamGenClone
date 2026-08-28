# B-077: Gap-Aware Steering — Willingness-Gap Aware "Towards" Directive

**State**: `designed` (plan persisted, pending implementation confirmation)
**Priority**: medium
**Scope**: small (Option A1 — enrich existing 4 steering choices)
**Date**: 2026-08-11

---

## TL;DR

Make the **steering option generator** aware of the **Wife willingness gap** so the `TOWARDS` (and `HARD`) options it produces are geared toward content that would actually close the gap (Loyalty ↓, Restraint ↓, SeductionReceptivity ↑) — **role-aware** (Husband steers neglect, OtherMan steers tension spikes, Wife steers voiced desire) and **without any direct stat changes**. Only the *choices* change; stats still move only through the existing semantic pipeline.

**Explicit constraint (user)**: no direct stat mutation. `B-020` stays out of scope, matching B-075's design boundary.

**Steering scope constraint (user, 2026-08-11)**: the gap-aware steer generator targets **everything except Desire**. The resolver's gap-closing hint set covers **Loyalty ↓ and Restraint ↓ only** (SeductionReceptivity rides along as drift from Restraint ↓, per `StatToDimensionMappings` −0.60 rule). **Desire is deliberately out of scope**: Desire ↑ will come naturally once willingness is on (post-gap, via the existing semantic pipeline), so steering must not try to manufacture Desire gains to close the gap — it would be out-of-order and fight the phase/tempo. Desire-driven hints are excluded from the per-role gap-closing hints.

**Primary approach**: **A1 — enrich the existing 4-direction choices** with a gap-aware context block injected into both steer-generation prompts. A2 (new 5th "Gap" category) is documented as an alternative but NOT recommended.

---

## Background (verified findings)

### The steering pipeline (verified in code)

Steering options are generated in **two places**, both building the same 4-choice (Away / Neutral / Towards / Hard) per-character prompt:

| Path | Location | Trigger |
|---|---|---|
| **UI** | `RolePlayWorkspace.razor` → `BuildAllCharacterSteerPromptAsync` (L5380) | User clicks Generate in the steer popup |
| **Background** | `SteerGenerationJobHandler.BuildGenerationPrompt` | Auto-steer after each turn (when `EnableAutoSteer`) |

Both prompts already include: phase, active theme, per-character role context (`SteerRoleIntentCatalog.GetRoleContext`), behavioral dimension tier texts, stat state texts, the 4 direction intents (`SteerRoleIntentCatalog.GetIntent(role, direction)`), and recent scene context.

**What they do NOT include:** the Wife willingness score, verdict, ceiling, or the gap to the next verdict tier. This is the blind spot — the model picks "Towards" content with zero awareness of how far the Wife is from willing.

### The willingness math (verified for session e4f057aa)

`WifeWillingnessCalculator.ComputeWillingnessToCheat` (Application/RolePlay):
```
willingness = clamp(50 + (Desire−Loyalty)·DW + (SedRecept−BoundFirm)·BW + ((100−Att)+(100−Intim))·MDW, 0, 100)
```
- Weights from `ScenarioEngineSettings`: DW=0.5, BW=0.5, MDW=0.25.
- Verdict: `willingness ≤ NoMax(40)` → NO; `≤ MaybeMax(70)` → MAYBE; else YES.
- Ceiling: `min(Desire, willingness)` → resolves the Willingness Profile band.

Session e4f057aa (Becky): Desire=46, Loyalty=93, SedRecept=15, BoundFirm=78, Att=50, Intim=50 → **willingness 20, NO, ceiling 20 (Heated Make-Out)**. To reach MAYBE (41): Loyalty 93→~69 or Restraint 80→~52 (single-lever, coupled with dimension drift).

### What actually moves Loyalty / Restraint (verified from `RPThemeSemanticStatMappings`)

Semantic inference only fires events the **active theme's mapping table** allows; those events map to stat deltas + reason codes. Gap-closing events (Direction=decrease):

**Loyalty ↓ (93→69, need −24):**
| Event | Δ | Reason |
|---|---|---|
| `emotional-transference` | −3 | affection-moves-on |
| `husband-neglect-felt` | −2 | bond-fraying |
| `wife-desire-others` | −2 | exhibitionism-wife-desire-loyalty |
| `excuse-cover` (lying) | −4 | deception-loyalty-cost |
| `betrayal-committed` | −5 | loyalty-broken |
| `emotional-surrender` | −1 | loyalty-lost |

**Restraint ↓ (80→52, need −28):**
| Event | Δ | Reason |
|---|---|---|
| `desire-spoken` | −1 | desire-breaks-restraint |
| `tension-spike` | −1 | composure-cost |
| `forbidden-touch` | −1 | exhibitionism-touch-inhibition |
| `emotional-surrender` | −2 | restraint-gone |
| `mutual-engagement` | −1 | inhibition-falls |
| `resistance-breaks` | −1 | resistance-gone |

### Steering never mutates stats

B-075 design (verified): steer execution injects a per-character directive into the next continuation's prompt; stats move only via the semantic pipeline. `RolePlaySteeringDirective` has **no StatDeltas field**. B-020 (steer → stat changes) is deliberately unimplemented. This feature preserves that boundary.

---

## Design (Option A1)

### 1. Shared gap resolver — single source of truth

New helper **`WillingnessSteerGapResolver`** (Application/RolePlay):

- **Inputs**: session snapshots (`CharacterStatProfileV2` per role), `ScenarioEngineSettings` (weights + verdict bounds), active theme's semantic stat mappings.
- **Outputs**:
  - `Willingness` / `Verdict` / `Ceiling` (reuse `WifeWillingnessCalculator`)
  - `HasGap` — Wife present && willingness below a target verdict tier
  - **Per-role gap-closing event hints**, derived from the **active theme's actual `RPThemeSemanticStatMappings`**:
    - `Loyalty ↓` events → `husband-neglect-felt`, `emotional-transference`, `excuse-cover`, …
    - `Restraint ↓` events → `desire-spoken`, `tension-spike`, `forbidden-touch`, …
    - mapped to the role that can plausibly produce them (Husband→neglect, OtherMan→tension, Wife→voiced desire)
    - **Desire-directed hints are excluded** (user constraint): Desire ↑ comes only after willingness breaks through, via the existing semantic pipeline — steering never targets Desire to close the gap.
  - `TargetVerdict` (config-driven; default "MAYBE")

**Config-driven, no hardcoded values** (satisfies the repo no-fallback rule): event hints come from the DB mapping table; verdict thresholds come from `ScenarioEngineSettings`. One resolver shared by both prompt builders — avoids the "duplicated configuration-source resolution logic" forbidden pattern.

**Self-enforcing Desire exclusion**: the resolver extracts hints only from `RPThemeSemanticStatMappings` rows where `TargetStat ∈ {Loyalty, Restraint}` **and** `Direction = 'decrease'`. This structurally excludes Desire **by the explicit `TargetStat` whitelist** (Desire is not in `{Loyalty, Restraint}`), regardless of any Desire row's `Direction`. Verified 2026-08-11 against `dreamgenclone.dev.db`: there ARE 6 `Desire` + `Direction='decrease'` rows (`sexual-release` ×5, `orgasm` ×1 across themes incl. `ntr-open-world`), so the exclusion is *not* self-enforcing by Direction projection alone — it relies on the `TargetStat` whitelist. The resolver MUST NOT widen its `TargetStat` set without also reconsidering the Desire exclusion. Comparison on `Direction` must use `StringComparison.OrdinalIgnoreCase` (matching existing codebase convention at `RolePlayAdaptiveStateService.cs:1008,1106`).

### 2. Gap-aware context block (injected into both steer prompts)

When `HasGap` is true, both `BuildAllCharacterSteerPromptAsync` and `SteerGenerationJobHandler.BuildGenerationPrompt` append a block:

```
Willingness gap: Becky willingness=20 (NO), ceiling=20 (Heated Make-Out).
To move her toward willing, steer content that triggers (per the active theme's mappings):
  - Husband: feeling neglected / emotionally absent (→ Loyalty ↓)
  - OtherMan: tension spikes, escalating closeness (→ Restraint ↓)
  - Wife: voicing desire, emotional transference (→ Restraint ↓ / Loyalty ↓)
Generate TOWARDS (and HARD) options that advance these beats naturally — do not force outcomes.
```

This makes **all four choices** gap-aware and role-aware.

### 3. Config surface (no-fallback compliance)

- Event hints: read from existing `RPThemeSemanticStatMappings` (already DB config).
- Directive prose + enable flag: new optional fields on **`ScenarioEngineSettings`** (already holds willingness weights/verdict bounds):
  - `WillingnessGapSteeringEnabled` (bool)
  - `WillingnessGapSteeringDirective` (string — the block prose; empty → block not emitted)
- Fail fast when enabled but directive missing/empty (no silent fallback).

### 4. Alternative (NOT recommended): A2 — new 5th "Gap" category

Add a distinct `Gap` direction: `SteerDirection` enum value + `SteerRoleIntentCatalog` entries ×3 roles + prompt emits 5 options + JSON shape `{away,neutral,towards,hard,gap}` + `RolePlaySteeringDirective` + UI labels/pager + `StagedDirectionsSlot`. Larger blast radius (~8 files + persisted-option shape). Only worth it if a user-visible distinct "gap" action is required. **A1 delivers the goal with far less risk.**

---

## File list (A1)

| File | Change |
|---|---|
| `DreamGenClone.Application/RolePlay/WillingnessSteerGapResolver.cs` | **NEW** — gap computation + per-role event hints from mappings |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` | `BuildAllCharacterSteerPromptAsync` appends gap block |
| `DreamGenClone.Web/Application/RolePlay/SteerGenerationJobHandler.cs` | `BuildGenerationPrompt` appends gap block (shared resolver) |
| `DreamGenClone.Domain/RolePlay/ScenarioEngineSettings.cs` | Add `WillingnessGapSteeringEnabled` + `WillingnessGapSteeringDirective` |
| `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor` | UI for the two new settings (optional surface) |
| `DreamGenClone.Tests/RolePlay/WillingnessSteerGapResolverTests.cs` | **NEW** — resolver unit tests |
| `DreamGenClone.Tests/RolePlay/Prompts/SteerPromptTests.cs` | Both builders emit block when gap present; absent when not |

## Blast radius

- **Prompt-content change to steer generation only.** No stat mutation, no semantic pipeline change, no DB schema change (event hints read existing tables), no continuation-prompt slot changes.
- Both steer entry points (UI + background) affected — kept in sync via the shared resolver.
- Tests added; existing steer/B-075 tests must still pass.

---

## Open items / verification

- [ ] Confirm A1 vs A2 (recommend A1).
- [ ] Confirm config fields live on `ScenarioEngineSettings` vs `SteeringProfile`.
- [ ] Live-check which gap-closing events have already fired in a target session to tune directive prose.
- [ ] **Background-path plumbing (gap found 2026-08-11)**: `SteerGenerationJobHandler` ctor injects `ISqlitePersistence` but NOT `IScenarioEngineSettingsRepository` nor `IRPThemeService`; `SteerGenerationJobPayload` carries neither the willingness config (weights/verdict bounds) nor the active theme's `RPSemanticStatMapping` list. Pick one:
  - **(a)** add `IScenarioEngineSettingsRepository` + `IRPThemeService` to `SteerGenerationJobHandler`'s ctor so the resolver can read both at runtime;
  - **(b) [RECOMMENDED]** snapshot `ScenarioEngineSettings` (or just the willingness weights/bounds) and the resolved `RPSemanticStatMapping` list into `SteerGenerationJobPayload` at enqueue time (`EnqueueSteerGenerationJob` in `RolePlayEngineService.cs`). Matches the existing B-075 "payload snapshots everything the builder needs, job doesn't re-read the session" design cited in this plan. Keeps `BuildGenerationPrompt` a pure function of the payload.
- [ ] Build web + tests clean; run new resolver + steer-prompt tests.
- [ ] **Desire-exclusion rationale corrected 2026-08-11**: Desire is excluded by the resolver's `TargetStat ∈ {Loyalty, Restraint}` whitelist, NOT by Direction projection (DB has 6 `Desire`+`decrease` rows). Resolver MUST keep the `TargetStat` whitelist intact to preserve the user's "steering targets everything except Desire" constraint.
