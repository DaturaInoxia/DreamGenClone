# 051 - Beat production Tier 1 (image default): spoken scaffolding removed (partial)

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59`
- **Date**: 2026-09-08 · follows 050 (spoken compaction)
- **Parent spec**: `specs/Planning/B-100-progressive-scene-beat-pipeline/beat-production-tiered-contract-spec.md`

## Report / Plan

Implement Tier 1 (image default): the spoken pass should emit only what images need
(`cueKey/order/kind/eventKey/exactSourceText/sourceKey/speakerKey/reviewStatus/reviewReason`) and the
assembler should fill the audio/video scaffolding deterministically. Also attempt to drop the
soundscape pass (audio-only).

## Analysis

- The `performance` block (~350 chars/cue), the 9-field `window`, `normalizedSpokenText`, and
  normalization are audio/video-only; the parser still requires them in the final combined JSON, so the
  assembler can fill deterministic defaults (neutral performance, eventKey-anchored window, identity
  normalization) without the model paying for them.
- **Soundscape drop reverted**: moment discovery/enrichment reference `SoundEventAnchor` sound-event
  roles, so an image-tier plan with no sound events fails enrichment validation. Full tier-awareness of
  the moment pipeline is a separate follow-on (see tiered spec §8).

## Resolution (shipped)

- `SceneBeatProductionContract` → `scene-beat-production-v7`; `SpokenCue()` schema slimmed to the image
  essentials; `ProviderPassCount` back to 4 (soundscape still runs).
- `SceneBeatProductionAssembler.ExpandSpokenCues` now fills the full `DialogueInput`: `displayText` =
  `exactSourceText`, `normalizedSpokenText` = `exactSourceText`, `normalizationMethod` = `"verbatim"`,
  version `"1"`, default `performance` (en-US / Neutral / Medium), eventKey-anchored `window`,
  `addresseeKeys=[]`, `lipSyncRelevant=false`.

## Validated

- Build: 0 errors.
- Beat-production cluster: **102 passed / 0 failed**.
- Live re-run pending.

## Deferred

- Soundscape/music/ambience deferral + moment-enrichment tier-awareness (image tier without sound
  events) — see tiered spec §8 and `beat-production-audio-video-path-plans.md`.
