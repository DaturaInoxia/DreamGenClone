# B-101 Analysis - From Roleplay Story to Audiovisual Presentation

**Analyzed:** 2026-08-27  
**Scope:** Story metadata, storyboard authoring, audiovisual generation inputs, sequencing, and Visual Novel playback. Media-model implementation is excluded.

## Executive Assessment

The product vision has three distinct concerns that should not share one mutable model:

1. authoring authoritative story prose through roleplay;
2. editorially adapting that story into a timed audiovisual presentation;
3. playing a stable published presentation.

B-100 provides the missing narrative decomposition: a Beat describes development over time and a Moment identifies one frozen state. This is necessary but not sufficient for audio or video. Speech, ambience, effects, music, and motion occupy intervals, can overlap, and need synchronization. Attaching one generated audio blob or video blob directly to a Moment would overload the Moment in the same way the earlier image design overloaded Beat.

The recommended architecture is a separate `StoryPresentation` wrapper with immutable revisions. Its ordered `PresentationSegment` is both the Visual Novel navigation unit and timing container. It imports canonical B-100 Beat Production Plans, Moment key states, and production cues. Media-generation systems consume B-100 production contracts and return candidate assets; the Storyboard selects/places assets and publication compiles a deterministic playback manifest.

## Existing Foundation

- `RolePlayV2Turn`, Narrative, and interactions provide authoritative source text and chronology.
- B-100 defines versioned Beat Catalogues, Moment Sets, Moments, and source evidence.
- B-032 defines provider-specific image compilation, candidate renders, validation, explicit acceptance, and `ApprovedSceneFrame`.
- B-032 continuity work defines character identity packs, location profiles, visual plans, shot plans, and approved continuity anchors.
- Existing background-job and Model Manager work can later host independent media generators.

After the corrected B-100 boundary, B-100 owns dialogue attribution, soundscape/events, action arcs, continuity boundaries, and video coverage. No existing artifact owns presentation sequencing/timing, asset placement, publication, or Visual Novel playback; those remain B-101's purpose.

## Findings

### F-101-01 - A Moment cannot own timed media

A Moment is an exact frozen state. Dialogue, footsteps, a door slam, room tone, music, and video all have duration. Some begin before the Moment, continue after it, or span several Moments.

**Decision:** Keep Moment semantically frozen. B-100 derives audio/video source cues and anchors; B-101 places those existing cues on duration-bearing `PresentationSegment` tracks.

### F-101-02 - Storyboard and playback are different models

Storyboard authoring needs drafts, alternatives, unresolved questions, candidate assets, regeneration, and editorial notes. Playback needs one stable ordered state with concrete asset choices and timing.

**Decision:** Author against mutable draft revisions with optimistic concurrency. Publish an immutable revision and compile a self-contained playback manifest.

### F-101-03 - Exact dialogue needs stronger provenance than prose prompts

Speech generation and lip sync require exact words, speaker identity, delivery, and timing. RP content may contain narration, quoted dialogue, multiple speakers, or ambiguous attribution. Incorrectly assigning a line to a character is a severe semantic error.

**Decision:** B-100 stores every exact dialogue span, speaker, addressee, delivery intent, and lip-sync relevance. B-101 may select the cue, assign an approved voice version, and place it in presentation time; it does not perform attribution.

### F-101-04 - Audio is several coordinated tracks

Character speech, narration, ambience, discrete effects, and music have different source, continuity, generation, and mixing semantics. One `AudioDescription` field cannot express overlap or ownership.

**Decision:** B-100 models speech, ambience, sound effects, and music intent as separate semantic cues. B-101 models their selected assets, explicit presentation timing, continuation, fades, and mix behavior.

### F-101-05 - Video coverage is variable

A useful video may animate one Moment, interpolate between two Moments, depict a short Beat interval, or cover a whole Beat. Assuming one video per Moment loses action arcs; assuming one video per Beat may produce an overlong or under-specified request.

**Decision:** B-100 `SceneVideoCoveragePlan` explicitly defines `MomentHold`, `MomentAction`, `MomentTransition`, `BeatExcerpt`, or `WholeBeat`, including action/state/audio requirements. B-101 selects one plan, submits it through the owning generator, and places an approved resulting video.

### F-101-06 - More media improves richness, not story completeness

A Visual Novel can reuse an image over multiple text advances, play a video for one segment, or intentionally present text only. Requiring a unique image per text block would inflate cost and block publication. Silently reusing whatever asset is available would be unpredictable.

**Decision:** Every segment explicitly declares `NewStill`, `HoldPreviousStill`, `Video`, or `TextOnly`, plus explicit audio continuation/silence rules. Coverage quality can be measured independently from publication validity.

### F-101-07 - Generated assets are derivatives, not source truth

Images, speech, sound effects, music, and videos may have several takes and may be regenerated. Their status must not alter source story, Beat, Moment, or cue metadata.

**Decision:** Immutable asset attempts reference semantic briefs. Explicit approval creates eligible media assets. Timeline placements select approved assets by stable version/checksum.

### F-101-08 - The VN player must remain deterministic

Calling analysis or generation models during playback would make Back/Forward behavior slow and non-repeatable and could expose missing configuration at viewing time.

**Decision:** Player consumes only a published manifest with resolved assets, durations, transitions, text, and mix instructions. Publication fails if required dependencies are unresolved.

## Terminology Decision

| Candidate term | Strength | Problem | Use |
|---|---|---|---|
| Storyboard | Familiar visual authoring metaphor | Often implies only shot images and panels | UI and workflow name: Storyboard Studio |
| Timeline | Correctly expresses duration and overlap | Does not by itself imply narrative source | Core ordered/timed structure |
| Visual Novel Script | Fits playback | Too presentation-specific for video production metadata | Future export/projection |
| Media Blueprint | Expresses provider-neutral generation inputs | Weak as navigation model | Name for cue/shot briefs |
| Story Presentation | Covers text, stills, audio, and video | Requires timeline detail beneath it | Root aggregate |

**Selected vocabulary:** `StoryPresentation` -> versioned Presentation Timeline -> Presentation Segments and Media Briefs. "Storyboard" names the authoring UI.

## Options Considered

### Option A - Add audio and video fields to `SceneMoment`

- Simple initial schema.
- Fails for duration, overlap, multi-Moment transitions, whole-Beat video, and image reuse across text advances.
- Couples B-100 analysis to every future media type.

**Decision:** Rejected.

### Option B - One generic media JSON document per Beat

- Flexible storage.
- Weak invariants, difficult editing, ambiguous provenance, and provider concepts would accumulate in opaque JSON.

**Decision:** Rejected as the canonical domain. JSON snapshots remain appropriate at job boundaries.

### Option C - B-100 production plans plus versioned presentation wrapper

- B-100 represents canonical story/media semantics; B-101 represents intervals, overlap, navigation, approved assets, and publication.
- Keeps media generators replaceable.
- Adds a deliberate editorial layer and more entities.

**Decision:** Recommended.

### Option D - Generate a complete video from the RP turn

- Minimal editorial interaction.
- Poor control, weak correction boundaries, difficult dialogue synchronization, and no natural Visual Novel navigation.

**Decision:** Possible future export from a published timeline, not the source architecture.

## Recommended Product Sequence

1. Implement linear text-first `StoryPresentation` import and segment authoring.
2. Integrate approved still frames and explicit hold/text-only policies.
3. Import B-100 dialogue/narration cues and require resolved attribution before placement.
4. Import B-100 ambience/sound cues and build timeline preview/mixing placement.
5. Add voice identities and speech generation requests sourced from B-100 cues.
6. Import B-100 video coverage plans and place generated video over Moment/Beat ranges.
7. Publish immutable playback manifests and build the separate Visual Novel Player.
8. Add richer media generation, validation, and synchronized video/audio compilation behind stable contracts.

This sequence validates the story and playback architecture before expensive media implementation.