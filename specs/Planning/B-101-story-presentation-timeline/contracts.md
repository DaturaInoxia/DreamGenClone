# B-101 Contracts

## Source Import Contract

`IStoryPresentationImportService` accepts a session ID and explicit ordered Beat Production Plan IDs. It imports exact source lineage, normalized dialogue/narration cues, sound cues, video coverage plans, Moment sets, and Moment enrichments from completed current B-100 records.

Import never:

- mutates RP interactions, turns, Beats, or Moments;
- treats generated media as story evidence;
- invents or re-derives a missing speaker, addressee, ambience, sound event, action, video coverage plan, Moment, or chronology edge;
- silently upgrades to a newer B-100 version after draft creation.

Missing required analysis returns a typed prerequisite result. A later explicit refresh forks or updates the draft with a visible source-diff review.

## Storyboard Arrangement Contract

A proposal service may suggest sequences, segment boundaries, cue inclusion, and presentation placement using B-100 record IDs. It may not inspect raw RP prose to produce new semantic cues.

Every proposal item has:

- exact B-100 production IDs and version lineage;
- presentation timing/placement confidence;
- `Ready` or `ReviewRequired` placement status;
- no provider/model prompt syntax;
- no asset eligibility until persisted and approved.

Speaker attribution, exact text, sound-event meaning, Beat/Moment membership, action arcs, and video coverage are imported facts. Any unresolved B-100 semantic status blocks dependent generation and publication; B-101 cannot repair it by inference.

## Draft Editing Contract

All edits target one draft revision and require its concurrency token. Structural edits include segment split/merge/reorder, source reassignment, and cue movement. They trigger revalidation of downstream timing and media briefs.

Published revisions are immutable. Editing a published presentation first creates a new draft based on that revision. Candidate and approved assets remain reusable only when source lineage, brief version, continuity, and content policy are compatible.

## Timing Contract

Authoring uses relative segment time. A cue start is expressed as a typed anchor plus offset:

- `SegmentStart`
- `TextCueStart`
- `TextCueEnd`
- `MomentAnchor`
- `PriorCueEnd`
- `SegmentEnd`

Circular dependencies, negative resolved time, invalid overlap, and out-of-segment placement fail validation.

Duration resolution order is explicit by `DurationMode`:

- `ReaderAdvanced`: no automatic segment end; audio/video may complete while the segment remains visible.
- `Fixed`: configured segment duration is authoritative.
- `MediaDriven`: one designated approved asset supplies duration.

There is no implicit default duration. Required absent values fail publication.

## Text and Dialogue Contract

The exact B-100 dialogue/narration cue is immutable. Display omission or presentation-only projection is allowed with edit provenance, but changing spoken meaning requires an explicit B-100 production-plan revision before regeneration.

A speech-ready dialogue cue requires:

- exactly one speaker character;
- exact spoken text;
- approved attribution;
- one Voice Identity Version;
- delivery intent and language;
- a valid timing anchor;
- explicit lip-sync requirement;
- content-policy compatibility.

No narrator voice, generic voice, or alternate speaker is selected as a fallback.

## Ambience and Sound Contract

Ambience is stateful across segment boundaries only through an explicit continuity group and `ContinueUntilBoundary`. Location/time/environment changes require a reviewed continue, crossfade, replace, or stop decision.

A sound-effect placement requires a B-100 sound cue with its event, temporal relation, and on-screen/diegetic state. B-101 resolves only presentation offset, asset, gain, and spatial mix.

Silence and omitted music are valid authored choices. Missing required sound is not interpreted as silence.

## Video Contract

Imported B-100 coverage validation:

| Coverage kind | Required source |
|---|---|
| `MomentHold` | one Moment; no material state change |
| `MomentAction` | one Moment plus bounded action around that anchor |
| `MomentTransition` | ordered start and end Moments from one Beat/sequence context |
| `BeatExcerpt` | Beat plus explicit source interval and start/end states |
| `WholeBeat` | Beat plus complete action arc and any required internal key Moments |

Every selected B-100 video plan includes cast/identity state, wardrobe, location, action arc, camera intent, duration intent, content policy, continuity inputs, and audio policy. B-101 may choose presentation duration only within the plan/compiler's allowed range.

B-100 maps dialogue cues to video line intent, on-screen speaker, and lip-sync requirement. The resolved generator contract supplies concrete line windows. B-101 places the result and verifies cue/asset mappings. `GeneratedWithVideo` remains acceptable only when returned streams/captions verify against B-100 cue IDs and exact dialogue.

## Media Generation Boundary

```csharp
public interface IMultimodalProductionBriefCompiler
{
    string MediaKind { get; }
    string CompilerKey { get; }

    CompiledMediaRequest Compile(
        MultimodalProductionBriefSnapshot brief,
        ResolvedMediaModel model);
}

public interface IMediaAssetEligibilityService
{
    Task<ApprovedMediaAsset> RequireApprovedAsync(
        string approvedMediaAssetId,
        MediaPlacementRequirements requirements,
        CancellationToken cancellationToken = default);
}
```

The `MultimodalProductionBriefSnapshot` is compiled from current B-100 production records. Model Manager declares media kind, capabilities, compiler key, content policy, duration limits, input modalities, audio/lip-sync support, and output formats. Exactly one compatible path is resolved. Missing/incompatible configuration fails before job acceptance; no media-kind fallback or raw-RP reinterpretation is allowed.

## Asset Approval Contract

Generation completion does not make an asset eligible. Eligibility requires explicit approval, checksum verification, source/brief compatibility, required validation, and non-revoked status.

B-032 `ApprovedSceneFrame` is the still-image authority. B-101 may reference it directly or through an `ApprovedMediaAsset` adapter record, but must not create a competing still-image approval path.

Audio/video validation policies are future implementation packages. Until configured automation exists, explicit human review is required; the system must not fabricate validation success.

## Publication Contract

Publication is one compare-and-set operation over a `Ready` revision. It validates:

1. contiguous sequence and segment order;
2. valid and current source lineage;
3. resolved text and speaker attribution;
4. allowed visual mode for every segment;
5. explicit audio state for every segment;
6. all required approved assets and exact checksums;
7. valid timing, overlap, transitions, and duration source;
8. video start/end state and dialogue-sync completeness;
9. content-policy compatibility;
10. no unresolved blocking findings.

The operation creates the immutable playback manifest and promotes the revision only if ownership/version still matches. Zero affected rows means the publish attempt is stale.

## Playback Contract

The Visual Novel Player receives only a `PlaybackManifest`. It performs no source analysis, model resolution, generation, approval, or timing inference.

For each segment it can deterministically restore:

- displayed text and reveal state;
- selected still/video and transition;
- active speech, ambience, effects, and music;
- audio positions and mix state;
- previous and next segment identifiers.

Back navigation stops media from the current segment and restores the target segment according to the manifest's restart/resume policy. That policy is persisted; the player does not guess.

## Failure Categories

- `SourceMissing`
- `SourceVersionStale`
- `AttributionAmbiguous`
- `MomentOrderInvalid`
- `TimingUnresolved`
- `CueOverlapInvalid`
- `ProductionBriefIncomplete`
- `CapabilityUnsupported`
- `AssetNotApproved`
- `AssetChecksumMismatch`
- `ContentPolicyConflict`
- `ContinuityConflict`
- `PublicationStale`
- `ManifestInvalid`

Failures carry stable codes, affected entity IDs, and actionable details. They never trigger hidden substitution.