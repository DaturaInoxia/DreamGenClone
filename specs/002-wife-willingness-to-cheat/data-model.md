# Data Model & Prompt Contract: Unified "Wife Willingness to Cheat"

**Feature**: `002-wife-willingness-to-cheat` | **Date**: 2026-08-10

---

## 1. Canonical Formulas

**Selected: Option A** (single `WillingnessToCheat` score — see `plan.md` §3.8 for Options B/C and switching detail).

```
willingness = clamp( 50
              + (W.Desire − W.Loyalty) * 0.5
              + (W.SeductionReceptivity − W.BoundaryFirmness) * 0.5
              + ((100 − H.Attentiveness) + (100 − H.IntimacyAvailability)) * 0.25
              , 0, 100 )

verdict   = WillingnessVerdictBands.Resolve(willingness)   // config: 0-40 NO, 41-70 MAYBE, 71-100 YES
ceiling   = min( W.Desire, willingness )                   // explicitness band source
```

Missing inputs default to 50 (neutral). Coefficient values are **persisted config** (`WillingnessCoefficients`), not hardcoded.

### Behavior mapping (unchanged from the user intent)
| Scenario | Verdict | Ceiling | Expected behavior |
|----------|---------|---------|-------------------|
| Low Desire, low willingness | MAYBE/YES | Low (e.g. "Genital Over Clothes") | Cheats but limited to over-clothes contact |
| High Desire, high willingness | YES | High (e.g. "Group Exploration") | Full explicit range |
| Low Desire, high willingness | YES | Low | Cheats, but ceiling capped by Desire |
| High Desire, low willingness | NO/MAYBE | High | Tempted, verdict unresolved, ceiling high if she crosses |

---

## 2. Persistence

### `RolePlayV2AdaptiveStates` — add column
```sql
WillingnessToCheat INTEGER NOT NULL DEFAULT 50
```
- Set on every continuation state-save (recomputed via the Option A score).
- Backfill existing rows to 50 (neutral).
- `MotivationScore` (old, unpersisted) retired; keep only for UI back-compat during transition (§5 decision 4).

### `WillingnessVerdictBands` (new config, JSON persisted)
Maps the `willingness` score to verdict labels:
```json
[
  { "Min": 0,  "Max": 40, "Verdict": "NO" },
  { "Min": 41, "Max": 70, "Verdict": "MAYBE" },
  { "Min": 71, "Max": 100, "Verdict": "YES" }
]
```
> Not hardcoded — UI-backed / seeded config per repo rule.

### `WillingnessCoefficients` (config)
```json
{ "DesireLoyaltyWeight": 0.5, "BehaviorWeight": 0.5, "MaritalDeficitWeight": 0.25 }
```
Persisted tuning scalars for the Option A formula (no hardcoded RP thresholds).

---

## 3. Prompt Output Contract

Emitted by `BehavioralFramesSlot` (Slot 13) for the Wife character (non-Narrative):

```
HARD CONSTRAINT — Wife Willingness to Cheat (authoritative, overrides theme guidance):
  Verdict: {Verdict} — {verdictBand.PromptDirective}
  Ceiling: {ExplicitnessLevel} — {willingness.PromptGuideline}
  Details: Willingness to Cheat = {willingness} (Desire={Desire}, Loyalty={Loyalty}, SeductionReceptivity={SR}, BoundaryFirmness={BF}, Attentiveness={Att}, IntimacyAvailability={Intim}); Ceiling = min(Desire, willingness) = {ceiling}.
```

- The `Details:` line preserves machine-parseable values for the adaptive panel + diagnostics.
- HARD CONSTRAINT framing matches the repo's existing resistance-directive convention (authoritative, overrides theme guidance).

---

## 4. Derived Formula Additions (`RolePlayDerivedFormulaEvaluator`)

```csharp
["WifeWillingnessToCheat"]  = profile => ComputeOptionAWillingness(profile),  // 0-100, single source of truth
["WifeWillingnessVerdict"]  = profile => VerdictScore(profile),               // drives YES/MAYBE/NO bands
["WifeExplicitnessCeiling"] = profile => Math.Min(profile.Desire, ComputeOptionAWillingness(profile)),
```

These feed `FormulaThresholds` in theme fit rules (Stage B) and `DecisionPointService`.

---

## 5. Behavior Mapping (user intent)

| Scenario | Verdict | Ceiling | Expected behavior |
|----------|---------|---------|-------------------|
| Low Desire, low willingness | MAYBE/YES | Low (e.g. "Genital Over Clothes") | Cheats but limited to over-clothes contact |
| High Desire, high willingness | YES | High (e.g. "Group Exploration") | Full explicit range |
| Low Desire, high willingness | YES | Low | Cheats, but ceiling capped by Desire |
| High Desire, low willingness | NO/MAYBE | High | Tempted, verdict unresolved, ceiling high if she crosses |

Verdict and ceiling are coupled via the single `willingness` score — a Wife does not need High Desire to cheat; Desire caps what she'll do *if* she cheats.
