# Beat Production — Deferred Stages: Detailed Implementation Plan

- **Status**: plan (not implemented)
- **Date**: 2026-09-08
- **Parent design**: `beat-production-deterministic-assembly-design.md` (Stage 1 shipped — debug 049)
- **Goal**: cut beat production from ~9–12 min active toward ~2–4 min, and remove the remaining
  fragility (verbatim re-emission, eager soundscape).

## ROI ordering

| # | Stage | Est. time saved | Est. tokens saved | Risk |
|---|---|---|---|---|
| 2 | Spoken anchor compaction | **largest** (~4 min, biggest pass) | largest (15–19k → ~1–2k chars) | medium (span extension) |
| 3 | Lazy soundscape | ~1.5–2 min (one parallel branch) | ~5k chars | low |
| 4 | Structure `stateChanges` + deterministic continuity states | ~small (continuity pass already small) | ~1–2k chars | high (semantics) |
| 5 | Continuity chaining across beats | ~small (correctness, not speed) | ~0 | medium |

Stages 2 and 3 deliver most of the target; 4 and 5 are mostly correctness/robustness.

---

## Stage 2 — Spoken anchor compaction (do first)

### Why it's the big win
The `spoken` pass re-emits `exactSourceText`, `displayText`, and `normalizedSpokenText` — full verbatim
prose — per cue, plus normalization metadata. This is the single largest output (measured 15–19k chars)
and the direct source of the 047 verbatim-drift failure. The app already searches evidence for the
verbatim text (`SceneBeatProductionParser.ResolveExactSpanBySearch`) and stores the full span + offsets
on the parsed cue; downstream only reads the parsed cue. So the LLM never needed to re-emit the full text.

### Change
1. **`SceneBeatProductionContract.DialogueCue()` schema (spoken pass)** — replace `exactSourceText`,
   `displayText`, `normalizedSpokenText`, `normalizationMethod`, `normalizationVersion` with a single
   short **`sourceAnchor`** (a distinctive verbatim phrase ~5–15 words from the evidence). Keep
   `sourceKey`, `speakerKey`, `addresseeKeys`, `eventKey`, `performance`, `window`, `lipSyncRelevant`,
   `reviewStatus`/`reviewReason`.
2. **`SpokenSystemPrompt`** — instruct: emit a short unique verbatim anchor (never the whole line),
   never paraphrase; keep performance intent (emotion/intensity/pace) — that part is genuinely creative
   and stays on the model.
3. **New deterministic span resolver** (extends `SceneBeatProductionSourceResolver`):
   - locate the anchor via the existing verbatim search (cursor-ordered per evidence key),
   - **extend** the span backward/forward to sentence/line/quote boundaries in the evidence text,
   - derive `exactSourceText` (extended span), `displayText` (same), `normalizedSpokenText` (same, or a
     fixed normalization), `normalizationMethod = "verbatim-anchor"`, `normalizationVersion = "1"`.
4. **`SceneBeatProductionParser`** — map `sourceAnchor` → resolved span (the above resolver) instead of
   requiring full `exactSourceText`; keep `ValidateSpokenNormalization` trivially satisfied (span ==
   normalized when normalization is identity).

### Contract version
`scene-beat-production-v5` → `v6` (spoken pass schema changed).

### Risks / decisions
- **Anchor ambiguity**: multiple occurrences of a short phrase. Mitigation: cursor-ordered search +
  require the anchor to be unique within the evidence item; if ambiguous → `ReviewRequired` with reason.
- **Extension boundaries**: sentence vs paragraph vs quote. Decision needed — start with paragraph/quote
  boundaries (largest deterministic unit), tighten later.
- **Normalization**: keep identity normalization (span text == spoken text) for Stage 2; real
  normalization is future work.

### Tests
- Contract test: spoken schema has `sourceAnchor`, not `exactSourceText`.
- Parser test: short anchor → correct extended span + offsets; ambiguous anchor → ReviewRequired.
- Handler/e2e: existing fixtures updated to the v6 spoken shape.

---

## Stage 3 — Lazy soundscape

### Why
`soundscape` (ambience/soundEvents/music) runs eagerly for every beat, but the still-image path
(`DeterministicMultimodalMediaCompiler.BuildStill`) never reads sound/music — those sections exist only
for the future speech/music/video media kinds. So the still-image path pays ~1.5–2 min and ~5k chars for
nothing.

### Change
1. Gate soundscape generation on whether any sound/music consumer is active. Simplest deterministic
   default for the still path: emit **empty** `soundEvents` + `music` arrays and a minimal
   `ambience` (location = beat location, `authoredSilence = true`) without an LLM call.
2. Add a per-request flag (e.g. on the production pipeline request, or a catalogue/plan-level
   `RequiresSoundscape` capability) so a future audio request can still trigger the real soundscape pass.
3. `SceneBeatProductionAssembler` (or handler) skips the soundscape LLM pass and substitutes the empty
   default when the flag is off.

### Contract version
No schema change to soundscape itself (only whether it's generated). May add a request/capability field
without bumping the response contract.

### Risks / decisions
- `ambience` is required by the parser; empty default must still satisfy `ValidateAmbience`
  (location == beat location; authoredSilence true means no sound sources required — already supported).
- Decision: default to lazy for stills; keep an explicit opt-in for audio.

### Tests
- Pipeline/handler test: soundscape disabled → empty sound/music + authored-silence ambience, plan
  still completes; enabled → real pass runs.

---

## Stage 4 — Structure `stateChanges` deltas + deterministic continuity states

### Why (and why it's lower ROI)
Stage 1 already made continuity a **small** dedicated LLM pass. The remaining win is correctness
(deterministic cross-consistency), not speed — the pass is already cheap. Do this only after Stages 2–3.

### Change
1. **`ActionStep` schema** — add `stateChanges: [{key, kind: characterState|wardrobeState|objectState, value}]`
   (nullable array) alongside the existing free-text `resultingState`.
2. **`StructureSystemPrompt`** — require exhaustive state deltas per step (who/what changed at each
   event, in profile-key/object-key terms).
3. **`SceneBeatProductionAssembler`** — fold `stateChanges` over a start-state baseline to produce
   `endContinuity`; `startContinuity` from profile baselines (`profile.Clothing` → wardrobeStates,
   `profile.Description` → characterStates) or the chained previous beat (Stage 5).
4. Keep `lighting`/`stateSummary` — these remain semantic; either (a) a tiny LLM output folded into the
   continuity pass, or (b) derive from structure lighting deltas. **Decision needed.**

### Contract version
`v6` → `v7` (structure schema changes).

### Risks
- State-delta completeness: a missed wardrobe/object change silently wrong-s the end state.
- Enumerating objects: object keys are free-form; the model must emit them consistently.

---

## Stage 5 — Continuity chaining across beats

### Why
Correctness: a beat's `startContinuity` should equal the previous beat's `endContinuity` (identity and
wardrobe persist across the scene). Currently each beat recomputes independently, so wardrobe/state can
drift beat-to-beat.

### Change
1. When enqueuing a beat, look up the previous beat (by catalogue entry `Order`) and its completed plan's
   `EndContinuityJson` (`ISceneBeatProductionPlanRepository.GetCurrentAsync(catalogueId, previousBeatId)`).
2. Pass it as the `startContinuity` baseline to the assembler (Stage 4) or, in Stage 1's small-LLM form,
   inject it into the continuity pass as context so the model anchors start == previous end.
3. First beat in a catalogue: profile baselines.

### Contract version
No response-schema change (continuity shape unchanged); this is a request/context change.

### Risks
- Requires catalogue beats to be completed in order (dependency). If a previous beat is missing/failed,
  fail fast (no fallback) per the repo strict-config rule, or require explicit re-run ordering.

---

## Cross-cutting

- **Rollout**: bump `ContractVersion` at each schema-changing stage (v6 spoken, v7 structure). Old plans
  are already superseded on version mismatch — no migration.
- **Test protocol**: after each stage, build web + tests, run the beat-production cluster
  (`SceneBeatProduction*`, `SceneBeatPipeline*`, `SceneBeatAnalyzer*`, `SceneMoment*`) and the affected
  contract/parser/handler/e2e tests; keep `DeterministicMultimodalMediaCompiler` + downstream consumers
  green (no API change).
- **Debug records**: each stage lands a `###-…` record under `specs/001-final-writing-instruction/debug/`.

## Decisions needed before implementation
1. Stage 2 extension boundary (paragraph vs sentence vs quote).
2. Stage 3 default: lazy-for-stills vs keep eager.
3. Stage 4 lighting/stateSummary: tiny LLM vs structure deltas.
4. Stage 5 ordering dependency: fail-fast vs best-effort.
