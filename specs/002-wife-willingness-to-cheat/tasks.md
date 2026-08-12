# Tasks: Unified "Wife Willingness to Cheat"

**Input**: `specs/002-wife-willingness-to-cheat/plan.md`, `spec.md`, `research.md`, `data-model.md`
**Prereqs**: build clean baseline; webapp stopped before builds that lock Web/bin (see memory).

## Phase 1 — Correctness (bands reach prompt, panel truthful)

- [ ] T001 Persist `WillingnessToCheat` on `RolePlayV2AdaptiveStates` — add `WillingnessToCheat INTEGER NOT NULL DEFAULT 50` column + backfill in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`; include in save/load mapping in `RolePlayStateRepository.cs`. Remove `MotivationScore` field from `AdaptiveScenarioState` (not persisted, never set — no migration needed).
- [ ] T002 Set `WillingnessToCheat` on every continuation state-save — recompute via the Option A shared helper (`ComputeWillingnessToCheat`) and store on `AdaptiveScenarioState` in `RolePlayEngineService`/`RolePlayContinuationService`.
- [ ] T003 Add `ScenarioGuidanceText` (merged `GuidanceText`) to `PromptBuildContext.cs`; populate it in `RolePlayContinuationService.BuildPromptViaBuilderAsync` from `guidance.GuidanceText` (currently dropped).
- [ ] T004 **Inject BOTH band catalogs into the unified block** in `BehavioralFramesSlot.cs` (Slot 13) for the Wife character (non-Narrative):
  - **Verdict** — resolve `WillingnessVerdictBands` (YES/MAYBE/NO config) from the `willingness` score; emit the YES/MAYBE/NO label + the band's directive text.
  - **Ceiling (Wife Willingness Profile catalog)** — resolve `StatWillingnessProfiles` (the 20-band catalog: Purely Emotional → Group Exploration) from `min(Desire, willingness)`; emit the `ExplicitnessLevel` + `PromptGuideline` + `ExampleScenarios`. This is the explicit injection of the Wife Willingness Profile catalog into the prompt.
  - Emit the unified HARD CONSTRAINT block format per `contracts/willingness-prompt-contract.md`.
- [ ] T005 Fix `RolePlayWorkspace.razor` adaptive panel — replace `GetEffLoyalty()` / `Motivation Score` / `Eff. Loyalty` rows with the Option A score (`WillingnessToCheat`, verdict, ceiling). Render `WillingnessToCheat = {score}`, Verdict row, Ceiling row (both band labels + texts).
- [ ] T006 Tests: band text present in built prompt (Slot 13) — assert **both** "Verdict:" **and** "Ceiling:" lines + the `ExplicitnessLevel` from the Willingness Profile and the YES/MAYBE/NO from `WillingnessVerdictBands`; `WillingnessToCheat` persistence round-trip.

## Phase 2 — Canonicalization (Option A score, configs, derived formulas)

- [ ] T007 Implement `ComputeWillingnessToCheat` shared helper (Option A formula — Desire/Loyalty/SeductionReceptivity/BoundaryFirmness/Attentiveness/IntimacyAvailability) in `DreamGenClone.Application/RolePlay/RolePlayDerivedFormulaEvaluator.cs` (or a new dedicated helper). **Signature accepts `IReadOnlyDictionary<string, CharacterStatProfileV2>` (all snapshots)** so the helper can read Husband/Wife/OtherMan — resolves readiness Gap #1. Wire `ScenarioGuidanceGenerator` and `CharacterStateScenarioMapper`/`DecisionPointService` to call it.
- [ ] T008 Modify `ScenarioGuidanceGenerator.BuildWillingnessInterpretationAsync` — resolve the Willingness Profile band from `min(Desire, willingness)` instead of raw `ResolveAverageForStat`; modify `BuildResistanceInterpretationAsync` — resolve verdict from `willingness` via `WillingnessVerdictBands` instead of `effectiveLoyalty` via `StatResistanceProfiles`. Both append to `GuidanceText` (already wired by T003 → Slot 13).
- [ ] T009 Add `WillingnessVerdictBands` config (YES/MAYBE/NO mapping) + `WillingnessCoefficients` config (`DesireLoyaltyWeight=0.5`, `BehaviorWeight=0.5`, `MaritalDeficitWeight=0.25`) to `ScenarioEngineSettings` (existing config home — resolves readiness Gap #2). UI-backed/seeded, not hardcoded.
- [ ] T010 Add `WifeWillingnessToCheat` / `WifeWillingnessVerdict` / `WifeExplicitnessCeiling` to `RolePlayDerivedFormulaEvaluator.cs` (call the shared helper); retire the 10 overlapping derived formulas or leave them dormant. These feed `FormulaThresholds` in theme fit rules (Stage B) and `DecisionPointService`.
- [ ] T011 Leave `ScenarioEligibilityService.ResolveWillingnessTier` (Stage A) as-is (coarse eligibility by avg Desire) — verdict feeds via Stage B formula thresholds (research RQ-7 — soft fit signal). Add a task note to this effect.
- [ ] T012 Tests: `ComputeWillingnessToCheat` cross-character helper (missing Husband/OtherMan → 50 fallback), verdict mapping, coefficient effect, `ScenarioGuidanceGenerator` Option A score in `GuidanceText`.

## Phase 3 — NTR migration

- [x] T013 Update `ntr-open-world` fit rule (`ow-fit-wife`) + GuidanceText to consume `WifeWillingnessVerdict` / ceiling instead of raw Desire/Loyalty decision gates (keep coarse fit signal). — GuidanceText updated via SQL (see T016); fit rule `ow-fit-wife` uses `WifeWillingnessVerdict` formula threshold (T010 added the formula; fit rules resolve via derived formulas).
- [x] T014 Update `wife-reawakening` fit rule (`wr-fit-wife`) + GuidanceText similarly.
- [x] T015 Update `infidelity-public-facade-exhibition` fit rule (`pfex-fit-wife`) + GuidanceText — keep exhibitionism/structural clauses, switch decision reads to the verdict.
- [x] T016 SQL scripts under `artifacts/tmp/dbquery/queries/` for the theme data updates. — `b034_ntr_migration.sql` applied (7 UPDATEs, all verified landed: ntr-open-world BuildUp/Approaching/Committed/Climax, wife-reawakening BuildUp, infidelity-public-facade-exhibition Committed).

## Phase 4 — Validation

- [x] T017 Build web + tests clean; run Slot contract tests. — Test project builds clean (0 errors) after adding the missing `ISqlitePersistence.GetLatestSteeringGenerationRecordAsync` stub to 3 in-memory test fakes (pre-existing B-075 compile gap, documented in `debug/022-pacing-other-actors.md`). New B-034 tests pass: `WifeWillingnessCalculatorTests` 21/21, `ScenarioGuidanceGeneratorTests`+round-trip 9/9, `BehavioralFramesSlot` willingness-block 2/2. 7 pre-existing `SlotContractTests` failures remain (unrelated; see `/memories/repo/pre-existing-test-failures.md`).
- [ ] T018 Fresh NTR session: verify the unified block appears (Verdict + Ceiling, both band catalogs), panel numbers match, low-Desire+YES verdict produces low-ceiling behavior (over-clothes vs intercourse).

## Bugs found & fixed during Phase 4
- **INSERT column/values mismatch** (`RolePlayStateRepository`): Phase 1 added `$willingnessToCheat` to VALUES + ON CONFLICT + param binding but omitted `WillingnessToCheat` from the INSERT column list → "42 values for 41 columns" SqliteException on every adaptive save. Fixed by adding the column. This would have broken all session saves.
- **Snapshot overload with no Wife** (`WifeWillingnessCalculator`): returned a non-50 score (added Husband's marital deficit with no Wife present). Fixed: no Wife → return neutral 50.
- Test math: marital-deficit term at att=intim=50 is 25 (not 0); all-50 baseline = 75; desire=80/loyalty=20 with neutral behavior clamps to 100.
- **Live-verify bug 1 — block not rendering** (`BehavioralFramesSlot`): the generator appends the willingness lines space-separated mid-line; the slot filtered by line-start → block dropped. Fixed via marker-based `ExtractBandLines`.
- **Live-verify bug 2 — inverted resistance band** (user-confirmed): the resistance line fed the willingness score into the Loyalty-keyed `StatResistanceProfiles` catalog (willingness 11 → "Weak Resistance — yields quickly" while verdict=NO + loyalty=95). **Decision (user, option 1) + plan decision #3:** `StatResistanceProfiles` is retired for the verdict. `ScenarioGuidanceGenerator` now emits the contract format (`Verdict:`, `Ceiling:`, `Details:` lines); verdict directives are UI-backed config (`WillingnessVerdictNo/Maybe/YesDirective` on `ScenarioEngineSettings`, editable in ThemeProfiles). Slot extracts `Verdict:`/`Ceiling:`/`Details:` markers. T008/T009 updated accordingly; `IStatResistanceProfileService` dependency removed from `ScenarioGuidanceGenerator`.

## Open Decisions to confirm before implementation
1. Slot ownership (plan §5.1) — recommended `BehavioralFramesSlot` (13).
2. Coefficient values (`0.5 / 0.5 / 0.25`) + `min(Desire, willingness)` ceiling — confirm via session tests.
3. `StatResistanceProfiles` disposition — retired for verdict (replaced by `WillingnessVerdictBands`); keep the table dormant or repurpose. `StatWillingnessProfiles` **stays active** as the ceiling catalog (T004 injects it).
4. `MotivationScore` retirement — drop the field (no DB column exists, no migration). Replaced by `WillingnessToCheat`.
5. Config home for `WillingnessVerdictBands` + `WillingnessCoefficients` — `ScenarioEngineSettings` (existing pattern).
6. Shared helper signature — accepts `IReadOnlyDictionary<string, CharacterStatProfileV2>` (all snapshots) so it can read Husband/Wife/OtherMan.
7. Stage A gate — leave `ResolveWillingnessTier` as-is (coarse); verdict feeds via Stage B formula thresholds.
