# B-101 Specification - Story Presentation Timeline and Storyboard

## User Story 1 - Create a presentation from RP story text (P1)

As an author, I can select authoritative RP turns and create a versioned story presentation without changing the roleplay session.

**Independent test:** Importing three current B-100 Beat Production Plans creates an ordered draft with exact production/cue/Moment versions and checksums; RP and B-100 records remain unchanged.

**Acceptance scenarios:**

1. Given selected turns, import preserves their order, exact source text, interaction IDs, and checksums.
2. Given current B-100 production data, imported anchors retain Catalogue, Beat Production Plan, dialogue/sound/video cue, Moment-set, and Moment-enrichment lineage.
3. Given missing required Beat production, audio/video cues, or Moment analysis, import reports a typed prerequisite state rather than deriving it in B-101.
4. Given source changes after import, the draft shows a source-version conflict and requires explicit refresh review.

## User Story 2 - Edit a linear storyboard (P1)

As an author, I can organize imported story material into ordered segments that define exactly what Back and Forward mean in the Visual Novel.

**Independent test:** Split, merge, and reorder segments in a draft; preview follows the resulting deterministic order; the published predecessor is unchanged.

**Acceptance scenarios:**

1. Every segment declares duration, visual, audio, and advance policies.
2. A segment may show a new still, hold the previous still, show video, or intentionally show text only.
3. A segment may contain multiple text and audio cues with explicit relative timing.
4. Conflicting concurrent edits fail by concurrency token instead of overwriting one another.

## User Story 3 - Preserve dialogue and narration correctly (P1)

As an author, I can review and place B-100's exact dialogue/narration production cues before speech generation so the correct character says the correct words.

**Independent test:** Ambiguous dialogue remains review-required and cannot create a speech-ready brief; reviewed dialogue retains source and edited performance text.

**Acceptance scenarios:**

1. Every Dialogue/Thought placement references one B-100 cue with resolved speaker attribution.
2. Source text is immutable while display and spoken projections are editable with provenance.
3. Speech cues declare voice identity, delivery, timing, and lip-sync requirements.
4. Missing voice identity or ambiguous speaker fails readiness explicitly.

## User Story 4 - Design environmental audio (P1)

As an author, I can place B-100's persistent ambience and exact sound-event cues so audio generation and VN mixing have specific, timed inputs.

**Independent test:** A room ambience continues across two segments, crossfades on a location change, and a door slam aligns to its source Moment without hidden continuation.

**Acceptance scenarios:**

1. Ambience declares location, time, sources, intensity, loop intent, and continuity boundary.
2. Effects declare source event, subject/object, temporal relation, on-screen state, and spatial intent.
3. Silence and continued ambience are explicit authored states.
4. Location/time contradiction creates a review finding before generation or publication.

## User Story 5 - Specify still and video coverage (P1)

As an author, I can select B-100 still/key-state and video coverage plans for a segment, request generation, and place approved results.

**Independent test:** Each video coverage kind rejects incomplete or cross-lineage sources and creates a provider-neutral production selection when complete.

**Acceptance scenarios:**

1. A new still references one source Moment and an approved B-032 frame before publication.
2. A Moment-transition placement references a B-100 plan with ordered enriched start/end Moments and complete continuity states.
3. Beat-excerpt/whole-Beat placement references a B-100 action arc and required key Moments.
4. Dialogue-bearing video preserves B-100 speech cue mappings and lip-sync requirements.
5. No provider prompt or workflow syntax is required to author a valid production selection.

## User Story 6 - Request and select independently approved media (P2)

As an author, I can request multiple media takes through the owning generator and select one of its approved derivatives without changing story or timeline metadata.

**Independent test:** Two candidates exist in an owning generator; B-101 can place only the derivative that generator reports as approved and eligible, while rejection or regeneration does not affect the source cue.

**Acceptance scenarios:**

1. B-101 submits an exact B-100 production selection through the owning generator contract.
2. B-101 reads candidate and provenance state from the owning generator without duplicating it.
3. Generation completion alone is not eligible for placement.
4. Still-image placement reuses B-032 `ApprovedSceneFrame` authority rather than creating another image-approval path.

## User Story 7 - Validate and publish a presentation (P1)

As an author, I can publish an immutable revision only when its story, timing, media, and continuity dependencies are complete.

**Independent test:** Publication rejects one unresolved attribution and one missing required asset, then succeeds after correction and emits a stable checksummed manifest.

**Acceptance scenarios:**

1. Publication validates source lineage, contiguous ordering, cue timing, explicit media states, approved assets, checksums, and content policy.
2. A stale concurrent publish affects no rows and returns `PublicationStale`.
3. Successful publication does not mutate the revision or approved assets.
4. Later editing forks a new draft; the published revision remains playable.

## User Story 8 - Play the Visual Novel deterministically (P1)

As a viewer, I can move Back and Forward through text, images/video, and synchronized audio with the same state restored every time.

**Independent test:** Navigate forward through three segments and backward twice; each target restores its exact text, media, audio, transition, and restart/resume state without model calls.

**Acceptance scenarios:**

1. Player consumes only an immutable playback manifest.
2. Back/Forward order is stable and bounded.
3. Navigating stops or restores media according to persisted policy.
4. Player does not infer missing assets, speakers, durations, or transitions.
5. Reader-advanced segments remain until input even after finite audio/video ends.

## Functional Requirements

- **FR-001:** Story presentations shall reference one RP session without mutating it.
- **FR-002:** Presentations shall support immutable published revisions and concurrency-protected drafts.
- **FR-003:** V1 playback shall be a linear ordered sequence of presentation segments.
- **FR-004:** A segment shall be the Back/Forward unit and own duration, visual, audio, and advance policy.
- **FR-005:** Every segment/placement shall retain exact B-100 production lineage and checksums.
- **FR-006:** Refreshing source analysis shall require explicit diff review.
- **FR-007:** Text cues shall preserve immutable source and editable display/spoken projections.
- **FR-008:** Dialogue/thought placements shall require B-100 cues with explicit speaker attribution.
- **FR-009:** Unresolved B-100 attribution shall block dependent generation and publication.
- **FR-010:** Speech placement shall preserve B-100 exact words/delivery/lip-sync data and add Voice Identity Version and presentation timing.
- **FR-011:** B-100 ambience, sound effects, and music intents shall remain separate typed source cues and B-101 placements.
- **FR-012:** Ambience continuation, silence, and replacement shall be explicit states.
- **FR-013:** Sound-effect placement shall retain B-100 authoritative event and temporal anchors.
- **FR-014:** Visual mode shall be exactly one of NewStill, HoldPreviousStill, Video, or TextOnly.
- **FR-015:** New stills shall reference one source Moment and an eligible B-032 approved frame.
- **FR-016:** Video placement shall reference exactly one current B-100 coverage plan and all required enriched Moment key states.
- **FR-017:** Video requests shall preserve B-100 action arc, continuity, camera, audio ownership, and dialogue synchronization while adding only presentation timing/asset choices.
- **FR-018:** Storyboard entities shall not derive semantic media metadata from raw RP prose; they shall select and place B-100 production data.
- **FR-019:** B-101 shall submit generation requests only through modality-owned contracts that resolve explicit compiler capabilities and configured keys.
- **FR-020:** A modality generator shall report missing or incompatible generation configuration before enqueue without fallback; B-101 shall surface that result unchanged.
- **FR-021:** B-101 shall treat generator-owned candidates as read-only and ineligible until the owning generator reports approval.
- **FR-022:** Placements shall reference exact approved assets and checksums.
- **FR-023:** Cue timing shall use typed relative anchors and resolve without cycles or invalid overlap.
- **FR-024:** No default duration shall be substituted when one is required.
- **FR-025:** Publication shall validate every blocking source, semantic, timing, asset, continuity, and content-policy condition.
- **FR-026:** Publication shall use compare-and-set promotion and create an immutable playback manifest.
- **FR-027:** Playback manifests shall contain all concrete text, assets, timing, transitions, mix, navigation, and restoration policy.
- **FR-028:** The Visual Novel Player shall perform no analysis, generation, model resolution, or semantic inference.
- **FR-029:** More-media coverage shall be measured separately from basic publication validity.
- **FR-030:** All B-101 arrangement, request submission, approved-asset selection, validation, and publication operations shall be auditable and retain links to generator-owned provenance.
- **FR-031:** Missing dialogue, audio, visual, action, continuity, or video semantics shall require an explicit B-100 revision, never B-101 inference.

## Non-Functional Requirements

- **NFR-001:** Opening an existing local published manifest shall require no network or model access.
- **NFR-002:** Back/Forward state restoration shall be deterministic for the same manifest and player version.
- **NFR-003:** Draft mutation and publication shall have concurrency tests proving no stale overwrite.
- **NFR-004:** Published manifests and assets shall be checksummed and versioned.
- **NFR-005:** Timeline validation shall report entity-scoped stable error codes.
- **NFR-006:** B-100 production contracts shall compile to new providers without changing B-101 presentation entities.
- **NFR-007:** Large asset bytes shall remain outside SQLite; metadata, lineage, checksums, and relative paths are persisted.
- **NFR-008:** Player layout shall support desktop and mobile without overlapping text/media/navigation.
- **NFR-009:** Accessibility shall include keyboard navigation, captions/transcripts, audio controls, and reduced-motion behavior.
- **NFR-010:** Content-policy and consent/provenance metadata shall remain attached through generation and publication.

## Success Measures

- 100% of published dialogue has reviewed speaker attribution and source spans.
- 100% of published segments have explicit visual and audio states.
- 100% of placed generated assets are approved and checksum verified.
- Zero model/provider calls during normal playback.
- Zero stale draft or publication overwrites in concurrency tests.
- Back/Forward restoration passes the reference-state matrix for every duration/visual/audio mode.
- A complete text-first presentation can publish before audio/video generators are implemented.

## Out of Scope

- Media model/vendor selection and generation-quality tuning.
- Branching choices and game-state logic.
- Localization workflow.
- Distribution packaging or streaming/CDN architecture.
- Real-time generated dialogue or media during playback.