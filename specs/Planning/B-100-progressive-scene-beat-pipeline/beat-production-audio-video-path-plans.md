# Beat Production — Audio & Video Path Plans

- **Status**: plan (not implemented)
- **Date**: 2026-09-08
- **Parent**: `beat-production-tiered-contract-spec.md`

Both paths are **append-only**: the image tier already produced the shared sections (events, timeline,
actionArc, slim cues, continuity, typedReferences, shotIntent, moment key-states). Audio/video paths
add only their own sections to the same plan, never regenerating images.

---

## Audio path (AVN)

### Trigger
User opts a beat/turn into audio (AVN). Persisted as a production intent on the group/request
(`ImageAudio` tier).

### What it adds (one small durable job, "beat-production-audio")
- per dialogue cue: `performance` (languageCode, emotion, intensity, pace, accent, pause,
  pronunciation, non-verbal) + `normalizedSpokenText` + normalization method/version.
- `ambience`, `soundEvents`, `music`.

### Model
Same `RolePlaySceneBeatAnalyzer` resolver (deepseek-v4-flash today). No new function default unless
a dedicated audio model is wanted later (open decision).

### Consumers unlocked
- `DeterministicMultimodalMediaCompiler.BuildSpeech` — dialogue + performance + typedReferences.
- `…BuildAmbienceEffects` — sound cues + ambience.
- `…BuildMusic` — music + typedReferences.

### Data model
- New plan section JSONs populated: `ambience`, `soundEvents`, `music`; dialogue cues gain
  `performance` + `normalizedSpokenText` (versioned, append-only).
- Plan `Version` bumps on append; image-derived sections are untouched.

### Estimate
One structured pass, ~3–5k chars, **~1–2 min**. Independent of image generation; retryable on its own.

### Contract version
`scene-beat-production-v8` (audio sections added; image sections unchanged).

---

## Video path (select beats/turns)

### Trigger
User selects a beat/turn for video (finishing touches). Persisted as `ImageAudioVideo` tier.

### What it adds (one small durable job, "beat-production-video")
- full `videoCoverage` refinement: `cameraIntent`, `lensIntent`, `motionIntent`, `pacingIntent`,
  `performanceIntent`, `durationFitPolicy`, `lipSyncRequired`, `audioOwnership`.
- full per-cue `window` (duration intent, precision, overlap policy, continuity lead-in/tail),
  `lipSyncRelevant`.
- (audio ownership requires the audio path's cue keys to exist — so video implies audio, or reuses
  audio-tier cue keys if already present.)

### Model
Same analyzer resolver.

### Consumers unlocked
- `DeterministicMultimodalMediaCompiler.BuildVideo` (native/external audio, key-states, actionArc,
  continuity, references).
- `…BuildLipSync` (dialogue + lip-sync windows + speech alignment).

### Data model
- Plan `videoCoverage` refined; cues gain full `window` + `lipSyncRelevant`. Append-only, version bump.

### Estimate
One structured pass, ~2–4k chars, **~1–2 min**.

### Contract version
`scene-beat-production-v9` (video sections added).

---

## Cross-cutting decisions (open)

1. Model per tier: reuse `RolePlaySceneBeatAnalyzer` for all three, or add `RolePlayBeatAudio` /
   `RolePlayBeatVideo` function defaults (UI-backed model choice per tier). Recommend: reuse now,
   split only if a tier needs a different model.
2. Video-implies-audio vs independent: recommend video reuses audio-tier cue keys if present, else
   fails fast asking to run audio first (no silent fallback).
3. Trigger UX: tier intent persisted where — recommend on the production group + request (same as the
   image tier decision).
4. Append concurrency: one active audio/video job per plan (dedupe key) to avoid double-append.

## Rollout order

1. Tier 1 (image default) — see implementation plan below.
2. Audio path job + `performance`/`normalized`/sound/music consumers.
3. Video path job + full coverage/window/lip-sync consumers.
