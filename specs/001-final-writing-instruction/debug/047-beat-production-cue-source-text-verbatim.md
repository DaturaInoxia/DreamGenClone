# 047 - Beat production cue source text must be verbatim in evidence (new v4 failure mode)

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59`
- **Catalogue**: `c9e43c89-57e5-469a-8938-da257eeaa31f`
- **Plan**: b2 v2 `26aa765e` (scene-beat-production-v4, deepseek-v4-flash / DeepSeek)
- **Error**: `scene_beat_production_output_invalid` — "Beat Production cue source text was not
  found verbatim in evidence 'c1'."
- **Date**: 2026-09-08 · follows `045` (ordering rule) and `046` (glm reasoning/upstream)

## Report

The first post-045 **v4** deepseek re-run (b2 `26aa765e`, started 20:10:51Z, completed
20:34:55Z) failed strict validation with a NEW message distinct from 045's ordering rule:
a cue's `exactSourceText` was not found verbatim inside its source evidence `c1`.

Per-pass progress was otherwise healthy (structure 156 s / 7510 chars, spoken, ..., assembly
241 980 ms) — the full 4-pass run completed in ~24 min on deepseek and only then failed the
verbatim-source-text check during parse validation.

## Analysis / open questions

- This is a **different validation rule** than 045 (which fixed narration/dialogue order
  contiguity). The v4 contract apparently also requires every cue `exactSourceText` to appear
  verbatim in the quoted evidence (`c1`). The model (deepseek) paraphrased/edited the source
  string instead of copying it exactly, so validation rejected the whole plan.
- Likely directions (not yet root-caused): (a) the schema/prompt does not insist hard enough on
  verbatim copying (needs a contract/schema change like 045); (b) the parser check is overly
  strict about a substring that legitimately differs (case/whitespace/punctuation normalization);
  (c) the model simply cannot reproduce long verbatim spans reliably at this output scale.
- Impact: this can fail ANY model (deepseek here; glm runs have separate upstream failures) —
  so beat production may keep failing regardless of the model until the verbatim rule is
  understood/fixed.

## Resolution

None yet — open. Next step when beat production is being worked again: locate the verbatim
check in `SceneBeatProductionParser` (the `'c1'` evidence), determine whether the stored
`exactSourceText` differs from the evidence by normalization only, and decide between a
prompt/contract strengthening (v5) or a parser tolerance fix.

## Validated

- [x] Confirmed the new failure code/message on the stored v4 attempt (`26aa765e`).
- [ ] Root cause + fix not yet done.
