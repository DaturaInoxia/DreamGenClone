# Prompt Contract: Unified "Wife Willingness to Cheat"

**Feature**: `002-wife-willingness-to-cheat` | **Date**: 2026-08-10

## Block (BehavioralFramesSlot, Slot 13 — Wife character, non-Narrative)

```
HARD CONSTRAINT — Wife Willingness to Cheat (authoritative, overrides theme guidance):
  Verdict: {Verdict} — {verdictBand.PromptDirective}
  Ceiling: {ExplicitnessLevel} — {willingnessBand.PromptGuideline} (Examples: {ExampleScenarios})
  Ladder: {band1.ExplicitnessLevel}, {band2.ExplicitnessLevel}, …, {willingnessBand.ExplicitnessLevel}
  Details: Willingness to Cheat = {willingness} (Desire={Desire}, Loyalty={Loyalty}, SeductionReceptivity={SR}, BoundaryFirmness={BF}, Attentiveness={Att}, IntimacyAvailability={Intim}); Ceiling = min(Desire, willingness) = {ceiling}.
```

## Field Sources

| Field | Source |
|-------|--------|
| `willingness` | `ComputeWillingnessToCheat` (Option A shared helper; see plan §3.1) — reads Wife Desire/Loyalty + Wife behavioral dims (`SeductionReceptivity`, `BoundaryFirmness` via `RuntimeEncounterStats`) + Husband `Attentiveness`/`IntimacyAvailability`; defaults 50 |
| `Verdict` | `WillingnessVerdictBands` config (0-40 NO, 41-70 MAYBE, 71-100 YES) resolved from `willingness` |
| `verdictBand.PromptDirective` | The `PromptDirective` of the resolved verdict band |
| `ExplicitnessLevel` + `PromptGuideline` + `ExampleScenarios` | **`StatWillingnessProfiles` catalog** (the Wife Willingness Profile — 20 bands: Purely Emotional → Group & Gangbang) resolved from **`min(Desire, willingness)`** — NOT raw Desire |
| `Ladder` | All bands from the bottom up to (and including) the resolved ceiling band, joined by `ExplicitnessLevel` — so the model knows she is capable of escalating through every level up to the cap (may move up within a scene, never past the ceiling) |
| `ceiling` | `min(Desire, willingness)` — the numeric ceiling used to resolve the Willingness Profile band |

## Behavior Notes (for model)

- Verdict (from `willingness`) and Ceiling (from `min(Desire, willingness)`) are coupled via the single `willingness` score but answer different questions.
- Verdict decides *whether* the Wife crosses (`YES/MAYBE/NO`).
- Ceiling decides *how far* if she does — resolved from the **Wife Willingness Profile catalog** (`StatWillingnessProfiles`, 20 bands) on `min(Desire, willingness)`.
- A Wife with Verdict=YES but low Ceiling may cheat yet remain limited to over-clothes / lower-explicitness acts (e.g. "Genital Over Clothes") until Desire rises.
- A Wife does NOT need High Desire to cheat — Desire caps the ceiling (what she'll do), not the verdict.
- This block is authoritative and outranks theme guidance prose on the Wife's willingness.

## Parse / Diagnostics

The `Details:` line is stable for the adaptive panel and debug tooling:
```
Willingness to Cheat = {willingness} (Desire={Desire}, Loyalty={Loyalty}, SeductionReceptivity={SR}, BoundaryFirmness={BF}, Attentiveness={Att}, IntimacyAvailability={Intim}); Ceiling = min(Desire, willingness) = {ceiling}.
```
