# 045 - Beat production dialogue/narration cue order must be one global chronological sequence

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59` · Interaction `85da7c1b-cb10-413e-b40d-b445ea1e67b3`
- **Catalogue**: `c9e43c89-57e5-469a-8938-da257eeaa31f` (deepseek-v4-flash / DeepSeek)
- **Error**: `scene_beat_production_output_invalid` — "Beat Production dialogue and narration cues must have contiguous positive order."
- **Plans**: b4 `9e67458b` (Failed 19:26Z), b2 `57564e46` (Failed 19:29Z, same code)
- **Date**: 2026-09-08 · follows `044-moment-enrichment-glm-openrouter-reasoning-effort-400.md`

## Report

Beat b4 plan `9e67458b` attempt `f6bb716c` failed permanent validation. The parser
(`SceneBeatProductionParser`) requires the spoken cues' `order` values to equal one
contiguous `1..N`. The model (deepseek) returned narration `narr-1..narr-7` orders 1-7
and dialogue `dlg-1..dlg-2` orders 1-2 (restarting per array), so the concatenated order
was `[1..7,1,2]` -> fail.

## Analysis

- Parser built `dialogueInputs = response.Narration.Concat(response.Dialogue)` and ran
  `ValidateUniqueOrdered(...)` which requires that LIST's orders == Range(1,count). That
  structurally forces ALL narration before ALL dialogue and forbids any chronological
  interleaving (even a correctly global-numbered interleaved response fails).
- Content of the failed attempt proved the intended spoken track INTERLEAVES: dlg-1
  ("Look at me...") belongs to event e3, chronologically between narration at e2 and e4;
  dlg-2 at e7. The dialogue lines are woven between narrated action.
- Neither the spoken prompt nor the schema told the model that `order` is ONE sequence
  across BOTH arrays; schema only requires PositiveInteger. Earlier completed plans had
  narration only (no dialogue), so the cross-array rule was never exercised until a beat
  contained both.

## Resolution (Option B — single global chronological order, interleaving allowed)

1. **`SceneBeatProductionParser.cs`** — sort the combined list by order before validating:
   `response.Narration.Concat(response.Dialogue).OrderBy(input => input.Order).ToList()`.
   The contiguous check now validates the spoken-track sequence (dialogue may sit between
   narration). Downstream (span resolution) already sorted by order.
2. **`SceneBeatProductionContract.cs`** — `ContractVersion` `scene-beat-production-v3` ->
   `v4`. `SpokenSystemPrompt` (and monolithic `SystemPrompt`) now instruct: order is one
   global chronological sequence across the narration and dialogue arrays together; never
   restart numbering per array or duplicate/skip an order.
3. **`SceneBeatProductionParserTests`** — new
   `Parse_AllowsDialogueAndNarrationToShareOneChronologicalOrder` (narration order 2 listed
   before a dialogue order 1; old parser would reject, new passes).

## Validated

- Web build: 0 errors.
- Beat production parser + contract tests: 26 passed / 0 failed (incl. new regression).
- RolePlay + Processing sweep (excluding 4 pre-existing failing classes): 1481 passed / 0 failed.
- Webapp rebuilt + running. Re-running the failed b4/b2 beats (now v4) should produce
  interleaved narration/dialogue with a single global order and pass validation.
- Live re-run of b4/b2 pending.
