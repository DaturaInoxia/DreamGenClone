# Feature Specification - Progressive Scene Beat, Moment, and Multimodal Production Pipeline

## User Story 1 - Generate a fast Beat Catalogue (P1)

As a production user, I can generate a compact list of narrative Beats from one authoritative roleplay turn so I can choose which development to prepare for image, audio, and video generation.

**Independent test:** Given a persisted turn with Narrative and character interactions, catalogue generation produces ordered selectable entries without creating any enrichment records.

**Acceptance scenarios:**

1. Given a valid authoritative turn and configured analyzer, when Generate Beats is selected, then one pending catalogue version and one durable catalogue job are created.
2. Given a completed catalogue, when it is displayed, then every entry shows order, label, concise narrative development, primary location, and participant summary.
3. Given missing Narrative synthesis, when generation is requested, then the request fails explicitly before a model call.
4. Given unsupported analyzer capabilities, when generation is requested, then the request fails with Model Manager guidance and no job is accepted.
5. Given malformed structured output, when the job completes, then the attempt is Failed with preserved raw response and diagnostics; no partial catalogue is promoted.

## User Story 2 - Enrich the selected Beat for multimodal production (P1)

As a user, I can turn one selected Beat into canonical provider-neutral story-production data containing everything downstream image, audio, and video preparation needs.

**Independent test:** Beat enrichment resolves exact dialogue/narration, ambience, sound events, ordered action, state boundaries, and video coverage from authoritative evidence without generating Moments or provider prompts.

**Acceptance scenarios:**

1. Given a selected current Beat, enrichment persists ordered source-supported events and start/end continuity.
2. Exact dialogue/narration spans match immutable RP source offsets and resolve speaker/addressee keys authoritatively.
3. Ambiguous speaker attribution is review-required and never silently assigned.
4. Ambience and discrete sound events include explicit Beat-relative/event anchors.
5. Video coverage explicitly identifies one-Moment, Moment-transition, Beat-excerpt, or whole-Beat scope, required key states, action arc, dialogue mapping, and audio ownership.
6. Output contains no image/audio/video provider prompt syntax.

## User Story 3 - Discover moments inside the selected beat (P1)

As a user, I can receive exact frozen states from inside the selected Beat Production Plan for still images, sound-event anchors, and video key states.

**Independent test:** Selecting a beat without a current moment set generates 2–4 compact moments for only that beat; selecting a beat with a completed current moment set reuses it.

**Acceptance scenarios:**

1. Given a completed current Beat Production Plan, when required moments do not exist, then Moment discovery is queued for that plan version.
2. Given moment discovery is pending, then the beat remains selected, progress is visible, and prompt generation is disabled.
3. Given Moment discovery completes, then each Moment identifies one exact frozen state, temporal anchor, visible action, participant summary, composition rationale, and production roles.
4. Given a moment set, then exactly one moment may be marked Recommended and Studio may preselect it without automatically rendering it.
5. Given the user selects another beat, then moments are generated only for that beat; prior moment sets remain immutable and reusable.
6. Given the catalogue has been replaced, then moment sets from the older catalogue cannot be used for new prompts.

## User Story 4 - Enrich only the selected Moment (P1)

As a user, I can select one Moment and expand it into the exact-state production contract needed by still-image and video-key-state generation.

**Independent test:** Selecting an unenriched moment creates exactly one enrichment; selecting a completed moment reuses it; sibling moments remain unenriched.

**Acceptance scenarios:**

1. Given a completed current moment set, when an unenriched moment is selected, then enrichment is queued for that moment-set version and moment ID.
2. Given enrichment is pending, then the moment remains selected, progress is visible, and prompt generation is disabled.
3. Given enrichment completes, then cast, clothing, positions, arrested actions, sightlines, location, lighting, environment, objects, instantaneous sound events, continuity, and video key-state roles are available to media compilers.
4. Given **Generate from suggested moment** is selected, then Studio selects the persisted recommended moment and follows the same enrichment path; it does not bypass or create an anonymous moment.
5. Given the moment set or parent catalogue has been replaced, then older enrichments cannot be used for new prompts.

## User Story 5 - Replace or retry work safely (P1)

As a user, I can replace a catalogue or retry failed work without an older background operation overwriting my newer choice.

**Independent test:** Two overlapping catalogue attempts complete in reverse order; only the current attempt can promote its result.

**Acceptance scenarios:**

1. Given a catalogue job is pending or processing, Generate Again is disabled unless the user explicitly cancels/supersedes it.
2. Given attempt A is superseded by attempt B, when A completes later, then A is marked Superseded and cannot mutate B.
3. Given a transient provider failure, then the durable job retries within its configured bounded policy.
4. Given schema, configuration, or semantic validation failure, then the job does not retry automatically.
5. Given application restart, then accepted queued/processing jobs are recovered or explicitly marked recoverable; records do not remain silently Pending forever.

## User Story 6 - Configure the analyzer independently (P1)

As an administrator, I can choose and tune the scene beat analyzer independently from roleplay prose generation and image prompt compilation.

**Independent test:** Changing the RP session prose-model override does not change the resolved scene beat analyzer.

**Acceptance scenarios:**

1. Model Manager exposes `RolePlaySceneBeatAnalyzer` with model, temperature, top-p, max output tokens, thinking mode, timeout source, and concurrency.
2. Registered text models declare structured-output and thinking-control capabilities.
3. Missing or incompatible configuration fails explicitly; no alternate model is selected.
4. Resolution provenance is stored on each attempt before execution.

## User Story 7 - Support media-model families without changing production data (P2)

As an administrator, I can register supported image, audio, and video model families and compiler dialects without changing Beat/Moment production schemas.

**Independent test:** The same production plan can be projected into still-image, speech/audio, and video semantic briefs while persisted Beat/Moment facts remain byte-for-byte unchanged.

**Acceptance scenarios:**

1. Media models carry explicit persisted kind, family, capabilities, and compiler-dialect metadata.
2. Compiler resolution uses that metadata, not model-name guessing.
3. Unknown family, unsupported input modality, or missing compiler fails before enqueue.
4. No compiler may rediscover missing story semantics from raw RP prose or use another media path as fallback.

## User Story 8 - Diagnose and measure every stage (P2)

As a developer or power user, I can see queue, model, attempt, latency, and validation information for catalogue, Beat production, Moment discovery, and Moment enrichment independently.

**Independent test:** A forced malformed enrichment response persists its exact request provenance, raw output, validation error category, and duration.

## User Story 9 - Compile one consistent multimodal production (P1)

As a production user, I can compile one Beat/Moment lineage into image, speech, sound, music, video, native-audio video, and lip-sync requests that describe the same scene and timing.

**Independent test:** A frozen golden lineage compiles through every representative request contract without rereading RP prose or mutating canonical records.

**Acceptance scenarios:**

1. Every compiled request retains the canonical IDs and immutable semantic snapshot used to build it.
2. Image and video keyframes preserve the same cast, appearance, wardrobe, location, props, frozen state, lighting, and camera intent.
3. TTS preserves authored dialogue while allowing a separately audited spoken-text normalization and returns realized alignment for downstream synchronization.
4. Video and lip-sync use the same dialogue cue, speaker, realized speech asset, and exact timeline window.
5. Native-video audio and external audio requests use the same dialogue, effect, ambience, and music cue ownership.
6. Unsupported required intent fails compatibility validation and is never silently omitted, guessed, or replaced.

## Functional Requirements

- **FR-001:** The system shall represent Beat Catalogue, Beat Production Plan, Beat dialogue/sound/video cues, Moment Set, Scene Moment, and Moment Enrichment as separate persisted resources.
- **FR-002:** A catalogue shall be immutable after promotion to Complete.
- **FR-003:** Every replacement shall create a new catalogue version and preserve prior versions for provenance.
- **FR-004:** Catalogue entries shall use stable IDs unique within their catalogue version.
- **FR-005:** Catalogue output shall describe narrative developments and shall not represent a beat as one frozen image.
- **FR-006:** Catalogue output shall reference turn evidence by compact indexes supplied by the application.
- **FR-007:** Application code shall resolve evidence indexes to authoritative interaction IDs and reject unknown indexes.
- **FR-008:** Beat production enrichment shall require a current completed catalogue and Beat belonging to it.
- **FR-009:** A Beat Production Plan shall include ordered events, exact dialogue/narration, ambience, sound events, action arc, start/end continuity, and video coverage.
- **FR-010:** Dialogue/narration spans and speaker/addressee references shall resolve against immutable authoritative source.
- **FR-011:** Ambiguous attribution shall be review-required and shall not be guessed.
- **FR-012:** Video coverage shall declare scope, event range, key-state roles, action arc, dialogue mapping, and audio ownership.
- **FR-013:** Beat Production Plans shall be provider-neutral and contain no media-model syntax.
- **FR-014:** Moment discovery shall require a current completed Beat Production Plan.
- **FR-015:** A Moment set shall contain 2–4 compact candidates unless an explicit bounded contract requires another range.
- **FR-016:** Every Moment shall represent exactly one frozen state and declare production roles.
- **FR-017:** A Moment set may identify exactly one persisted recommended still-image Moment.
- **FR-018:** Moment enrichment shall require a current completed Moment set and Moment belonging to it.
- **FR-019:** Moment enrichment shall produce the complete provider-neutral frozen-state, instantaneous-sound, and video-key-state contract.
- **FR-020:** Still-image generation shall require current Moment enrichment.
- **FR-021:** Audio generation shall require current Beat cues and all referenced Moment anchors.
- **FR-022:** Video generation shall require a current Beat Production Plan, coverage plan, and all mandated enriched Moment key states.
- **FR-023:** Every media derivative shall retain Turn, Catalogue, Beat Plan, Moment Set, and Moment lineage where applicable.
- **FR-024:** Selecting any completed current analysis stage shall not issue another model request.
- **FR-025:** Concurrent requests shall dedupe by their exact parent version and target identity.
- **FR-026:** All analysis-stage promotion shall use compare-and-set persistence.
- **FR-027:** Stale attempts shall never overwrite current resources.
- **FR-028:** Accepted jobs shall be persisted before enqueue acknowledgement and support lease recovery.
- **FR-029:** Retry policy shall distinguish transient failures from permanent configuration/schema/semantic failures and use only persisted UI-backed policy.
- **FR-030:** All four analysis stages shall resolve only from `RolePlaySceneBeatAnalyzer` configuration and separate versioned contracts.
- **FR-031:** Required structured-output capability and thinking mode shall be validated and snapshotted before acceptance.
- **FR-032:** Exact prompt/schema/model/settings/attempt/finish/timing provenance shall be auditable.
- **FR-033:** Raw model response/reasoning retention shall use configured policy.
- **FR-034:** Media-family selection shall use persisted capabilities and exact compiler registries.
- **FR-035:** Unknown or incompatible image/audio/video families shall fail explicitly.
- **FR-036:** Existing schema-v3 analyses shall remain viewable and shall not be guessed into multimodal production records.
- **FR-037:** Default work shall remain progressive; future batch commands must be explicit and bounded.
- **FR-038:** Studio shall expose distinct Catalogue, Beat Production, Moment Discovery, and Moment Enrichment states/errors.
- **FR-039:** Every canonical production field shall have a documented semantic purpose and representative media consumer in the provider evidence matrix.
- **FR-040:** The Beat plan shall use one explicit Beat-relative timebase with typed start/end windows for events, dialogue, ambience, effects, music, video coverage, and lip-sync.
- **FR-041:** Frame indexes shall be derived from canonical time only after a target video FPS is selected; frames shall not be the sole canonical time representation.
- **FR-042:** Dialogue cues shall preserve immutable display/source text separately from normalized spoken text and normalization provenance.
- **FR-043:** Generated speech derivatives shall persist realized duration and character/word alignment when the provider supplies it.
- **FR-044:** Canonical media references shall declare a typed role and lineage; untyped reference collections are invalid.
- **FR-045:** Music plans shall support ordered duration-bearing sections, entry/exit/transition intent, instrumentation, tempo/key when authored, and lyric/instrumental ownership.
- **FR-046:** Video coverage shall identify exact visual start/end/internal states, permitted action phases, camera/motion intent, and per-cue audio ownership.
- **FR-047:** Lip-sync plans shall identify the approved visual source role, realized speech source, target character, exact video/audio windows, face-visibility requirement, performance scope, and duration-fit policy.
- **FR-048:** Native-video audio shall compile from the same canonical cue IDs used by external speech, effects, ambience, and music generation.
- **FR-049:** Compiler output shall report every unsupported required canonical field before media work is accepted.
- **FR-050:** A golden lineage shall compile through every representative modality contract and pass cross-modal consistency assertions before schema freeze.

## Non-Functional Requirements

- **NFR-001:** Catalogue latency target: p50 <= 15 seconds and p95 <= 45 seconds on the frozen acceptance corpus and configured acceptance model.
- **NFR-002:** Beat-production latency target: p50 <= 20 seconds and p95 <= 60 seconds.
- **NFR-003:** Moment-discovery latency target: p50 <= 10 seconds and p95 <= 30 seconds.
- **NFR-004:** Moment-enrichment latency target: p50 <= 20 seconds and p95 <= 60 seconds.
- **NFR-005:** Structured response validity shall be at least 99% for each stage across the frozen corpus before rollout.
- **NFR-006:** Queue state shall survive application restart.
- **NFR-007:** Text analysis shall not block image, audio, or video generation lanes.
- **NFR-008:** All state transitions shall be monotonic and concurrency-tested.
- **NFR-009:** No hidden defaults, fallback models, schema repair, semantic rediscovery, or prompt-only downgrade are permitted.
- **NFR-010:** Stored diagnostics shall have configurable retention to control database growth.

## Out of Scope

- Character identity conditioning and identity-pack authoring.
- Three-dimensional blocking and camera editor implementation.
- Image validation and bounded repair implementation.
- Image, speech, sound, music, lip-sync, and video generator implementation.
- Selecting production providers or treating documentation-only fixtures as live provider qualification.
- Storyboard sequencing, asset placement, publication, and Visual Novel playback.
- Automatic rendering of the recommended moment without user confirmation.
- Automatic moment generation for every beat or enrichment of every moment in normal operation.
