# Current Formula Analysis — Wife Willingness to Cheat

**Date**: 2026-08-10
**Scope**: Document exactly what the current formula computes today, what lives in the willingness/resistance profiles, and provide analysis + suggestions for a simpler formula that centers **Desire + Loyalty + Behavioral Dimensions**.

---

## 1. What the current formula actually does

There are **two independent computations** plus a set of **derived formulas**. None of them share a single source of truth.

### 1.1 Motivation → Resistance (the "will she cheat" path)

Source: `DreamGenClone.Infrastructure/RolePlay/ScenarioGuidanceGenerator.cs`

```csharp
motivationScore = clamp( ((100 − H.Attentiveness)
                        + (100 − H.IntimacyAvailability)
                        + (100 − W.SelfRespect)
                        + OM.PersistencePastLimits) / 4,  0, 100 )

effectiveLoyalty = min( W.Loyalty + motivationScore, 100 )

verdictBand = ResistanceProfile.ResolveBand(effectiveLoyalty)   // 20 bands on Loyalty
```

- **Term owners are mixed**: 2 Husband dimensions, 1 Wife canonical stat (SelfRespect), 1 OtherMan dimension.
- **Direction**: higher = more willing to cheat (neglect/persistence push effective Loyalty *down*, so she lands in more permissive bands).
- **Saturation**: neutral 50/50/50/50 → motivation 50 → any Wife with Loyalty ≤ 50 resolves to effective 100. Over-dominates.

### 1.2 Desire → Willingness (the "how far will she go" path)

```csharp
ceilingBand = WillingnessProfile.ResolveBand( W.Desire )   // 20 bands on Desire
```

- **Pure Desire lookup** — no motivation, no behavioral dimensions, no Loyalty.
- This is the "explicitness ceiling" (what acts she's willing to do *with the other man*).

### 1.3 Derived formulas (for fit rules / gates — NOT used in the verdict)

`DreamGenClone.Application/RolePlay/RolePlayDerivedFormulaEvaluator.cs`:

| Formula | Definition |
|---------|-----------|
| RiskAppetite | `Tension + Desire/2 − Restraint/2 − Loyalty/3` |
| EscalationResistance | `Restraint + Loyalty − Desire` |
| Vulnerability | `100 − Dominance − SelfRespect/2 + Connection/2` |
| EmotionalVolatility | `Tension − Restraint/2 − Connection/3` |
| IntimacyCapacity | `Connection + Desire + Restraint/2` |
| BoundariesStrength | `SelfRespect + Restraint + Loyalty/2` |
| ConsentThreshold | `SelfRespect + Dominance + Restraint − Desire/2` |
| SubmissivenessCapacity | `100 − Dominance − SelfRespect/2 + Connection` |
| HotwifeCompatibility | `Desire + Connection − Dominance + (100 − Loyalty)` |
| DeceptionCapacity | `Restraint + (100 − Connection) − Tension` |

These are **complex, overlapping, and currently disconnected** from the prompt verdict.

---

## 2. What's in the willingness profile

`StatWillingnessProfiles` — "Married Woman Baseline" (TargetStatName = **Desire**), 20 contiguous bands 0–100:

| Desire range | Explicitness Level |
|---|---|
| 0–5 | Purely Emotional |
| 6–10 | Hand-Holding |
| 11–15 | Forehead Kisses |
| 16–20 | Closed Mouth Kissing |
| 21–25 | Open Mouth Kissing |
| 26–30 | Light Petting |
| 31–35 | Breast Over Clothes |
| 36–40 | Under Clothes |
| 41–45 | Genital Over Clothes |
| 46–50 | Manual Stimulation |
| 51–55 | Oral Receiving |
| 56–60 | Oral Giving |
| 61–65 | Cowgirl Top |
| 66–70 | Doggy Style |
| 71–75 | Confident Positions |
| 76–80 | Toys |
| 81–85 | Rough Play |
| 86–90 | Anal |
| 91–95 | Public Risk |
| 96–100 | Group Exploration |

Each band carries a `PromptGuideline` + `ExampleScenarios` injected as a "Willingness band ..." line.

`StatResistanceProfiles` — "Married Woman Resistance" (TargetStatName = **Loyalty**), 20 bands:
No Resistance → Token → Weak → Pliable → Hesitant → Ambivalent → Conditional → Reluctant Gate → Selective Boundary → Balanced Guard → Moderate Firmness → Guarded → Firm Boundaries → Steadfast → Strong → Very Strong → Near-Immovable → Rigidly Faithful → Untouchable → Unbreakable Vow.

---

## 3. What the behavioral dimensions offer (available but unused by the formula)

From `BehavioralDimensionCatalog.cs` (all present at runtime via `RuntimeEncounterStats`):

- **Wife**: `DiscoveryCaution`, `Exhibitionism`, `EmotionalEngagement`, `PostEncounterGuilt`, `BoundaryFirmness`, `SeductionReceptivity`
- **Husband**: `Attentiveness`, `IntimacyAvailability`, `Awareness`, `Acceptance`, `Voyeurism`, `Participation`, `Encouragement`, `RiskTolerance`
- **OtherMan**: `HusbandAwareness`, `MarriageContextUse`, `DiscoveryRisk`, `PersistencePastLimits`

`StatToDimensionMappings` already drifts these from stats:
- Desire → Exhibitionism (+0.90), SeductionReceptivity (+0.45), DiscoveryCaution (−0.25)
- Restraint → BoundaryFirmness (+0.90), SeductionReceptivity (−0.60), DiscoveryCaution (+0.40), Exhibitionism (−0.60), PostEncounterGuilt (+0.45)
- Loyalty → BoundaryFirmness (+0.75), PostEncounterGuilt (+0.75), EmotionalEngagement (+0.60)
- SelfRespect → BoundaryFirmness (+0.60), DiscoveryCaution (+0.40)

So the Wife's behavioral dimensions **already encode** Desire/Loyalty/Restraint/SelfRespect, but the willingness formula reads raw stats instead of these richer, already-available signals.

---

## 4. Analysis — why the current formula is over-complex

1. **Two disconnected band systems** — Loyalty→resistance (verdict) and Desire→willingness (ceiling) never cross-talk, yet the user wants one unified "Wife Willingness to Cheat" state.
2. **Mixed term owners** — motivation mixes Husband neglect + Wife SelfRespect + OtherMan persistence with equal weight; the Wife's own willingness dimensions (BoundaryFirmness, SeductionReceptivity) are absent from the verdict.
3. **Pure Desire ceiling ignores behavior** — a high-Desire wife with high BoundaryFirmness gets the full explicitness range; a low-Desire wife can never cheat much even if her verdict is YES.
4. **Saturation** — `min(Loyalty + motivation, 100)` pegs too easily; neutral inputs already dominate.
5. **Duplicate machinery** — the 10 derived formulas overlap each other and the two profile paths; none feed the prompt.
6. **The UI contradicts the engine** (panel subtracts motivation; server adds it; `MotivationScore` is never persisted).

---

## 5. Suggestions — a simpler unified formula

### Principle
Center on the **Wife's own state**, expressed as **Desire + Loyalty + a small set of behavioral dimensions**, with relationship/pursuit context as secondary inputs. One `WillingnessToCheat` score drives both verdict and ceiling.

### Option A — Minimal (recommended): three-signal willingness score

```
willingness = clamp( 50
              + (W.Desire − W.Loyalty) * 0.5                       // own drive vs commitment
              + (W.SeductionReceptivity − W.BoundaryFirmness) * 0.5 // behavioral receptivity vs firmness
              + ((100 − H.Attentiveness) + (100 − H.IntimacyAvailability)) * 0.25   // marital deficit
              , 0, 100 )

verdict   = verdictBands.Resolve(willingness)        // YES / MAYBE / NO   (config, not hardcoded)
ceiling   = min( Desire, willingness )               // can't exceed her drive
```

- 3 signals, all already available; no mixed owners; Wife-owned terms dominate.
- Verdict: willingness low → NO, mid → MAYBE, high → YES.
- Ceiling: bounded by Desire so a low-Desire wife with a YES verdict still has a low explicitness ceiling (over-clothes vs intercourse).

### Option B — Reuse the derived formulas as the core

```
willingness = clamp( 100 − EscalationResistance, 0, 100 )
            // = 100 − (Restraint + Loyalty − Desire) = Desire + 100 − Restraint − Loyalty
verdict   = verdictBands.Resolve(willingness)
ceiling   = min( Desire, 100 − BoundariesStrength )
```

- Already computed in `RolePlayDerivedFormulaEvaluator`; minimal new code.
- `EscalationResistance = Restraint + Loyalty − Desire` is remarkably close to the user's "Desire + Loyalty" instinct.
- Still ignores Wife behavioral dimensions (BoundaryFirmness/SeductionReceptivity).

### Option C — Full behavioral blend (richest, still simple)

```
drive      = (W.Desire * 0.4) + (W.SeductionReceptivity * 0.3) + ((100−W.Loyalty) * 0.3)
restraint  = (W.BoundaryFirmness * 0.5) + (W.Restraint * 0.3) + (W.SelfRespect * 0.2)
willingness = clamp( 50 + drive − restraint + husbandNeglect + persistence, 0, 100 )
verdict    = verdictBands.Resolve(willingness)
ceiling    = min( Desire, clamp(50 + drive − restraint, 0, 100) )
```

### Verdict band mapping (config, default suggestion)
```
0–40   → NO
41–70  → MAYBE
71–100 → YES
```

---

## 6. Recommendation

- **Adopt Option A** (or C) — a single `WillingnessToCheat` score in `[0,100]` derived from **Desire + Loyalty + Wife behavioral dimensions** (`SeductionReceptivity`, `BoundaryFirmness`), with **Husband neglect** as a secondary modifier.
- **Derive the ceiling from the same score**, bounded by Desire, so the two questions ("will she" and "how far") come from one coherent state.
- **Replace the 10 overlapping derived formulas** with a small set (`WifeWillingnessVerdict`, `WifeExplicitnessCeiling`) consumed by both the prompt and fit rules.
- **Persist `MotivationScore` (→ `WillingnessToCheat`)** and make the UI render the same number the engine uses (add, not subtract).
- **Config, not hardcoded**: verdict-band mapping and the coefficient are UI-backed persisted settings.
