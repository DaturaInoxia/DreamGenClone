# Research: Unified "Wife Willingness to Cheat"

**Feature**: `002-wife-willingness-to-cheat` | **Date**: 2026-08-10

## RQ-1: Why doesn't the resistance/willingness band reach the prompt today?

Traced `RolePlayContinuationService.BuildPromptViaBuilderAsync`:
- `_scenarioGuidanceContextFactory.CreateAsync(guidanceInput)` → `ScenarioGuidanceContext` holds `GuidanceText` (which contains the appended "Resistance band ..." / "Willingness band ..." lines), plus `CharacterBehavioralFrames` and `CharacterStatStateTexts`.
- Only the **latter two** are copied into `PromptBuildContext` (`CharacterBehavioralFrames`, `CharacterStatStateTexts`).
- The **merged `GuidanceText` is dropped** — never placed on `PromptBuildContext`, so no slot renders it.
- Legacy `RolePlayAssistantPrompts.AppendScenarioGuidance` emits `- Guidance: {guidance.GuidanceText}` but is **not called** in the live 17-slot path (only tests).
- Verified: stored prompts in session 7763f8a8 contain 0 occurrences of "Resistance band"/"Willingness band".
- Debug 008 fixed frames + stat-state texts wiring but **did not** wire the band text.

**Decision**: Add a `ScenarioGuidanceText` (merged `GuidanceText`) field to `PromptBuildContext` and render the unified block from it in `BehavioralFramesSlot`.

## RQ-2: Which slot owns the unified block?

Options:
- **BehavioralFramesSlot (Slot 13)** — per-character, already carries Wife behavioral frame + stat-state texts; debug-008 explicitly intended Wife resistance to live here. Wife-only filtering natural.
- **ScenarioGuidanceSlot (Slot 14)** — phase steering; more global, less per-character.

**Decision**: `BehavioralFramesSlot` (Slot 13). It is per-character and already the home of Wife behavioral/stat state guidance. The unified block renders only when the Wife character is present (non-Narrative variant).

## RQ-3: Formula direction and the UI inversion

- Server: `effectiveStat = min(Loyalty + motivation, 100)` (add).
- Spec/data-model: same (add).
- UI `GetEffLoyalty`: `Math.Max(Loyalty - GetMotScore(), 0)` (**subtract**) → inverted and also uses unpersisted `MotivationScore` (always 0).

**Decision**: Fix UI to render the **Option A `willingness` score** (not the old `Loyalty − Motivation`). One formula everywhere. `MotivationScore` retired.

## RQ-4: Formula saturation (spec-flagged)

`min(Loyalty + motivation, 100)` with neutral motivation (50/50/50/50 → score 50) makes Loyalty 50 → effective 100. Quickstart proposed a coefficient.

**Decision (tuning)**: **Option A** removes the old saturation problem structurally — the score is centered at 50 with signed `(Desire − Loyalty)` and `(SeductionReceptivity − BoundaryFirmness)` terms, so neutral-equal values stay near 50 and only real divergence moves the verdict. Coefficients (`WillingnessCoefficients`) are persisted config; tune via session tests if the distribution skews.

## RQ-5: YES / MAYBE / NO mapping

**Decision**: A persisted `WillingnessVerdictBands` config mapping the `willingness` score (0–100) to `YES`/`MAYBE`/`NO`. Default suggestion: 0–40 NO, 41–70 MAYBE, 71–100 YES — MUST be config, not hardcoded (repo no-hardcoded-thresholds rule).

## RQ-6: Persistence timing for the willingness score

**Decision**: Recompute + persist `WillingnessToCheat` on `RolePlayV2AdaptiveStates` during each continuation's state-save (alongside `CharacterSnapshots`), so the panel is always current and history is inspectable.

## RQ-7: NTR fit-rule posture — hard gate vs soft signal

**Decision**: Keep the willingness flag as a **soft fit signal** (Stage B formula threshold `WifeWillingnessVerdict`) rather than a Stage A hard gate, so open-world themes aren't over-blocked. Stage A stays a coarse eligibility tier but is upgraded to consider the verdict (see plan §5 item 5).

## RQ-8: Theme stat read migration

`RolePlayDerivedFormulaEvaluator` gains (Option A, shared helper with `ScenarioGuidanceGenerator`):
```csharp
["WifeWillingnessToCheat"]  = profile => ComputeOptionAWillingness(profile),
["WifeWillingnessVerdict"]  = profile => VerdictScore(profile),
["WifeExplicitnessCeiling"] = profile => Math.Min(profile.Desire, ComputeOptionAWillingness(profile)),
```
`ComputeOptionAWillingness` resolves from the same formula as the guidance generator (shared helper) so themes, gates, and the prompt all agree.

## RQ-9: Option selection (2026-08-10)

**Decision**: **Option A** selected (single three-signal `willingness` score). Options B/C documented in `plan.md` §3.8 with full formulas, term owners, code touch points, and switch-back steps so the algorithm can be swapped later without re-deriving the analysis.

## References
- `specs/001-wife-resistance-motivation/research.md`, `data-model.md`, `quickstart.md`
- `specs/001-rp-prompt-redesign/debug/008-stat-state-texts-missing.md`
- `specs/Planning/WifeResistance.md`
- `specs/002-wife-willingness-to-cheat/plan.md` §3.8 (option analysis)
