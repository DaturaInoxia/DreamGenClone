# Beat Production — Tiered Contract (Image → Audio → Video) — Spec

- **Status**: spec (proposal; not implemented)
- **Date**: 2026-09-08
- **Supersedes the per-pass micro-optimizations** in `beat-production-deferred-stages-detailed-plan.md`
  (anchor compaction, soundscape trim) — the scaffolding analysis below shows the real fix is
  tiering, not trimming.
- **Parent design**: `beat-production-deterministic-assembly-design.md` (Stage 1 shipped, debug 049)

## 1. Product intent (authoritative)

- **Image** is first in the workflow and highest-volume: most beats get *some* images.
- **Audio** is for the AVN and is moderate-volume.
- **Video** is last, lowest-volume — only select beats/turns, as finishing touches.

Therefore beat production must **not** eagerly author the full audio/video ontology. It should
author the image tier by default, and author audio/video data **on demand** for the beats that
actually request them.

## 2. Current state (why this is needed)

Beat production authors all 12 sections in one run, in 4 LLM passes (structure → spoken ∥ soundscape
→ continuity), then deterministically assembles typedReferences + videoCoverage. Measured ~8 min and
~45k chars for one beat. The dominant cost is **scaffolding, not prose**:

- A 1,179-char narration cue = ~132 chars `exactSourceText` (the actual prose) + ~350 chars
  `performance` block (audio-only) + ~150 chars `window` block (video/audio-only) + keys/enums.
- `exactSourceText` is **~6%** of the spoken pass; `performance` + `window` are **~60–70%**.
- Soundscape (~17k chars) is ambience + soundEvents + music — almost entirely audio-only.

So the image path pays ~8 min for data it never reads. This spec replaces that with a tiered
contract.

## 3. Proposed tiers

### What is SHARED (always authored, every tier)

These are needed by images **and** the AVN/video timeline, so they are never dropped:

- `events`, `timeline`, `actionArc` — temporal/action structure (AVN needs the timeline; images need the
  action arc and event anchors).
- `narration` / `dialogue` — `cueKey, order, kind, eventKey, exactSourceText, sourceKey, speakerKey,
  reviewStatus`, plus a **coarse temporal anchor** (one `eventKey`, not the 9-field window).
- `startContinuity` / `endContinuity` — frozen visual state (identity/wardrobe/location/lighting);
  the AVN needs continuity too.
- `typedReferences` — identity/wardrobe/location (deterministic).
- **`shotIntent`** — per coverage/moment camera + lens + view intent (e.g. "medium shot, over Becky's
  shoulder toward the trailer"). Images NEED this for composition; it is not video-only.

### Tier 1 — Image (default, highest volume)

Everything in SHARED, plus the moment key-state plan (`coverageKey, kind, sourceEventKeys,
requiredMomentRoles`) that moment discovery reads. **Not authored**: `performance`, `normalizedSpokenText`,
`ambience`, `soundEvents`, `music`, full windows, lip-sync, audio ownership.

### Tier 2 — Audio (AVN, on demand)

Adds: `performance` (voice emotion/pace/accent/pause/pronunciation), `normalizedSpokenText` +
normalization, `ambience`, `soundEvents`, `music`.

### Tier 3 — Video (select beats, lowest volume)

Adds: full per-cue `window` (duration intent, precision, overlap, continuity flags), `lipSyncRelevant`,
`audioOwnership`, and the refined `motionIntent`/`pacingIntent`/`durationFitPolicy`.

### Verified finding (why camera intent must stay in the image tier)

`SceneMomentDiscoveryContract.SceneVideoCoverageSnapshot` carries only `coverageKey/kind/sourceEventKeys/
requiredMomentRoles/permittedActionPhases` — camera/lens/motion intent is authored in the beat plan but
currently consumed **only** by `DeterministicMultimodalMediaCompiler.BuildVideo` (no live caller). The
still image's camera/view is produced later at moment enrichment (`compositionRationale`, frozen state,
production roles), not from the beat plan. For the image tier we keep a compact `shotIntent` and **wire
it into the still composition** so images are camera-aware.

### Tier 2 — Audio (on demand; AVN)

Adds, generated only when an audio consumer is requested:
- `performance` per dialogue cue (languageCode, emotion, intensity, pace, accent, pause, …)
- `normalizedSpokenText` + normalization
- `ambience`, `soundEvents`, `music`

Resolves the earlier "lazy soundscape breaks `SoundEventAnchor`" finding: sound-event anchors are an
**audio-tier** concern — moment enrichment only needs them when the beat is in the audio tier.

### Tier 3 — Video (on demand; select beats only, lowest volume)

Adds, generated only for selected beats/turns:
- full `videoCoverage` (cameraIntent, lensIntent, motionIntent, pacingIntent, performanceIntent,
  durationFitPolicy, lipSyncRequired, audioOwnership, permittedActionPhases)
- full per-cue `window` (duration intent, precision, overlap, continuity flags)
- `lipSyncRelevant` per dialogue cue

## 4. Tier request mechanism

- Add a persisted production intent per beat (e.g. `BeatProductionIntent { Image, ImageAudio, ImageAudioVideo }`)
  to `GenerateSceneBeatProductionPlanRequest` (or the catalogue entry / production group).
- Default = **Image** (the Studio's current behavior).
- Audio/Video tiers are opt-in per beat/turn; a future AVN/video surface sets them before enqueue.
- The pipeline reads the intent and runs only the passes for the requested tier:
  - Image: structure → spoken(slim) → continuity → deterministic assemble.
  - Audio: + soundscape + spoken performance/normalization.
  - Video: + full video coverage pass (or extends the deterministic assembler's coverage).

## 5. Consumer impact matrix

| Consumer | Reads | Tier needed |
|---|---|---|
| moment discovery | actionArc, continuity, typedReferences, **momentKeyStates roles** | Image |
| moment enrichment | frozen state, participant summary, production roles, videoKeyState | Image |
| `BuildStill` (images) | moment, frozen state, continuity, typedReferences, videoKeyState | Image |
| `BuildSpeech` / `BuildAmbience` / `BuildMusic` (audio) | performance, normalized text, ambience, soundEvents, music, typedReferences | Audio |
| `BuildVideo` / `BuildLipSync` (video) | full videoCoverage, actionArc, continuity, windows, lipSync, audioOwnership | Video |

The image path (moment discovery/enrichment + `BuildStill`) never reads the audio/video sections, so
Tier 1 is complete for it.

## 6. Estimated effect (honest)

| Tier | LLM passes | Est. output | Est. active time |
|---|---|---|---|
| Image (default) | structure + spoken(slim) + continuity | ~15k chars | **~5–6 min** |
| Audio | + soundscape (+ performance/normalized) | ~20k chars | ~7–8 min |
| Video (select) | + full video coverage | ~25k chars | ~8–9 min |

Dropping `performance` + `normalizedSpokenText` + `ambience`/`soundEvents`/`music` + full windows cuts
~65% of the output chars (45k → ~15k). The wall time drops less than the chars because **structure
(~2.3 min) and continuity (~2.1 min) are fixed deepseek-latency costs** driven by the shared context,
not by their output size. Reaching ~2–3 min additionally requires compacting the structure and
continuity passes (smaller shared context / fewer tokens), which is a separate follow-on.

## 7. Contract / migration

- `scene-beat-production-v7` — sections `performance`, `ambience`, `soundEvents`, `music`,
  `videoCoverage` (full) become **tier-gated**: present/rich only in the tier that requested them;
  absent/empty in Image tier.
- Parser + `SceneBeatProductionPlanData` keep the same section JSONs so downstream consumers are
  unchanged; empty/absent sections are tolerated for the tiers that don't produce them.
- Old plans are superseded on version bump (existing behavior).
- `SceneBeatProductionAssembler` becomes tier-aware (builds the right sections per intent).

## 8. Open decisions

1. Where the tier intent lives (production group vs catalogue entry vs request) — recommend request +
   production group persisted field.
2. Whether `momentKeyStates` is a separate section or a minimal form of `videoCoverage` (recommend
   keep `videoCoverage` name, make video-only fields optional/empty in Image tier).
3. Whether audio/video tiers are re-entrant (re-run the same beat later to ADD audio/video without
   regenerating images) — recommend yes (append-only, versioned sections).
4. Whether the slimmed Image-tier cue still needs `window` at all (recommend keep a single
   `eventKey` anchor, drop the 9-field window).
5. **Camera/view intent**: author a compact `shotIntent` at the beat level and wire it into the still
   composition (`BuildStill` / moment enrichment), vs. generate camera intent only at moment
   enrichment. Recommend the former so the beat-level intent is consistent across the image and
   later video tiers.
