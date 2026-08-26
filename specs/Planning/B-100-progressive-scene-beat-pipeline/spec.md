# Feature Specification - Progressive Scene Beat Pipeline

## User Story 1 - Generate a fast Beat Catalogue (P1)

As a Scene Image Studio user, I can generate a compact list of image-worthy moments from one authoritative roleplay turn so I can choose what is worth rendering without waiting for every candidate to be fully described.

**Independent test:** Given a persisted turn with Narrative and character interactions, catalogue generation produces ordered selectable entries without creating any enrichment records.

**Acceptance scenarios:**

1. Given a valid authoritative turn and configured analyzer, when Generate Beats is selected, then one pending catalogue version and one durable catalogue job are created.
2. Given a completed catalogue, when it is displayed, then every entry shows order, label, concise frozen moment, primary location, and participant summary.
3. Given missing Narrative synthesis, when generation is requested, then the request fails explicitly before a model call.
4. Given unsupported analyzer capabilities, when generation is requested, then the request fails with Model Manager guidance and no job is accepted.
5. Given malformed structured output, when the job completes, then the attempt is Failed with preserved raw response and diagnostics; no partial catalogue is promoted.

## User Story 2 - Enrich only the selected beat (P1)

As a user, I can select a catalogue beat and have only that beat expanded into a render-ready visual contract so unused candidates do not consume model time or tokens.

**Independent test:** Selecting an unenriched entry creates exactly one enrichment; selecting a completed entry reuses it; no sibling entry is enriched.

**Acceptance scenarios:**

1. Given a completed current catalogue, when an unenriched beat is selected, then an enrichment is queued for that catalogue version and beat ID.
2. Given enrichment is pending, then its beat remains selected, progress is visible, and prompt generation is disabled.
3. Given enrichment completes, then cast involvement, clothing, positions, sightlines, visibility, location, lighting, environment, mood, and evidence are available to prompt generation.
4. Given the user selects another beat, then only that beat is enriched; the prior enrichment remains immutable and reusable.
5. Given the catalogue has been replaced, then enrichments from the older catalogue cannot be used for new prompts.

## User Story 3 - Replace or retry work safely (P1)

As a user, I can replace a catalogue or retry failed work without an older background operation overwriting my newer choice.

**Independent test:** Two overlapping catalogue attempts complete in reverse order; only the current attempt can promote its result.

**Acceptance scenarios:**

1. Given a catalogue job is pending or processing, Generate Again is disabled unless the user explicitly cancels/supersedes it.
2. Given attempt A is superseded by attempt B, when A completes later, then A is marked Superseded and cannot mutate B.
3. Given a transient provider failure, then the durable job retries within its configured bounded policy.
4. Given schema, configuration, or semantic validation failure, then the job does not retry automatically.
5. Given application restart, then accepted queued/processing jobs are recovered or explicitly marked recoverable; records do not remain silently Pending forever.

## User Story 4 - Configure the analyzer independently (P1)

As an administrator, I can choose and tune the scene beat analyzer independently from roleplay prose generation and image prompt compilation.

**Independent test:** Changing the RP session prose-model override does not change the resolved scene beat analyzer.

**Acceptance scenarios:**

1. Model Manager exposes `RolePlaySceneBeatAnalyzer` with model, temperature, top-p, max output tokens, thinking mode, timeout source, and concurrency.
2. Registered text models declare structured-output and thinking-control capabilities.
3. Missing or incompatible configuration fails explicitly; no alternate model is selected.
4. Resolution provenance is stored on each attempt before execution.

## User Story 5 - Support multiple image-model families without changing beats (P2)

As an administrator, I can register a supported image-model family and prompt dialect without changing catalogue or enrichment schemas.

**Independent test:** The same enriched beat can be compiled by two registered compiler strategies while the persisted enrichment remains byte-for-byte unchanged.

**Acceptance scenarios:**

1. Image models carry explicit persisted family and prompt-dialect metadata.
2. Prompt compiler resolution uses that metadata, not checkpoint filename guessing.
3. Unknown family or missing compiler fails before render enqueue.
4. There is no fallback to Pony, SDXL, or prompt-only behavior.

## User Story 6 - Diagnose and measure every stage (P2)

As a developer or power user, I can see queue, model, attempt, latency, and validation information for catalogue and enrichment independently.

**Independent test:** A forced malformed enrichment response persists its exact request provenance, raw output, validation error category, and duration.

## Functional Requirements

- **FR-001:** The system shall represent Beat Catalogue and Beat Enrichment as separate persisted resources.
- **FR-002:** A catalogue shall be immutable after promotion to Complete.
- **FR-003:** Every replacement shall create a new catalogue version and preserve prior versions for provenance.
- **FR-004:** Catalogue entries shall use stable IDs unique within their catalogue version.
- **FR-005:** Catalogue output shall contain only selection-level fields defined by the contract.
- **FR-006:** Catalogue output shall reference turn evidence by compact indexes supplied by the application.
- **FR-007:** Application code shall resolve evidence indexes to authoritative interaction IDs and reject unknown indexes.
- **FR-008:** Enrichment shall require a current completed catalogue and a beat ID belonging to it.
- **FR-009:** Enrichment shall produce the complete image-family-neutral visual contract.
- **FR-010:** Prompt generation shall require a completed enrichment for the current catalogue version.
- **FR-011:** Selecting a completed current enrichment shall not issue another model request.
- **FR-012:** Concurrent requests for the same catalogue beat shall dedupe by catalogue-version ID plus beat ID.
- **FR-013:** Catalogue promotion and enrichment promotion shall use compare-and-set persistence.
- **FR-014:** Stale attempts shall never overwrite current resources.
- **FR-015:** Accepted jobs shall be persisted before enqueue acknowledgement.
- **FR-016:** Durable jobs shall support lease expiration and restart recovery.
- **FR-017:** Retry policy shall distinguish transient transport/provider failures from permanent configuration/schema/semantic failures.
- **FR-018:** Retry count and delays shall be persisted UI-backed configuration; no hardcoded runtime fallback policy is allowed.
- **FR-019:** Beat analysis shall resolve only from `RolePlaySceneBeatAnalyzer` configuration.
- **FR-020:** Required structured-output capability shall be validated before job acceptance.
- **FR-021:** Thinking mode shall be explicit in resolved configuration and persisted provenance.
- **FR-022:** Exact system prompt, user prompt, prompt-contract version, model/provider, sampling parameters, output limits, attempt count, finish reason, and timing shall be auditable.
- **FR-023:** Raw model response and reasoning may be retained under a configured diagnostics retention policy.
- **FR-024:** Image-family selection shall use persisted model metadata and one compiler registry.
- **FR-025:** Unknown or incompatible image families shall fail explicitly.
- **FR-026:** Existing completed schema-v3 beat analyses shall remain viewable during migration.
- **FR-027:** Legacy records shall not be silently converted by guessed field values.
- **FR-028:** The default workflow shall not enrich all catalogue entries.
- **FR-029:** Any future batch-enrichment command shall be explicit, bounded, and separately observable.
- **FR-030:** Studio shall expose distinct catalogue and selected-beat enrichment states and errors.

## Non-Functional Requirements

- **NFR-001:** Catalogue latency target: p50 <= 15 seconds and p95 <= 45 seconds on the frozen acceptance corpus and configured acceptance model.
- **NFR-002:** Enrichment latency target: p50 <= 20 seconds and p95 <= 60 seconds.
- **NFR-003:** Structured response validity shall be at least 99% across the frozen corpus before rollout.
- **NFR-004:** Queue state shall survive application restart.
- **NFR-005:** Text analysis shall not block the image-render execution lane.
- **NFR-006:** All state transitions shall be monotonic and concurrency-tested.
- **NFR-007:** No hidden defaults, fallback models, schema repair, or prompt-only downgrade are permitted.
- **NFR-008:** Stored diagnostics shall have configurable retention to control database growth.

## Out of Scope

- Character identity conditioning and identity-pack authoring.
- Three-dimensional blocking and camera editor implementation.
- Image validation and bounded repair implementation.
- Automatic selection of the "best" beat without user confirmation.
- Automatic enrichment of every entry in normal operation.
