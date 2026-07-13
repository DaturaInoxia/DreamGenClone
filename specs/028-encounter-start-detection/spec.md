# Feature Specification: Semantic Encounter-Start Detection & Memory Enrichment

**Feature Branch**: `028-encounter-start-detection`  
**Created**: 2026-07-08  
**Status**: Draft  
**Input**: User description: "Semantic encounter-start detection with memory enrichment for vivid first-person encounter recall and bug fixes for start-index capture"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reliable Sexual Encounter Detection (Priority: P1)

As a roleplay participant, I want the system to accurately detect when a sexual encounter actually begins (not just when sexy conversation occurs), so that encounter summaries and memories reflect the full interaction range from the first physical sexual act rather than missing early interactions or triggering on mere flirtation.

**Why this priority**: Encounter start detection is foundational — without it, encounter summaries have wrong interaction ranges, memories miss key content, and the aftermath contrast framing breaks. The current keyword-only heuristic cannot distinguish "sexy conversation" from "actual sex."

**Independent Test**: Can be tested by playing through a session with flirtation followed by sexual activity — verify that encounter-start is NOT triggered during flirtation but IS triggered on the first interaction containing actual physical sexual contact. Verify the `EncounterCompletion` record captures the correct `StartInteractionIndex`.

**Acceptance Scenarios**:

1. **Given** a session where characters flirt and make sexual innuendos, **When** no actual physical sexual contact has occurred, **Then** no encounter-start is detected and no encounter is created.
2. **Given** a session where characters are engaged in sexy conversation, **When** the first interaction containing actual physical sexual activity (touching, oral, intercourse) occurs, **Then** the system detects the encounter-start, sets the start interaction index, and tags the interaction as the encounter start.
3. **Given** encounter #1 has completed and the characters are in non-sexual interaction, **When** sexual activity begins again for encounter #2, **Then** the system correctly detects encounter #2's start (not blocked by stale state from encounter #1).
4. **Given** a session where sexual activity began during the BuildUp phase, **When** the Climax phase is entered later, **Then** the encounter start index from BuildUp is preserved (not overwritten by the Climax entry).

---

### User Story 2 - Vivid First-Person Encounter Memories (Priority: P2)

As a roleplay participant, I want encounter memories to be written in vivid first-person prose that captures sensory detail, emotional impact, who was present, what acts occurred, and orgasm details, so that the character's internal recollection feels authentic and emotionally resonant during aftermath scenes.

**Why this priority**: The current third-person sterile summaries ("Becky and Dean had an encounter in the living room") produce weak aftermath contrast. Vivid first-person memories ("I can still feel him inside me — my thighs are wet") create the emotional punch that makes the aftermath framing work. This depends on P1 (correct encounter ranges) for accurate input.

**Independent Test**: Can be tested by completing a sexual encounter, waiting for the enrichment job to run, and inspecting the `LlmSummary` in the `EncounterCompletion` record — verify it contains first-person prose with who, what physical acts, orgasm details, and sensory/emotional content.

**Acceptance Scenarios**:

1. **Given** a completed sexual encounter with a Wife character, **When** the encounter enrichment job runs, **Then** the generated memory is written in first person ("I...") from the Wife's perspective, naming the other person, describing physical acts explicitly, and including orgasm and sensory details.
2. **Given** a completed sexual encounter with a Husband character who was watching, **When** the enrichment job runs, **Then** the generated memory is in first person from the Husband's perspective ("I stood in the hallway and watched...").
3. **Given** a completed sexual encounter with an OtherMan character, **When** the enrichment job runs, **Then** the generated memory is in first person from the OtherMan's perspective ("She was on top of me...").
4. **Given** a single-interaction encounter (only one interaction in the range), **When** the enrichment job runs, **Then** the LLM can still produce a memory from that single interaction.

---

### User Story 3 - Correct Encounter Interaction Ranges (Priority: P3)

As a system operator, I want encounter completion records to always reference the correct interaction range (from actual sexual start to encounter end), so that enrichment prompts, summaries, and analytics use accurate data regardless of which narrative phase the encounter started in.

**Why this priority**: This is a bug fix that enables P1 and P2 to work correctly. The start-index clobbering bug and missing reset bug both cause wrong interaction ranges in encounter records. Without this, even correct detection (P1) produces wrong data.

**Independent Test**: Can be tested by running a multi-encounter session and verifying that each `EncounterCompletion` record's `StartInteractionIndex` and `EndInteractionIndex` span the correct interactions for that encounter, with no overlap or gaps.

**Acceptance Scenarios**:

1. **Given** encounter #1 has completed and the boundary has been processed, **When** the system resets state for the next encounter, **Then** `CurrentEncounterStartInteractionIndex` is reset to 0 so encounter #2 detection works correctly.
2. **Given** an encounter began in BuildUp (start index = 5), **When** the Climax phase is entered at interaction 12, **Then** the start index remains 5 (not overwritten to 12).
3. **Given** encounter #2 completes, **When** the `EncounterCompletion` record is generated, **Then** `StartInteractionIndex` reflects the first interaction of encounter #2, not a stale index from encounter #1.

---

### Edge Cases

- What happens when two encounters occur back-to-back with no non-sexual interaction between them? The re-entry guard must prevent a false start detection during an active encounter.
- What happens when an encounter-start is detected mid-narrative (not at a turn boundary)? Detection fires on whichever interaction crosses the sexual activity threshold.
- What happens when keyword pre-filter matches but the LLM semantic inference says "no"? The LLM overrides — the interaction was not real physical contact, just explicit conversation.
- What happens when the LLM inference call fails (network error, timeout)? The system falls back to the keyword heuristic with the corrected re-entry guard, and logs a diagnostic event.
- What happens when the first interaction of a session is sexual? Detection works normally — all state is at initial values.
- What happens when the enrichment job hasn't run yet (async latency)? The `ActiveSummary` falls back to `TemplateSummary` — vivid prose arrives on the next aftermath cycle.
- What happens when a character has no role in `CharacterStats`? The enrichment prompt uses "Unknown" as the character role.
- What happens when a theme has no `encounter-started` mapping configured? Semantic detection still runs — encounter-started is universal and requires no per-theme mapping. The confidence threshold comes from a single global configurable default (e.g., appsettings).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST use LLM semantic inference to detect when a sexual encounter begins, replacing the keyword-only heuristic as the primary detection mechanism.
- **FR-002**: System MUST retain keyword-based sexual activity detection as a pre-filter to keep LLM inference calls economical (only invoke LLM when keywords suggest sexual content).
- **FR-003**: System MUST NOT detect an encounter-start if the characters are already in an active sexual encounter (re-entry guard using `InteractionsInCurrentEncounter`).
- **FR-004**: System MUST set the encounter start interaction index to the current interaction count when an encounter-start is detected.
- **FR-005**: System MUST tag interactions with a boolean flag indicating they are the encounter-start interaction.
- **FR-006**: System MUST run semantic `encounter-started` inference universally for all themes — no per-theme mapping required. Detection uses the same inference engine as `encounter-completed` without requiring an `encounter-started` entry in theme semantic event mappings.
- **FR-007**: System MUST use a single global configurable confidence threshold for `encounter-started` detection, defaulting to 0.70, overridable via appsettings, shared across all themes.
- **FR-008**: System MUST generate encounter completion memories in vivid first-person prose including: who was present, what physical acts occurred, orgasm details (who, how many, where), sensory details (touch, taste, sound, smell), and emotional impact.
- **FR-009**: Encounter memory enrichment MUST be role-agnostic — it must work for Wife, Husband, OtherMan, and Persona characters without role-specific branching.
- **FR-010**: System MUST correctly resolve the character's display name and role for the enrichment prompt (fixing the current data bug where detection evidence text is mislabeled as the character name).
- **FR-011**: System MUST reset `CurrentEncounterStartInteractionIndex` to 0 after each encounter boundary is processed, regardless of whether `AdvanceTime → None` is reached.
- **FR-012**: System MUST NOT overwrite `CurrentEncounterStartInteractionIndex` when entering the Climax phase if an encounter has already started (index is non-zero).
- **FR-013**: System MUST write debug events (`EncounterStartDetected`, `EncounterStartDetectionFailed`) when encounter-start inference runs.
- **FR-014**: System MUST log LLM inference failures with warnings and fall back to keyword heuristic behavior (with corrected re-entry guard) rather than crashing.
- **FR-015**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store.
- **FR-016**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-017**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-018**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities

- **Encounter Start Detection**: Represents the moment a sexual encounter begins — triggered by LLM semantic inference on the first interaction with actual physical sexual activity. Key attributes: detected timestamp, start interaction index, encounter number, confidence score.
- **Encounter Completion Memory**: A first-person prose summary of what happened during a completed sexual encounter, generated by LLM enrichment. Key attributes: character perspective, interaction range, sensory details, orgasm information, emotional content.
- **Semantic Event Mapping**: `encounter-completed` remains theme-mapped with confidence thresholds. `encounter-started` is universal — no per-theme mapping required; it always runs when the keyword pre-filter triggers.
- **Encounter State**: Runtime tracking of the current encounter's progress including: encounter number, start interaction index, interactions in current encounter, and time-skip phase. Reset at boundaries.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Encounter-start detection correctly distinguishes "sexy conversation" from "actual sexual activity" with at least 90% accuracy compared to human judgment.
- **SC-002**: Encounter #2+ start detection works correctly in 100% of multi-encounter sessions (no false blocks from stale encounter #1 state).
- **SC-003**: Encounter completion records reference the correct interaction range (StartInteractionIndex to EndInteractionIndex) with no overlap between consecutive encounters.
- **SC-004**: Generated encounter memories contain all required elements (who, what acts, orgasms, sensory detail, emotional impact) in at least 95% of enrichment runs.
- **SC-005**: Generated encounter memories are in first-person perspective ("I...") in 100% of enrichment runs.
- **SC-006**: The `displayName` data bug (detection evidence mislabeled as character name) is resolved — the enrichment prompt correctly shows the character name.
- **SC-007**: Semantic encounter-start detection runs for every session regardless of theme configuration — no per-theme mapping required.
- **SC-008**: When an encounter begins in BuildUp, the start index survives Climax phase entry unchanged in 100% of cases.

## Assumptions

- The LLM used for semantic inference (`encounter-started`) is the same engine already used for `encounter-completed` detection.
- The existing keyword lists (`SexualActivityKeywords` + `SubtleSexualActivityKeywords`) are broad enough to serve as an effective pre-filter; false negatives (real sex missed by keywords) are an acceptable risk.
- The `HusbandAftermathInjector` remains unchanged — it continues to read `ActiveSummary` and wrap it in contrast framing.
- No `encounter-started` theme mapping is required — semantic start detection runs universally for all themes.
- Character roles are already populated in `CharacterStats` at the time the enrichment job runs.

## Out of Scope

- **HusbandAftermathInjector**: Unchanged — continues to read `ActiveSummary` and wrap it in contrast framing as before.
- **DB migration**: No schema changes — no new tables or columns beyond the `WasEncounterStart` property on the existing `RolePlayInteraction` entity.

## Dependencies

- Existing `ISemanticEventInferenceService` and its LLM inference pipeline.
- Existing `EncounterSummaryJobHandler` and its async enrichment job infrastructure.
- Existing `RolePlayV2EncounterSummaries` table for storing encounter completion records.
- Theme configuration system supporting `RPThemeSemanticEventMappings`.
