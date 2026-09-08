# 050 - Beat production spoken compaction (Stage 2) + lazy-soundscape revert (Stage 3)

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59`
- **Date**: 2026-09-08 · follows 049 (deterministic assembly)

## Report / Plan

Following 049, implement the deferred stages that reduce beat-production latency:

- **Stage 2 (spoken compaction)** — the spoken pass re-emits `exactSourceText` AND `displayText` AND
  `normalizedSpokenText` plus normalization metadata (three copies of the same prose). This is the
  single biggest token sink.
- **Stage 3 (lazy soundscape)** — skip the soundscape LLM pass and emit an authored-silence default.

## Analysis

- Spoken compaction: the app already stores the resolved span + offsets on the parsed cue, and the
  parser's `ValidateSpokenNormalization` accepts identity normalization. So `displayText`,
  `normalizedSpokenText`, `normalizationMethod`/`normalizationVersion` can be derived app-side from
  `exactSourceText` (identity), removing two of the three copies + metadata from the model output.
- Lazy soundscape: **reverted**. Moment discovery/enrichment reference sound-event anchors
  (`SoundEventAnchor` production role — the e2e fixture exercises it), so sound events cannot be
  dropped unconditionally. A narrower, sound-anchor-aware deferral is future work.

## Resolution

- `SceneBeatProductionContract.cs` → `scene-beat-production-v6`; spoken pass uses a compact
  `SpokenCue()` schema (drops `displayText`/`normalizedSpokenText`/`normalizationMethod`/`normalizationVersion`);
  `CreateResponseSchema` keeps the full `DialogueCue()`.
- `SceneBeatProductionAssembler.ExpandSpokenCues` derives `displayText = exactSourceText`,
  `normalizedSpokenText = exactSourceText`, `normalizationMethod = "verbatim"`, `normalizationVersion = "1"`.
- Stage 3 reverted: handler runs spoken ∥ soundscape again; `ProviderPassCount` back to 4; assembler
  reads soundscape from the pass (no default).

## Validated

- Build: 0 errors.
- Beat-production cluster: **102 passed / 0 failed**.
- Live re-run pending.
