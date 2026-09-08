# 048 - Beat production assembly pass omits required sections under load (3rd distinct strict-gate failure)

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59`
- **Catalogue**: `c9e43c89-57e5-469a-8938-da257eeaa31f`
- **Plan**: b1 v5 `9dbc8625` (scene-beat-production-v4, deepseek-v4-flash / DeepSeek)
- **Error**: `scene_beat_production_assembly_incomplete` — "Beat Production 'assembly' pass omitted
  required section 'startContinuity'."
- **Date**: 2026-09-08 · follows `045` (ordering), `046` (glm reasoning/upstream), `047` (verbatim source text)

## Report

b1 v5 deepseek run completed all 4 passes (structure 164 s/3141 ch, spoken 182 s/5234 ch,
soundscape 113 s/5111 ch parallel, assembly 205 s/**19339 ch**) = ~9.5 min total, then FAILED at
final parse validation because the assembly output omitted the required `startContinuity` section.
Every pass returned content (all `status=Complete` in passTrace); the model dropped a top-level
assembly key while emitting a very large single assembly object.

## Pattern (why this matters)

This is now the THIRD distinct strict-validation terminal failure, all on well-formed-but-huge
single-authoring runs, each ~10–12 min of actual active work after any dead/downtime gaps:

### Timing note (accuracy correction)
- b2 v4 wall-clock spanned 20:10:51 → 20:34:55 (~24 min), but attempt `DurationMs` = 703,422 ms
  (~11.7 min) and passTrace shows structure did not begin until 20:23:12 — ~12 min after the
  attempt was claimed (worker downtime / app restart in that window; no provider work occurred).
  Sum of sequential pass spans (structure + soundscape-parallel + assembly) = 703 s = the recorded
  DurationMs. So actual model+processing time was ~11.7 min, not 24.
- b1 v5 wall = active (no gap): structure 164 s + spoken 182 s (parallel soundscape 113 s) +
  assembly 205 s = 551 s ≈ 9.2 min, matching the 21:00:50 → 21:10:02 window.

| Run | Model | Actual active work | Rule |
|---|---|---|---|
| b2 v3 `57564e46` | deepseek | ~10 min | ordering contiguity (045, fixed v4) |
| b2 v4 `26aa765e` | deepseek | **~11.7 min** (703 s; earlier "~24 min" figure was wall-clock inflated by a ~12-min worker-downtime gap between attempt claim 20:10:51 and first pass 20:23:12 — see 048 timing note) | cue source text not verbatim in evidence (047, open) |
| b1 v5 `9dbc8625` | deepseek | ~9.2 min | assembly omitted `startContinuity` (this record, open) |

Root cause across all three is architectural, not model choice: one model authors a large
multi-section canonical JSON in one all-or-nothing gate, so ANY section omission / ordering /
verbatim mismatch fails the whole run after many minutes. The assembly pass alone is a ~19k-char
single call with 4 required big sections (startContinuity, endContinuity, typedReferences,
videoCoverage) — models drop a key under that load.

## Resolution

None yet — open. Supports the design direction the user raised: replace the monolithic
"author-everything" beat production with (a) a small beat CORE derived at catalogue time on the
RP side (events/keys/continuity/identity seed), and (b) small INDEPENDENT per-section/per-modality
derivations (moments/image, sound, video) each with its own small prompt, own validation, and own
retry — so a dropped section fails/costs seconds, not a full multi-pass run.

## Validated

- [x] Confirmed failure code + message on stored v4 attempt (`9dbc8625`).
- [ ] Root cause + fix not yet done (design decision pending).
