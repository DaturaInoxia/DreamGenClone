# Progressive Beat Pipeline Contract

## Service Surface

Illustrative signatures; exact namespaces follow existing scene-image ownership boundaries.

```csharp
public interface ISceneBeatPipelineService
{
    Task<SceneBeatCatalogue> EnqueueCatalogueAsync(
        GenerateSceneBeatCatalogueRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneBeatCatalogue?> GetCurrentCatalogueAsync(
        string sessionId,
        string turnId,
        CancellationToken cancellationToken = default);

    Task<SceneBeatProductionPlan> EnqueueBeatProductionPlanAsync(
      EnrichSceneBeatProductionRequest request,
      CancellationToken cancellationToken = default);

    Task<SceneBeatProductionPlan?> GetCurrentBeatProductionPlanAsync(
      string catalogueId,
      string beatId,
      CancellationToken cancellationToken = default);

    Task<SceneMomentSet> EnqueueMomentDiscoveryAsync(
      GenerateSceneMomentsRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneMomentSet?> GetCurrentMomentSetAsync(
        string catalogueId,
        string beatId,
        CancellationToken cancellationToken = default);

    Task<SceneMomentEnrichment> EnqueueMomentEnrichmentAsync(
      EnrichSceneMomentRequest request,
      CancellationToken cancellationToken = default);

    Task<SceneMomentEnrichment?> GetCurrentMomentEnrichmentAsync(
      string momentSetId,
      string momentId,
      CancellationToken cancellationToken = default);

    Task CancelCatalogueAsync(
        string catalogueId,
        CancellationToken cancellationToken = default);
}
```

Requests contain stable IDs only. The service resolves and snapshots authoritative state before accepting a durable job.

## Catalogue Output Contract

The catalogue response is intentionally compact:

```json
{
  "schemaVersion": 1,
  "beats": [
    {
      "beatId": "b1",
      "order": 1,
      "label": "Arrival at the doorway",
      "beatSynopsis": "Becky enters through the doorway, pauses, and draws Dean's attention.",
      "primaryLocation": "entry hall",
      "participants": [
        { "name": "Becky", "involvement": "active" },
        { "name": "Dean", "involvement": "observer" }
      ],
      "evidenceKeys": ["n0", "c1"]
    }
  ]
}
```

Catalogue output explicitly excludes:

- profile IDs;
- full clothing descriptions;
- precise positions and sightlines;
- visible-character matrices;
- detailed environment and lighting;
- image-family tags or prompts;
- provider workflow settings.

A beat may describe a short progression through time. It is never passed directly to prompt compilation as if it were one frozen frame.

Validation rules:

- 1 to configured maximum catalogue entries;
- unique positive order and unique beat ID;
- concise bounded strings;
- at least one active participant;
- known participant names only;
- known evidence keys only;
- Narrative evidence key required for every entry;
- one atomic primary location.

Application code resolves evidence keys to authoritative interaction IDs. Unknown keys fail the attempt; they are never dropped or guessed.

## Beat Production Output Contract

Beat enrichment receives one immutable Beat snapshot, the authoritative RP Turn and cited interactions, and relevant character/location state. It returns provider-neutral temporal production data:

```json
{
  "schemaVersion": 1,
  "catalogueBeatId": "b1",
  "events": [
    { "eventKey": "e1", "order": 1, "description": "Becky crosses the threshold", "evidenceKeys": ["n0", "c1"] },
    { "eventKey": "e2", "order": 2, "description": "Becky and Dean exchange a look", "evidenceKeys": ["n0", "c1"] }
  ],
  "dialogue": [
    {
      "cueKey": "d1",
      "eventKey": "e2",
      "exactSourceText": "You're still awake.",
      "sourceKey": "c1",
      "startOffset": 42,
      "endOffset": 61,
      "speakerKey": "p0",
      "addresseeKeys": ["p1"],
      "deliveryIntent": "quiet surprise",
      "lipSyncRelevant": true
    }
  ],
  "ambience": {
    "location": "entry hall",
    "timeContext": "evening",
    "soundSources": ["low household room tone", "rain beyond the open door"],
    "continuityIntent": "continue through beat"
  },
  "soundEvents": [
    { "cueKey": "s1", "eventKey": "e1", "description": "shoe lands on the wooden floor", "diegetic": true }
  ],
  "actionArc": [
    { "order": 1, "subjectKey": "p0", "action": "crosses", "target": "threshold" },
    { "order": 2, "subjectKey": "p0", "action": "stops and looks toward", "targetKey": "p1" }
  ],
  "startContinuity": { "location": "outside doorway" },
  "endContinuity": { "location": "inside entry hall" },
  "videoCoverage": [
    {
      "coverageKey": "v1",
      "kind": "MomentTransition",
      "eventKeys": ["e1", "e2"],
      "requiredMomentRoles": ["start", "end"],
      "dialogueCueKeys": ["d1"],
      "audioPolicyIntent": "ExternalMix"
    }
  ]
}
```

Validation rules:

- exact source text and offsets match the immutable source snapshot;
- speaker/addressee/profile and evidence keys resolve authoritatively;
- ambiguous attribution returns `ReviewRequired`, never an invented speaker;
- events and action arc are ordered and source-supported;
- ambience state is explicit;
- sound events identify an event or valid Beat-relative anchor;
- video coverage has kind-specific source events, continuity boundaries, key-state roles, dialogue cue mapping, and audio ownership intent.

## Moment Discovery Output Contract

Moment discovery receives one immutable Beat Production Plan plus its authoritative evidence. It returns 2–4 compact frozen states and resolves key-state roles requested by video/audio coverage:

```json
{
  "schemaVersion": 1,
  "catalogueBeatId": "b1",
  "recommendedMomentId": "m2",
  "moments": [
    {
      "momentId": "m1",
      "order": 1,
      "label": "Crossing the threshold",
      "temporalAnchor": "the instant Becky's leading foot lands inside",
      "frozenState": "Becky is mid-step through the open doorway while Dean remains seated beyond her.",
      "visibleAction": "crossing the threshold",
      "participants": [
        { "name": "Becky", "involvement": "active" },
        { "name": "Dean", "involvement": "observer" }
      ],
      "compositionRationale": "The doorway frames Becky and preserves Dean's reaction in depth.",
      "evidenceKeys": ["n0", "c1"]
    },
    {
      "momentId": "m2",
      "order": 2,
      "label": "The exchanged look",
      "temporalAnchor": "immediately after Becky stops",
      "frozenState": "Becky stands just inside the doorway and meets Dean's raised gaze.",
      "visibleAction": "holding eye contact",
      "participants": [
        { "name": "Becky", "involvement": "active" },
        { "name": "Dean", "involvement": "active" }
      ],
      "compositionRationale": "The shared sightline gives the scene a clear emotional center.",
      "productionRoles": ["StillCandidate", "VideoEnd"],
      "evidenceKeys": ["n0", "c1"]
    }
  ]
}
```

Validation rules:

- 2–4 moments with unique IDs and positive order;
- exactly one `recommendedMomentId`, matching a returned moment;
- one exact temporal anchor and frozen state per moment;
- no sequential before/after action or montage inside a moment;
- known participant names and evidence keys only;
- each moment belongs semantically to its selected parent Beat and matches any assigned production role.

## Moment Enrichment Output Contract

Enrichment receives one immutable selected-Moment snapshot, its parent Beat Production Plan, and only the authoritative evidence needed for that state. It returns the detailed neutral frozen-state contract:

```json
{
  "schemaVersion": 1,
  "catalogueBeatId": "b1",
  "momentId": "m2",
  "visualDescription": "A complete description of one frozen moment.",
  "characters": [
    {
      "name": "Becky",
      "profileKey": "p0",
      "involvement": "active",
      "physicalLocation": "entry hall",
      "position": "just inside the doorway",
      "actionOrObservation": "pauses with one hand on the door",
      "sightline": "toward Dean",
      "visibleCharacterNames": ["Dean"],
      "clothing": "blue shirt"
    }
  ],
  "location": "entry hall",
  "timeOfDay": "evening",
  "lighting": "warm ceiling light",
  "environment": "narrow entry hall with the open door behind Becky",
  "mood": "expectant",
  "instantaneousSoundCueKeys": ["s1"],
  "videoKeyState": { "roles": ["VideoEnd"], "stateChangeAllowed": false }
}
```

Profile/cue keys are resolved to authoritative IDs by application code. Enrichment remains independent of image, audio, video, or provider dialect. Every derivative retains Beat-plan and Moment lineage where applicable.

## Structured Output Contract

- All four stages require a registered analyzer with an explicit supported structured-output mode: `StrictJsonSchema` or `JsonObject`.
- `StrictJsonSchema` transport sends the exact versioned schema as provider-enforced response metadata.
- `JsonObject` transport sends `response_format.type=json_object` and includes the exact versioned schema in the system instruction. The same strict parser and semantic validator remain authoritative after completion.
- Model capability validation happens before durable job acceptance.
- Schema parsing uses structured APIs.
- No JSON repair, control-character sanitizer, inferred missing fields, alternate root names, or reasoning-as-content substitution is allowed.
- Schema-valid but semantically invalid output fails with a stable validation code.

## Model Resolution Contract

- Source: `AppFunction.RolePlaySceneBeatAnalyzer` only.
- A roleplay session `SessionModelId` is not an analyzer override.
- Missing function default, disabled model/provider, missing/unsupported structured-output mode, incompatible configured token limits, or unspecified required thinking mode fails explicitly.
- Function `MaxTokens` is the required output limit. Optional model context/output capabilities constrain it when configured; their absence does not create a fallback value.
- No model fallback or alternate provider path is allowed.
- The resolved model and execution settings are snapshotted before the job is accepted.

## Job Payloads

```csharp
public sealed record SceneBeatCatalogueJobPayload(
    string CatalogueId,
    string AttemptId);

public sealed record SceneBeatProductionPlanJobPayload(
  string BeatProductionPlanId,
  string AttemptId);

public sealed record SceneMomentDiscoveryJobPayload(
  string MomentSetId,
  string AttemptId);

public sealed record SceneMomentEnrichmentJobPayload(
    string EnrichmentId,
    string AttemptId);
```

Handlers reload only the immutable snapshot and current ownership metadata. Payloads do not embed prompt text, session blobs, model secrets, or response schemas.

## Concurrency Contract

Catalogue completion uses compare-and-set semantics equivalent to:

```sql
UPDATE SceneBeatCatalogues
SET Status = 'Complete', CompletedUtc = $now
WHERE Id = $catalogueId
  AND CurrentAttemptId = $attemptId
  AND Status IN ('Pending', 'Processing');
```

The update must affect exactly one row. Zero rows means the attempt is stale, cancelled, or already terminal. The attempt transitions to `Superseded` or observes idempotent completion; it must not upsert or replace the current catalogue.

The same rule applies to Beat production enrichment, Moment discovery, and Moment enrichment.

Dedupe keys:

- Catalogue attempt: `scene-beat-catalogue:{catalogueId}:{attemptId}`.
- Current Beat-production request: `scene-beat-production:{catalogueId}:{beatId}:{version}`.
- Current moment-discovery request: `scene-moments:{beatProductionPlanId}:{version}`.
- Current moment-enrichment request: `scene-moment-enrichment:{momentSetId}:{momentId}:{revision}`.

## Durable Queue Contract

- Persist job before returning success to the UI.
- Claim jobs transactionally with a worker ID and expiring lease.
- Recover expired Processing leases on startup.
- Separate lanes prevent long text analysis from blocking image rendering.
- Lane concurrency is required persisted configuration.
- Cancellation is a persisted state transition checked before and after model execution.

Retry categories:

| Category | Automatic retry |
|---|---|
| Timeout, connection reset, HTTP 429, eligible HTTP 5xx | Yes, within configured policy |
| Missing/invalid configuration | No |
| Unsupported capability | No |
| JSON Schema violation | No by default; explicit regeneration is required |
| Semantic validation failure | No by default |
| Superseded/cancelled attempt | No |
| Application shutdown cancellation | Recover through lease, not counted as a model failure unless request outcome is known |

Retry attempts reuse the immutable input and resolved model snapshot. They do not silently resolve a newer model.

## UI Contract

Catalogue states:

- `None`: Generate Beats command.
- `Pending`/`Processing`: progress, elapsed time, cancel command; Generate Again disabled.
- `Complete`: selectable catalogue; explicit Generate Again confirmation.
- `Failed`: stable error category, details, explicit Retry.
- `Superseded`/`Cancelled`: historical only.

Beat-selection states:

- No production plan: selecting queues Beat production enrichment.
- Pending production: selected Beat remains visible with progress; media controls disabled.
- Complete production: dialogue, soundscape, action, and video coverage are reviewable; Moment discovery queues or loads.
- Pending discovery: selected Beat remains visible with progress; media controls remain disabled.
- Complete discovery: 2–4 moment choices are visible and the persisted recommendation is preselected.
- Failed discovery: catalogue remains usable; retry applies only to the selected beat.

Moment-selection states:

- No enrichment: selecting queues moment enrichment.
- Pending enrichment: selected moment remains visible with progress; prompt controls disabled.
- Complete enrichment: details and prompt controls are enabled.
- Failed enrichment: moment choices remain usable; retry applies only to the selected moment.
- **Generate from suggested moment** selects the persisted recommendation and uses the same enrichment path.

## Image-Family Compiler Contract

```csharp
public interface ISceneImagePromptCompiler
{
    string FamilyKey { get; }
    string PromptDialect { get; }

    SceneImagePromptCompilation Build(
        SceneMomentVisualContract moment,
        SceneImageCompilationContext context);
}
```

Compiler resolution uses explicit registered-image-model metadata. Exactly one compiler must match. Zero or multiple matches fail explicitly. Catalogue/moment/enrichment code never switches on an image family.

## Multimodal Compiler Input Contract

- Still-image compilation requires one current complete Moment enrichment.
- Speech compilation requires one reviewed dialogue/narration cue with immutable display text, separately normalized spoken text, speaker/voice-performance identity, language, pronunciation intent, continuity context, and requested window.
- Ambience/effect compilation requires reviewed typed Beat sound cues, exact windows, duration and loop/stem intent, spatial/diegetic source, and continuity boundaries.
- Music compilation requires ordered duration-bearing sections, global and local musical intent, transitions, lyric/instrumental ownership, and typed conditioning references where present.
- Video compilation requires one current Beat Production Plan, one coverage plan, and every required enriched Moment key state.
- Video compilation projects start/end/internal visual states, permitted action phases, camera/lens/motion/pacing intent, coverage duration, and typed source/reference media.
- Video-with-audio compilation additionally requires exact dialogue cue mapping, speaker attribution, line windows, ambience/effect/music ownership, and explicit `ExternalMix`, `GeneratedWithVideo`, `Hybrid`, or `None` policy per cue class.
- Lip-sync/performance compilation requires an approved visual derivative, an approved realized speech derivative, exact video and audio crop windows, target character, face-visibility/speaker-selection intent, duration-fit policy, and optional expression/head-motion scope.
- Compilers convert canonical time to provider frame indexes only after target FPS is known.
- Every compiler emits a required-intent coverage report. Unsupported required intent fails before enqueue; unsupported optional intent is reported explicitly.
- No compiler may re-read raw RP prose to invent missing production semantics.

### Compiler Projection Shapes

| Projection | Required semantic fields |
|---|---|
| `StillImageCompilationInput` | Moment frozen state; stable cast/appearance/wardrobe; location/props; pose/visibility; lighting/mood; composition/camera; typed identity/continuity/style references |
| `SpeechCompilationInput` | Cue/source/display/spoken text; speaker and voice role; language; performance; pronunciation; pause/overlap; previous/next cue context; requested window |
| `SoundCompilationInput` | Cue kind; ordered event description; exact window; duration; source/spatial/diegetic state; intensity envelope; loop/stem/continuity intent |
| `MusicCompilationInput` | Global intent; BPM/key when authored; instrumentation; ordered sections and durations; transitions; lyrics/instrumental state; conditioning reference roles |
| `VideoCompilationInput` | Coverage kind/window; start/end/internal Moment states; action phases; subject/location continuity; camera/lens/movement/pacing; references; linked audio cue IDs and ownership |
| `LipSyncCompilationInput` | Approved visual and speech asset IDs; target character; segment and crop windows; face visibility/selection intent; duration-fit policy; expression/head scope |

### Realized Alignment Contract

Speech/video/audio generation imports actual output duration and provider timing metadata without changing canonical intent. Character or word intervals are persisted when available. Lip-sync, captions, mix, and B-101 placement bind to the approved derivative alignment version, not estimated text duration.

### Golden Consistency Contract

Before schema freeze, one immutable fixture must compile through Pony, SDXL/Juggernaut, a FLUX-like structured image projection, TTS, ambience/effects, music, all five video coverage kinds, native-audio video, and lip-sync/performance. Assertions compare canonical IDs and normalized semantic fields for identity, appearance, wardrobe, location, props, frozen state, action order, dialogue, speaker, emotion, timing, camera, ambience, effects, and music. Compiler snapshots may differ in provider syntax but may not contradict these fields.

## Observability Contract

Each stage records:

- queued, started, completed/failed/superseded timestamps;
- queue wait and execution duration;
- prompt and schema versions;
- input/output character or token counts when available;
- resolved provider/model/settings;
- thinking mode and structured-output mode;
- finish reason;
- retry count and last transient failure;
- schema/semantic validation category;
- raw output and optional reasoning under retention policy.

Success metrics must be queryable separately for catalogue, Beat production enrichment, Moment discovery, and Moment enrichment.
