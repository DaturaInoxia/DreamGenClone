# B-034: Unified "Wife Willingness to Cheat" Algorithm

**State**: `planned`
**Priority**: high
**Scope**: large
**Backlog**: B-034 (Wife Willingness — refactor and implement); supersedes B-009 (resistance & motivation gap); relates to B-030 (woman willingness profile), B-054 (stat profile directive injector), B-073/S-020 (resistance band ignores narrative state)
**Spec folder**: `specs/002-wife-willingness-to-cheat/`
**Date**: 2026-08-10

---

## TL;DR

Replace the current over-complex, two-system "Wife Willingness to Cheat" implementation (motivation→resistance on effective Loyalty, plus a separate Desire→willingness ceiling — neither of which reaches the prompt) with **one unified, prompt-injected `willingness` score** that answers two questions per Wife:

1. **Will she cheat?** → verdict `YES / MAYBE / NO` from the `willingness` score.
2. **How far will she go?** → explicitness ceiling `= min(Desire, willingness)`.

The verdict becomes the **flag** that NTR / open-world themes consume instead of reading raw `Desire`/`Loyalty`/`Restraint`/`SelfRespect` directly from fit rules and guidance prose.

**Selected formula (Option A):**
```
willingness = clamp( 50
              + (Desire − Loyalty) * 0.5
              + (SeductionReceptivity − BoundaryFirmness) * 0.5
              + ((100 − Attentiveness) + (100 − IntimacyAvailability)) * 0.25, 0, 100 )
verdict = WillingnessVerdictBands.Resolve(willingness)   // config: 0-40 NO, 41-70 MAYBE, 71-100 YES
ceiling = min(Desire, willingness)
```
Options B/C are fully documented for future switching — see spec folder plan §3.8.

---

## Why (verified defects)

| # | Defect | Evidence |
|---|--------|----------|
| 1 | UI computes effective Loyalty backwards | `RolePlayWorkspace.razor` `GetEffLoyalty()` = `Loyalty − Motivation` (subtract); server/spec add. Panel shows "12" when truth is 67. |
| 2 | `MotivationScore` never set / not persisted | Declared on `AdaptiveScenarioState` but never assigned; no DB column. UI always reads 0. |
| 3 | Bands never reach the prompt | `BuildPromptViaBuilderAsync` drops `guidance.GuidanceText` (carries the band lines); `BehavioralFramesSlot` doesn't emit them. 0 stored prompts contain "Resistance band"/"Willingness band". |
| 4 | NTR themes read raw stats directly | `ntr-open-world`, `wife-reawakening`, `infidelity-public-facade-exhibition` fit rules + guidance reference bare `Desire≥x / Loyalty≤y`. |
| 5 | Selection gate is raw Desire only | `ScenarioEligibilityService.ResolveWillingnessTier` = avg Desire. |
| 6 | Formula saturation | `min(Loyalty + motivation, 100)` pegs to 100 too easily. |
| 7 | Two divergent spec sources | `001-wife-resistance-motivation` vs `v2/Stat-Based Willingness System`. |

---

## Design (Option A selected)

- **One score** drives both verdict and ceiling (Wife-owned terms dominate; Husband neglect is secondary).
- **Verdict** = flag for NTR/open themes (YES/MAYBE/NO from `WillingnessVerdictBands`); **ceiling** = what she'll do with the other man, resolved from the **`StatWillingnessProfiles` catalog** (20 bands: Purely Emotional → Group Exploration) on `min(Desire, willingness)` (low-Desire wife with YES verdict cheats but stays low-explicitness: over-clothes vs intercourse).
- **Prompt block** (Slot 13, BehavioralFramesSlot, Wife only) — **injects BOTH band catalogs**:
  ```
  HARD CONSTRAINT — Wife Willingness to Cheat (authoritative, overrides theme guidance):
    Verdict: {Verdict} — {verdictBand.PromptDirective}          ← from WillingnessVerdictBands on willingness
    Ceiling: {ExplicitnessLevel} — {willingness.PromptGuideline} ← from StatWillingnessProfiles catalog on min(Desire, willingness)
    Details: Willingness to Cheat = {willingness} (...); Ceiling = min(Desire, willingness) = {ceiling}.
  ```
- **Persistence**: `WillingnessToCheat` column on `RolePlayV2AdaptiveStates` (default 50); `WillingnessVerdictBands` + `WillingnessCoefficients` config in `ScenarioEngineSettings` (UI-backed, not hardcoded).
- **Derived formulas**: `WifeWillingnessToCheat` / `WifeWillingnessVerdict` / `WifeExplicitnessCeiling` via shared `ComputeWillingnessToCheat` helper (accepts all-character snapshots; single source of truth).
- **NTR migration**: convert the three NTR themes to consume the verdict flag.

---

## Phasing

- **Phase 1 — Correctness**: fix UI (render Option A score), persist `WillingnessToCheat`, wire `GuidanceText` → prompt (Slot 13 block). Truthful panel + bands in prompt.
- **Phase 2 — Canonicalization**: `ComputeWillingnessToCheat` (Option A), verdict-band config + seeding, derived formulas, coefficients, tuning.
- **Phase 3 — NTR migration**: NTR themes consume the flag; update fit rules + guidance prose.
- **Phase 4 — Validation**: build + slot tests + fresh NTR session; confirm low-Desire/YES → low-ceiling.

---

## Option Analysis (for future switching)

Full detail (formulas, term owners, strengths/weaknesses, switch-to steps) in `specs/002-wife-willingness-to-cheat/plan.md` §3.8.

| Option | Core formula | Behavioral dims | Notes |
|--------|-------------|-----------------|-------|
| **A (SELECTED)** | `clamp(50 + (Desire−Loyalty)*0.5 + (SedRecept−BoundFirm)*0.5 + (neglect)*0.25)` | Yes | Wife-owned; ceiling = `min(Desire, willingness)` |
| B | `100 − EscalationResistance` (= `Desire + 100 − Restraint − Loyalty`) | No | Reuses existing evaluator; simplest |
| C | `clamp(50 + drive − restraint + neglect + persistence)` | Yes | Richest; most terms/weights |

---

## Success Criteria

- Unified "Verdict + Ceiling" block appears in Wife-character prompts for all phases.
- Panel shows truthful `WillingnessToCheat = {score}` matching the engine.
- NTR/open themes use the verdict flag; no raw Desire/Loyalty decision-gates in guidance prose.
- Low-Desire + YES verdict → low ceiling (over-clothes vs intercourse).
- Options B/C remain documented so the algorithm can be switched without re-deriving.
- No hardcoded RP thresholds; all config is persisted/UI-backed.

---

## Artifacts (spec folder `specs/002-wife-willingness-to-cheat/`)

- `plan.md` — full plan + Option Analysis (§3.8)
- `spec.md` — feature spec (US1-3)
- `research.md` — decisions (RQ-1..9)
- `data-model.md` — formulas, persistence, prompt contract
- `tasks.md` — phased tasks (T001-T016)
- `contracts/willingness-prompt-contract.md` — exact prompt block
- `current-formula-analysis.md` — audit of what the current formula does
