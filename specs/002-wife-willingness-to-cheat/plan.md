# Implementation Plan: Unified "Wife Willingness to Cheat" Algorithm

**Branch**: `002-wife-willingness-to-cheat` | **Date**: 2026-08-10
**Related backlog**: B-009 (resistance & motivation gap, `designed` — superseded by this plan), B-030 (woman willingness profile, `new`), B-034 (wife willingness refactor, `planned`), B-054 (stat profile directive injector, `planned`), B-073/S-020 (resistance band ignores narrative state)
**Predecessor specs**: `specs/001-wife-resistance-motivation/*`, `specs/v2/Stat-Based Willingness System.md`, `specs/Planning/WifeResistance.md`

---

## 1. Objective

Build one coherent, prompt-injected **"Wife Willingness to Cheat"** algorithm that answers **two** questions for every Wife in a session:

1. **Will she cheat?** → a *verdict* (YES / MAYBE / NO) from a single **`willingness` score** (Option A — Desire, Loyalty, and Wife behavioral dimensions; see §3.1/§3.8).
2. **How far will she go if she does?** → an *explicitness ceiling* = `min(Desire, willingness)` resolved through the **Willingness Profile** bands (what she's willing to do *with the other man*).

The verdict is the **flag** the NTR / open-world themes consume going forward — replacing their current practice of reading raw `Desire`/`Loyalty`/`Restraint`/`SelfRespect` directly from fit rules and guidance prose.

**Key conceptual point (user):** A Wife does *not* need High Desire to cheat. Low Desire means she is less willing to do *much* (e.g. rubbing over clothes vs full intercourse) — Desire governs the **ceiling of acts**, not the **decision to cross**. The decision is governed by the `willingness` score (Desire vs Loyalty + behavioral receptivity/firmness under marital context).

---

## 2. Current State Audit (verified 2026-08-10)

### 2.1 The good (already implemented server-side)

- **Resistance Profile** — `StatResistanceProfiles` table + `StatResistanceProfileService` (seeded "Married Woman Resistance", Loyalty-target, 20 contiguous bands 0–100: No Resistance → Unbreakable Vow). ✅
- **Motivation score** — `ScenarioGuidanceGenerator.ComputeMotivationScore`: `((100−Attentiveness) + (100−IntimacyAvailability) + (100−SelfRespect) + PersistencePastLimits) / 4` with missing→50 neutral. ✅ (formula correct server-side)
- **Effective stat** — `effectiveStat = min(Loyalty + motivation, 100)` in `BuildResistanceInterpretationAsync`. ✅ (matches spec)
- **Willingness Profile** — `StatWillingnessProfiles` table + service (seeded "Married Woman Baseline", Desire-target, 20 contiguous bands 0–100: Purely Emotional → Group Exploration). ✅ (pre-existing, partial)
- **Band output format** — server emits `Resistance band 'X' (min-max) from Loyalty=.. (effective=.., motivation=..): {PromptDirective}` and `Willingness band 'X' (min-max) from Desire=..: {PromptGuideline}` appended to `GuidanceText`.

### 2.2 The broken / missing

| # | Defect | Evidence |
|---|--------|----------|
| 1 | **UI computes effective Loyalty backwards** | `RolePlayWorkspace.razor` `GetEffLoyalty()` = `Math.Max(loyalty - GetMotScore(), 0)` — **subtracts** motivation; server/spec **add** it. Displayed "Eff. Loyalty 12 (Loyalty − Motivation)" with Motivation 55 shows 12 → wrong band in panel. |
| 2 | **`MotivationScore` never set** | `AdaptiveScenarioState.MotivationScore` (int) declared but **never assigned** in any server code; **not persisted** (no column in `RolePlayV2AdaptiveStates`). UI reads it → always 0. |
| 3 | **Bands never reach the prompt** | `RolePlayContinuationService.BuildPromptViaBuilderAsync` copies only `CharacterBehavioralFrames` + `CharacterStatStateTexts` into `PromptBuildContext`. The merged `guidance.GuidanceText` carrying **both** band lines is **dropped** in the 17-slot path. `BehavioralFramesSlot` (Slot 13) renders frames + stat states but not the band directives. Legacy `RolePlayAssistantPrompts.AppendScenarioGuidance` (which emitted them) is only referenced by tests. Verified: **0** stored prompts in session 7763f8a8 contain "Resistance band" or "Willingness band". (Recorded in debug `specs/001-rp-prompt-redesign/debug/008-stat-state-texts-missing.md` — its resolution fixed frames/stats but **not** the band directive.) |
| 4 | **NTR themes read raw stats directly** | `ntr-open-world` `ow-fit-wife` (Desire≥40, Loyalty≤60), `wife-reawakening` `wr-fit-wife` (Desire≥55, Loyalty≤50, Restraint≤55, SelfRespect≤50), `infidelity-public-facade-exhibition` `pfex-fit-wife` (Desire≥50, Restraint≤35, DiscoveryCaution≤25, Exhibitionism≥65) + guidance prose "Her Desire, Loyalty, Restraint, SelfRespect ... are the sole authorities". These must consume the willingness flag instead. |
| 5 | **Selection gate is raw Desire only** | `ScenarioEligibilityService.ResolveWillingnessTier` uses **average Desire** → High/Medium/Low/Blocked (Stage A). Does not reflect motivation or willingness verdict. |
| 6 | **Formula saturation risk** | Spec + quickstart both flag `min(Loyalty + motivation, 100)` pegs to 100 too easily (neutral 50/50/50/50 → motivation 50 → Loyalty 50 → effective 100). |
| 7 | **Two divergent spec sources** | `001-wife-resistance-motivation` (adds motivation) vs `v2/Stat-Based Willingness System` (Desire-only, no motivation). Need one canonical definition. |

---

## 3. Target Design

### 3.1 Selected formula — **Option A** (single `WillingnessToCheat` score)

**Decision (2026-08-10):** Option A is the recommended and selected approach. It produces one coherent score that drives BOTH the verdict and the ceiling, with the Wife's own terms (Desire, Loyalty, behavioral dimensions) dominant. See **§3.8 Option Analysis** for the full comparison and the data needed to switch to Option B or C later.

```
willingness = clamp( 50
              + (W.Desire − W.Loyalty) * 0.5                       // own drive vs commitment
              + (W.SeductionReceptivity − W.BoundaryFirmness) * 0.5 // behavioral receptivity vs firmness
              + ((100 − H.Attentiveness) + (100 − H.IntimacyAvailability)) * 0.25   // marital deficit
              , 0, 100 )

verdict = WillingnessVerdictBands.Resolve(willingness)  // config: 0-40 NO, 41-70 MAYBE, 71-100 YES
ceiling = min( W.Desire, willingness )                  // bounded by her drive
```

- All inputs are available at runtime (canonical stats + `RuntimeEncounterStats` behavioral dimensions).
- **Verdict** (`YES/MAYBE/NO`) is the flag NTR/open themes consume.
- **Ceiling** is the explicitness level the Willingness Profile resolves from `min(Desire, willingness)`.
- Missing dimension values default to 50 (neutral), consistent with current code.
- `WillingnessVerdictBands` and the coefficient are **persisted config** (UI-backed), not hardcoded (repo rule).
- The existing `StatResistanceProfiles` (Loyalty bands) and `StatWillingnessProfiles` (Desire bands) remain as the **explicitness/verdict label sources** — Option A maps `willingness` → verdict bands (config) and `min(Desire, willingness)` → willingness bands. The raw `motivationScore` / `effectiveLoyalty` path is **retired** (replaced by `willingness`).

### 3.2 The two outputs (both injected)

**A. Verdict (from `willingness` via verdict bands)** — the flag:
- `YES / MAYBE / NO` label + the resolved band's `PromptDirective`.
- The verdict is the **decision-to-cross** gate.
- NTR/open themes use this as the flag instead of raw Loyalty/Desire reads.

**B. Explicitness ceiling (from `min(Desire, willingness)` via Willingness Profile)** — the "what she'll do with the other man":
- Band label + `PromptGuideline` → e.g. "Light Petting ... Cowgirl Top ... Group Exploration".
- **Resolved from `StatWillingnessProfiles`** (the Wife Willingness Profile catalog — 20 bands: Purely Emotional → Group Exploration, each carrying a `PromptGuideline` + `ExampleScenarios`).
- Under Option A the lookup value is **`min(Desire, willingness)`**, not raw Desire — so `BuildWillingnessInterpretationAsync` must be modified to resolve the band from `min(Desire, willingness)` instead of the current raw `ResolveAverageForStat`.
- Lower Desire = lower ceiling = less willing to do *much* (rubbing over clothes vs full intercourse), even if she cheats.
- This is the **act ceiling** — and it is now tied to the same score as the verdict (via `min(Desire, willingness)`), so a Wife cannot have a low ceiling while scoring high on willingness; her drive (`Desire`) caps what she'll do.

### 3.3 Unified prompt output (new format, both lines together)

```
HARD CONSTRAINT — Wife Willingness to Cheat (authoritative, overrides theme guidance):
  Verdict: {Verdict} — {verdictBand.PromptDirective}
  Ceiling: {ExplicitnessLevel} — {willingness.PromptGuideline}
  Details: Willingness to Cheat = {willingness} (Desire={Desire}, Loyalty={Loyalty}, SeductionReceptivity={SR}, BoundaryFirmness={BF}, Attentiveness={Att}, IntimacyAvailability={Intim}); Ceiling = min(Desire, willingness) = {ceiling}.
```

> Preserve a machine-parseable trailing `Details:` line for the adaptive panel / diagnostics (see §5 decision 3).

### 3.4 Where it plugs in (prompt path)

```
ScenarioGuidanceGenerator
  ├── ComputeWillingnessToCheat()          [NEW — Option A score]
  ├── BuildResistanceInterpretationAsync  [MODIFIED — verdict from willingness bands]
  └── BuildWillingnessInterpretationAsync [MODIFIED — ceiling from min(Desire, willingness)]
        │  both currently appended to GuidanceText (dropped in 17-slot path)
        ▼
ScenarioGuidanceContextFactory.CreateFromGeneratorAsync
        │  mergedGuidanceText = GuidanceText + emphasis + avoidance   [already]
        ▼
RolePlayContinuationService.BuildPromptViaBuilderAsync
        │  NEW: also copy guidance.GuidanceText → PromptBuildContext
        ▼
NEW PromptBuildContext field: `ScenarioGuidanceText` (or reuse existing)
        ▼
BehavioralFramesSlot (Slot 13) OR ScenarioGuidanceSlot (Slot 14) emits
        the unified "Wife Willingness to Cheat" HARD CONSTRAINT block
```

Decision needed (§5): **which slot** owns the unified block — `BehavioralFramesSlot` (Slot 13, per-character, matches debug-008's intent that Wife resistance lives there) vs `ScenarioGuidanceSlot` (Slot 14, phase steering). Recommend **BehavioralFramesSlot (Slot 13)** so it rides with the Wife's behavioral frame + stat-state texts and stays filtered to the Wife character.

### 3.5 `WillingnessToCheat` lifecycle (fix; supersedes `MotivationScore`)

- Persist `WillingnessToCheat` on `RolePlayV2AdaptiveStates` (new column `WillingnessToCheat INTEGER NOT NULL DEFAULT 50` — the Option A score). Keep `MotivationScore` for compatibility or drop it (see §5 decision 4).
- Set it wherever the adaptive state is updated after a continuation (alongside `CharacterSnapshots`), recomputing via `ComputeWillingnessToCheat`.
- Fix UI `GetEffLoyalty()` to **render the Option A score**, not the old `Loyalty − Motivation`: display `WillingnessToCheat = {score}`, verdict band, and ceiling band — one number everywhere.

### 3.6 NTR theme migration

Replace raw-stat fit-rule clauses + guidance prose with the **verdict flag**:

| Theme | Current raw reads | New |
|-------|-------------------|-----|
| `ntr-open-world` | ow-fit-wife: Desire≥40, Loyalty≤60 | Keep as coarse fit signal OR switch to `Verdict ∈ {YES, MAYBE}`. Guidance prose switches to "the Wife's willingness verdict + explicitness ceiling are the authorities" |
| `wife-reawakening` | wr-fit-wife: Desire≥55, Loyalty≤50, Restraint≤55, SelfRespect≤50 | Add formula threshold `WifeWillingnessVerdict` (see §3.7) |
| `infidelity-public-facade-exhibition` | pfex-fit-wife: Desire≥50, Restraint≤35, DiscoveryCaution≤25, Exhibitionism≥65 | Keep exhibitionism-specific clauses (structural), but the *decision* reads the verdict |

**Principle:** raw-stat clauses that *gate the decision* (Desire/Loyalty/Restraint) become `WifeWillingnessVerdict`/`WifeExplicitnessCeiling` formula thresholds; clauses that encode *flavor/structural constraints* (Exhibitionism, DiscoveryCaution, location) stay as-is.

### 3.7 Formula thresholds (reuse existing machinery)

`RolePlayDerivedFormulaEvaluator` already supports named formulas consumed by `CharacterStateScenarioMapper` (`FormulaThresholds`) and `DecisionPointService`. Add the **Option A** score as derived formulas (shared single source of truth):

```csharp
// Option A core (shared with ScenarioGuidanceGenerator via a single helper)
["WifeWillingnessToCheat"]     = profile => ComputeOptionAWillingness(profile),  // the Option A score
["WifeWillingnessVerdict"]     = profile => ComputeOptionAWillingness(profile),  // 0-100 → YES/MAYBE/NO bands
["WifeExplicitnessCeiling"]    = profile => Math.Min(profile.Desire, ComputeOptionAWillingness(profile)),
```

Where `ComputeOptionAWillingness(profile)` is the shared Option A formula (§3.1) — the single source of truth used by both `ScenarioGuidanceGenerator` and the derived-formula evaluator. These expose the willingness state to fit rules / gates / decisions without each theme re-deriving it.

### 3.8 Option Analysis — full detail for future switching

> **Status (2026-08-10):** Option A is **selected**. Options B and C are documented below with enough detail (formulas, term owners, code touch points, trade-offs) that the algorithm can be switched later without re-deriving the analysis. All three options share the same **output contract** (§3.3), **persistence** (§3.5), **derived-formula surface** (§3.7), and **NTR migration** (§3.6) — switching changes only the score computation and the verdict/ceiling mapping, not the surrounding wiring.

#### Shared contract (identical across all options)

- `willingness` → 0-100 score.
- `verdict` = `WillingnessVerdictBands.Resolve(willingness)` — config `0-40 NO, 41-70 MAYBE, 71-100 YES`.
- `ceiling` = `min(Desire, willingness)` (Option A/C) or `min(Desire, 100 − BoundariesStrength)` (Option B).
- Missing inputs → 50 (neutral).
- Single source of truth: a shared `WifeWillingnessToCheat(profile)` helper called by both `ScenarioGuidanceGenerator` and `RolePlayDerivedFormulaEvaluator`.

---

#### Option A — Minimal three-signal willingness score **(SELECTED)**

```
willingness = clamp( 50
              + (W.Desire − W.Loyalty) * 0.5
              + (W.SeductionReceptivity − W.BoundaryFirmness) * 0.5
              + ((100 − H.Attentiveness) + (100 − H.IntimacyAvailability)) * 0.25
              , 0, 100 )
ceiling = min( W.Desire, willingness )
```

| Aspect | Detail |
|--------|--------|
| **Term owners** | Wife-owned dominate: `Desire`, `Loyalty`, `SeductionReceptivity`, `BoundaryFirmness` (4/6). Husband contribution via `Attentiveness`, `IntimacyAvailability` (marital deficit, secondary). |
| **Behavioral dims** | Yes — `SeductionReceptivity` and `BoundaryFirmness` are first-class (already drifted from stats in `StatToDimensionMappings`). |
| **Inputs available** | All yes: canonical stats + `RuntimeEncounterStats` (Husband dims) at prompt-build time. |
| **Strength** | One coherent score; Wife-owned; ceiling tied to same score (low-Desire+YES → low ceiling); simple to reason about and tune (3 terms + 2 weights). |
| **Weakness** | Ignores `Exhibitionism`, `PostEncounterGuilt`, `EmotionalEngagement`, OtherMan `PersistencePastLimits`, and Husband `Awareness`/`RiskTolerance`. Coefficient weights are free constants (need config). |
| **Retires** | `motivationScore` + `effectiveLoyalty` path; the 10 derived formulas collapse to `WifeWillingnessToCheat`/`WifeWillingnessVerdict`/`WifeExplicitnessCeiling`. |
| **Switch-to-B** | Replace the `willingness` computation with `100 − EscalationResistance`; keep verdict/ceiling mapping and all wiring. |
| **Switch-to-C** | Replace the `willingness` computation with Option C's drive/restraint blend; ceiling becomes `min(Desire, clamp(50 + drive − restraint, 0, 100))`. |

#### Option B — Reuse existing derived formula machinery

```
willingness = clamp( 100 − EscalationResistance, 0, 100 )
            // = Desire + 100 − Restraint − Loyalty   (from RolePlayDerivedFormulaEvaluator)
ceiling = min( W.Desire, 100 − BoundariesStrength )
        // BoundariesStrength = SelfRespect + Restraint + Loyalty/2
```

| Aspect | Detail |
|--------|--------|
| **Term owners** | Wife canonical stats only: `Desire`, `Restraint`, `Loyalty` (+ `SelfRespect` for ceiling). |
| **Behavioral dims** | None directly — uses raw stats. Wife behavioral dimensions not first-class. |
| **Inputs available** | All yes (canonical stats only — no dependency on `RuntimeEncounterStats`). |
| **Strength** | Minimal new code (`EscalationResistance`/`BoundariesStrength` already computed); stable, deterministic; simplest to test. |
| **Weakness** | Ignores behavioral dimensions the user explicitly wants; Husband neglect not factored (a Wife of a neglectful husband scores the same as one with a great husband if stats match); ceiling uses `BoundariesStrength` (Restraint/SelfRespect-heavy) which can under-ceil a low-Desire wife. |
| **Switch-to-A** | Replace score with Option A three-signal blend (needs `RuntimeEncounterStats` wiring for Husband dims + Wife behavioral dims). |
| **Switch-to-C** | Replace with drive/restraint blend (also needs behavioral dims). |

#### Option C — Full behavioral blend (richest)

```
drive      = (W.Desire * 0.4) + (W.SeductionReceptivity * 0.3) + ((100 − W.Loyalty) * 0.3)
restraint  = (W.BoundaryFirmness * 0.5) + (W.Restraint * 0.3) + (W.SelfRespect * 0.2)
husbandNeglect = ((100 − H.Attentiveness) + (100 − H.IntimacyAvailability)) * 0.5
persistence    = OM.PersistencePastLimits * 0.5

willingness = clamp( 50 + drive − restraint + husbandNeglect + persistence, 0, 100 )
ceiling = min( W.Desire, clamp(50 + drive − restraint, 0, 100) )
```

| Aspect | Detail |
|--------|--------|
| **Term owners** | Wife-owned (`Desire`, `SeductionReceptivity`, `Loyalty`, `BoundaryFirmness`, `Restraint`, `SelfRespect`) with Husband neglect + OtherMan persistence as explicit secondary drivers. |
| **Behavioral dims** | Yes — `SeductionReceptivity`, `BoundaryFirmness` first-class; also the only option to re-introduce OtherMan `PersistencePastLimits` and Husband neglect as distinct adders. |
| **Inputs available** | All yes (canonical stats + `RuntimeEncounterStats` for all three roles). |
| **Strength** | Most faithful to "why would she cheat" (neglect + pursuit + her own drive); still a single score; ceiling separate but coherent. |
| **Weakness** | Most terms (6 + 2 secondary) and 5 weight constants to tune; slightly more complex to reason about; more config surface. |
| **Switch-to-A** | Drop the `persistence` term, fold `drive − restraint` into the three-signal form. |
| **Switch-to-B** | Reduce to `100 − EscalationResistance` (drop behavioral dims + secondary drivers). |

---

#### Decision record

| Date | Decision | By |
|------|----------|----|
| 2026-08-10 | Adopt **Option A** (minimal three-signal) as the default; document B/C for future switching | User |

---

## 4. Files to Modify / Add

### New
- `specs/002-wife-willingness-to-cheat/spec.md` — canonical feature spec
- `specs/002-wife-willingness-to-cheat/research.md` — decisions (slot ownership, formula coefficient, UI parse strategy)
- `specs/002-wife-willingness-to-cheat/data-model.md` — schema, formulas, output contract
- `specs/002-wife-willingness-to-cheat/tasks.md` — task breakdown
- `specs/002-wife-willingness-to-cheat/contracts/willingness-prompt-contract.md` — exact prompt block + parse contract

### Modify (code)
| File | Change |
|------|--------|
| `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs` | Drop `MotivationScore`; add `WillingnessToCheat` (int, default 50) |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | add `WillingnessToCheat` column to `RolePlayV2AdaptiveStates` (migration/backfill 50) |
| `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs` | include `WillingnessToCheat` in INSERT/UPDATE/SELECT for adaptive state |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | copy `guidance.GuidanceText` → `PromptBuildContext`; persist `WillingnessToCheat` after continuation |
| `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs` | add `ScenarioGuidanceText` (or unify with existing `CharacterBehavioralFrames`/`CharacterStatStateTexts`) |
| `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/BehavioralFramesSlot.cs` | emit the unified "Wife Willingness to Cheat" HARD CONSTRAINT block — verdict (from `WillingnessVerdictBands`) + **ceiling (from `StatWillingnessProfiles` catalog resolved on `min(Desire, willingness)`)** |
| `DreamGenClone.Infrastructure/RolePlay/ScenarioGuidanceGenerator.cs` | `BuildWillingnessInterpretationAsync` resolves band from `min(Desire, willingness)`; `BuildResistanceInterpretationAsync` resolves verdict from `willingness` via `WillingnessVerdictBands`; both call the shared `ComputeWillingnessToCheat` helper |
| `DreamGenClone.Application/RolePlay/RolePlayDerivedFormulaEvaluator.cs` | add `ComputeWillingnessToCheat` shared helper (accepts all-character snapshots) + `WifeWillingnessToCheat`/`WifeWillingnessVerdict`/`WifeExplicitnessCeiling` |
| `DreamGenClone.Infrastructure/RolePlay/ScenarioEngineSettings*.cs` | add `WillingnessVerdictBandsJson` + `WillingnessCoefficients` config fields |
| `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` | optional: shared builder for the unified block; keep `AppendScenarioGuidance` in sync or mark legacy |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` | replace `GetEffLoyalty`/Motivation rows with `WillingnessToCheat`, Verdict, Ceiling rows |

### Modify (theme data — NTR migration)
- `ntr-open-world` fit rule + per-phase GuidanceText
- `wife-reawakening` fit rule + GuidanceText
- `infidelity-public-facade-exhibition` fit rule + GuidanceText
- (SQL scripts under `artifacts/tmp/dbquery/queries/`)

### Tests
- `DreamGenClone.Tests/RolePlay/` — `GetEffLoyalty` add-formula; band presence in built prompt (Slot 13); `MotivationScore` persistence; NTR fit-rule verdict thresholds; derived-formula additions.

---

## 5. Open Decisions (resolve in research.md before implementation)

1. **Slot ownership** — BehavioralFramesSlot (13) vs ScenarioGuidanceSlot (14) for the unified block. *Recommend 13.*
2. **Option A weights / saturation** — confirm the three coefficients (`0.5 / 0.5 / 0.25`) and the `min(Desire, willingness)` ceiling behavior via session tests. Keep them as **persisted config** (e.g. `WillingnessCoefficients`), not hardcoded. If tuning shows over- or under-reach, adjust the coefficients or move the verdict-band thresholds — no formula change needed.
3. **UI parse strategy** — reuse the `Details:` line for the adaptive panel vs parse the new unified block. *Recommend: keep a stable `Details:` line + parse it.*
4. **`WillingnessToCheat` persistence timing + `MotivationScore` retirement** — recompute on every continuation vs on phase change only; keep `MotivationScore` column (deprecated) or drop it. *Recommend: recompute every continuation, drop `MotivationScore` (or keep only for UI back-compat during transition).*
5. **NTR fit-rule posture** — verdict as hard gate (Stage A) vs soft fit signal (Stage B). *Recommend soft fit signal to avoid over-blocking open-world themes.*
6. **Default verdict mapping** — confirm `0-40 NO, 41-70 MAYBE, 71-100 YES` as the seeded `WillingnessVerdictBands`. Must be config, not hardcoded (repo rule).
7. **Behavioral dimension value source** — confirm Wife `SeductionReceptivity`/`BoundaryFirmness` are read from `RuntimeEncounterStats` (drifted) with static-profile fallback, matching `CharacterBehavioralFrameGenerator` behavior.

---

## 6. Phasing

- **Phase 1 — Correctness**: fix UI (render Option A score, not `Loyalty − Motivation`), persist `WillingnessToCheat`, wire `GuidanceText` → prompt (Slot 13 unified block). Produces truthful panel + bands in prompt.
- **Phase 2 — Canonicalization**: implement `ComputeWillingnessToCheat` (Option A), verdict-band config + seeding, derived formulas (`WifeWillingnessToCheat`/`WifeWillingnessVerdict`/`WifeExplicitnessCeiling`), coefficient config, coefficient tuning.
- **Phase 3 — NTR migration**: convert NTR themes to consume the flag; update fit rules + guidance prose.
- **Phase 4 — Validation**: build + slot tests + a fresh NTR session verifying the flag appears and drives behavior. Confirm the low-Desire/YES → low-ceiling case.

---

## 7. Success Criteria

- The unified block ("Verdict + Ceiling") appears in Wife-character prompts for all phases.
- Adaptive panel shows truthful `WillingnessToCheat = {score}` with verdict + ceiling bands, matching the engine.
- NTR/open themes reference the verdict flag; no raw Desire/Loyalty decision-gates in guidance prose.
- A low-Desire Wife with a high willingness score verdicts YES but ceilings low (e.g. "Light Petting") because `ceiling = min(Desire, willingness)`; a high-Desire Wife with the same score has a high ceiling — matching the user's "rubbing over clothes vs full intercourse" example.
- The three options (§3.8) remain documented so the algorithm can be switched (A→B/C) without re-deriving the analysis.
- No hardcoded RP thresholds introduced; coefficients, verdict bands, and band mappings are persisted config.
