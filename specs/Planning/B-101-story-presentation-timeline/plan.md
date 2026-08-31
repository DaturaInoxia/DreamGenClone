# B-101 Implementation Plan

## Architecture Principles

1. RP text remains authoritative; presentation is a versioned projection.
2. Storyboard authoring, media generation, and VN playback are separate application boundaries.
3. B-100 Beat Production Plans and Moment enrichments provide all canonical media semantics; B-101 segments provide selection, presentation time, and navigation.
4. B-101 never repairs missing production metadata by re-analyzing RP prose.
5. Candidate generation never implies approval or publication eligibility.
6. Text-first delivery proves the domain before expensive media integration.
7. B-101 submits through compiler/generator contracts but does not own prompt compilation, provider clients, candidate validation, or asset approval.

## Ownership Boundaries

| Concern | Intended ownership |
|---|---|
| Presentation entities and invariants | `DreamGenClone.Domain` presentation namespace |
| Import, authoring, validation, publication use cases | `DreamGenClone.Application` |
| SQLite repositories and asset storage | `DreamGenClone.Infrastructure` |
| Storyboard Studio and Visual Novel Player | separate pages/components in `DreamGenClone.Web` |
| Beat/Moment/image/audio/video production semantics | B-100 ownership; referenced read-only |
| Still compilation, generation, candidate review, and approval | B-100 compiler contracts plus B-032 execution/approval; consumed read-only |
| Future audio/video compilers, clients, candidate review, and approval | separate generator epics consuming B-100 contracts |

## Phase 0 - End-to-End Production Contract Gate

1. Use the B-100 sanitized corpus covering dialogue, narration, ambience, effects, image states, one-Moment motion, two-Moment transitions, and whole-Beat action.
2. Require completed expected B-100 Beat Production Plans, production cues, video coverage, Moment roles, and Moment enrichments.
3. Decide the V1 Back/Forward restart/resume matrix for speech, ambience, music, and video.
4. Define publication-valid text-only and still-only profiles before generator work.

**Exit:** every reference case supplies complete image/audio/video production data before B-101 import; no Storyboard semantic extraction is needed.

## Phase 1 - Presentation Core and Source Import

1. Add `StoryPresentation`, revision, sequence, segment, source-anchor, and Text Cue domain models.
2. Add additive SQLite schema, repositories, optimistic concurrency, and immutable publication state.
3. Implement explicit Beat Production Plan selection and immutable version/checksum snapshots.
4. Import B-100 dialogue/sound/video cues and Moment enrichments without changing them.
5. Implement source refresh with a reviewed diff rather than silent rebinding.
6. Add application services for create/fork/query/edit/reorder/split/merge.

**Exit:** a text-only linear presentation can be authored, revised, validated, and persisted.

## Phase 2 - Storyboard Studio Text-First Slice

1. Build a dedicated Storyboard Studio route and sequence/segment navigator.
2. Show source RP text beside projected VN text and lineage.
3. Implement segment editing and explicit visual/audio policy controls.
4. Add preview mode with deterministic Back/Forward behavior.
5. Surface validation findings and stale-source conflicts.
6. Keep the UI usable without any generated media.

**Exit:** an author can create and preview a complete text-only VN adaptation.

## Phase 3 - Still Coverage and B-032 Integration

1. Add Visual Cue, production-brief selection, approved-asset adapter, and Timeline Placement models.
2. Resolve source Moments and request B-032 still generation without duplicating its compiler or approval path.
3. Place exact `ApprovedSceneFrame` versions/checksums.
4. Implement `NewStill`, `HoldPreviousStill`, and `TextOnly` publication validation.
5. Add image-coverage metrics per sequence and presentation.

**Exit:** a still-image Visual Novel can publish with intentional image reuse and no hidden asset selection.

## Phase 4 - Dialogue Placement, Voice Identity, and Speech Requests

1. Import B-100 exact dialogue/narration cues and their attribution/review status.
2. Block unresolved attribution and route correction back to a new B-100 production-plan revision.
3. Add versioned Voice Identity metadata and consent/provenance.
4. Add Speech Cue Placement and production-brief selection contracts referencing B-100 cue IDs.
5. Add timing anchors, captions/transcripts, and speech-synchronized text reveal.
6. Build production-selection and approved-asset interfaces without defining a TTS compiler or choosing the final provider in this architecture item.

**Exit:** every placed spoken line retains B-100 source/speaker/performance semantics and adds voice, timing, and mix choices ready for independent TTS.

## Phase 5 - Audio Placement and Timeline Resolution

1. Import typed B-100 Ambience, Sound Effect, and Music Intent cues.
2. Place cues with continuity groups, location/time boundaries, and explicit silence.
3. Add production-brief selections and generator-owned approved-asset adapters referencing exact B-100 cue IDs.
4. Implement track overlap rules, mix intents, fade/ducking metadata, and concrete duration resolution.
5. Build an audiovisual timeline preview and validation matrix.

**Exit:** one presentation can resolve deterministic multi-track audio timing with placeholder or approved assets.

## Phase 6 - Video Coverage Selection and Placement

1. Import B-100 Video Coverage Plans and validation status.
2. Build selectors for existing one-Moment, two-Moment transition, Beat-excerpt, and whole-Beat plans.
3. Snapshot exact B-100 action, continuity, key-Moment, camera, dialogue/audio, and content-policy lineage.
4. Map B-100 dialogue cue requirements to selected speech placements and resolved generator line windows.
5. Add a provider-neutral video production-selection boundary and approved video asset placement.
6. Validate video audio modes against external cue tracks and prevent duplicate speech/effects.

**Exit:** Storyboard Studio can request and place video from complete B-100 metadata without interpreting the RP story or depending on one video model.

## Phase 7 - Approved-Media Eligibility and Publication Compiler

1. Implement thin adapters over modality-owned approved-asset eligibility contracts.
2. Implement one presentation validator with stable entity-scoped findings.
3. Resolve all relative timing to concrete segment manifests.
4. Verify approved assets, checksums, source versions, policies, and continuity without re-approving them.
5. Compile immutable Playback Manifest schema v1.
6. Promote revisions through compare-and-set publication.
7. Preserve published manifests when new drafts or regenerated assets appear.

**Exit:** text-only, still, audio, and video-capable revisions publish through the same deterministic contract.

## Phase 8 - Separate Visual Novel Player

1. Build a dedicated player route that accepts presentation/revision identity only.
2. Render media and text from the manifest with no authoring services or model clients.
3. Implement Back/Forward, keyboard/touch controls, bounded navigation, and state restoration.
4. Implement captions/transcripts, volume/mute controls, reduced motion, and mobile layout.
5. Preload bounded adjacent assets and handle asset-read failures explicitly.
6. Add end-of-presentation behavior and return-to-library flow.

**Exit:** published presentations play deterministically on desktop and mobile without model or network requirements for local assets.

## Phase 9 - Generator Epics and Quality Expansion

Create separate implementation items for:

- speech/TTS and voice consistency;
- ambience/effect/music sourcing or generation;
- audio mixing and mastering;
- image-to-video and text-to-video;
- lip sync and dialogue-video synchronization;
- audio/video validation and bounded repair;
- export/packaging and optional branching.

Each consumes B-100 production contracts plus B-101 presentation selections, owns its candidate and approval lifecycle, and exposes approved derivatives through a common eligibility contract. None may redefine story facts, production semantics, order, or lineage.

## Testing Strategy

### Domain and repository

- source lineage and checksum validation;
- draft concurrency and published immutability;
- contiguous ordering and segment edits;
- cue timing cycle/overlap resolution;
- coverage-kind and Moment-order invariants;
- approval and asset checksum eligibility;
- compare-and-set publication.

### Contract corpus

- exact import of B-100 dialogue spans and speaker attribution;
- exact import of narration/dialogue separation;
- placement of B-100 ambience and location transitions;
- placement of B-100 effect-to-Moment alignment;
- one-Moment, two-Moment, Beat-excerpt, and whole-Beat B-100 video plans;
- synchronized dialogue line windows.

### UI and end-to-end

- text-only authoring and publication;
- still-image reuse and explicit text-only segments;
- Storyboard preview equals Player order;
- Back/Forward restoration matrix;
- desktop/mobile text and media fit;
- no model calls during playback;
- old published revision remains playable after draft changes.

## Rollout Gates

| Gate | Requirement |
|---|---|
| G1 B-100 prerequisite | Reference corpus has complete, valid image/audio/video production plans. |
| G2 Text-first | Text-only presentation publishes and plays end to end. |
| G3 Still VN | B-032 approved frames and holds publish without duplicate approval. |
| G4 Audio placement | Imported B-100 dialogue/ambience/effects place without semantic reinterpretation. |
| G5 Video placement | Every B-100 coverage kind requests and places with exact source/continuity/sync lineage. |
| G6 Publication | Manifest is deterministic, checksummed, and stale-write safe. |
| G7 Player | Back/Forward restoration and accessibility matrices pass desktop/mobile. |

## Migration and Compatibility

- Existing RP sessions, beat analyses, prompts, and images are not rewritten.
- A presentation is opt-in and references exact source versions.
- Existing B-032 complete images are not automatically eligible; only approved frames may be placed.
- Early text/still manifests remain readable as later manifest schema versions add audio/video tracks.
- Manifest readers reject unsupported required features explicitly.