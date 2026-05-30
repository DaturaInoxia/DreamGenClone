# Feature Specification: B-041 — Session Memory Context (Intimate Encounter History Injection)

**Feature Branch**: `001-session-memory-context`
**Created**: 2026-05-29
**Status**: Draft
**Backlog**: B-041

---

## Background & Context

Characters currently behave as if each new arc is the first time an encounter has occurred, even when the same scenario has been completed multiple times earlier in the same session. Two root causes drive this:

1. The LLM context window does not retain early turns from long sessions — interactions from prior arcs scroll out of view.
2. The engine never injects structured history into the prompt — there is no mechanism to tell the AI what has already happened.

The result: characters forget prior intimacy entirely. The wife acts as though she has never been with the other man before; the other man has no knowledge of what they have done together. This destroys narrative continuity across arcs.

**Fix**: After each phase transition, generate and persist a short summary per character. At Climax→Reset (arc completion), run an asynchronous LLM job that reads all arc interactions and extracts per-character prose describing exactly what intimate acts occurred — kissing, touching, oral sex, intercourse, positions used, and how the encounter ended (finishing move). On every subsequent prompt build, inject the N most recent prior arc summaries plus current-arc phase milestones as a "Session Memory" block so the AI knows what has already happened between these characters.

---

## Clarifications

### Session 2026-05-29

- Q: Should intimate act detail be captured per completed arc or per phase transition? → **A**: Both — one detailed `ArcCompletion` entry at Climax→Reset capturing everything that happened, plus lighter `PhaseMilestone` entries at each other transition.
- Q: What format for intimate act details? → **A**: Prose paragraph, per-character perspective.
- Q: Per-character perspective or shared encounter log? → **A**: Per-character perspective — each character's memory describes their own experience of the acts.
- Q: How should per-character memories appear in the prompt? → **A**: Single combined `"Session Memory:"` block with lines tagged by character name; arc completions first then current-arc milestones.
- Q: Should the LLM arc-completion job block the session or run fire-and-forget? → **A**: Fire-and-forget — inject `LlmSummary ?? TemplateSummary` at prompt build time.
- Q: Where should the "max summaries" setting live? → **A**: Global default in `appsettings.json`, overridable per session at creation.

### Session 2026-05-29 (clarify pass)

- Q: Should there be a cap on how many prior-arc `ArcCompletion` entries are injected per prompt? → **A**: Yes — add `MaxArcCompletionsToInject` (configurable, default 10); inject the most recent N arc completions per prompt, same pattern as milestones.
- Q: What retry policy should the `EncounterSummaryEnhancement` background job follow on LLM failure? → **A**: Retry once after ~5 s; on second failure log Warning and abandon — matching the semantic analysis job pattern.
- Q: Should one LLM job be enqueued per character or per arc transition at Climax→Reset? → **A**: One job per arc transition — reads arc history once, generates all character summaries in a single LLM call, writes all character rows.

---

## Design Decisions (confirmed)

1. **Two summary entry types** — `PhaseMilestone` (template-generated immediately at every non-Reset transition) and `ArcCompletion` (LLM-generated at Climax→Reset, reading actual interaction prose).

2. **One row per character per transition** — the `RolePlayV2EncounterSummaries` table stores character-specific summaries so each character carries an independent memory of events from their own perspective.

3. **Hybrid generation** — template text is written synchronously at the transition hook; an async LLM job runs fire-and-forget to replace or enrich `LlmSummary`. The prompt always injects `LlmSummary ?? TemplateSummary`.

4. **ArcCompletion LLM job scope** — one job is enqueued per arc transition (not per character). The handler reads all interactions for the completed arc once, then generates per-character narrative prose for all characters in a single LLM call, covering: initial contact (kissing, touching), escalation acts (oral), intercourse (positions used), and the finishing move if reached. It writes one `LlmSummary` update per character row.

5. **Prompt injection block** — a single `"Session Memory:"` block positioned after the Recent Interaction History block. Injected content: all `ArcCompletion` entries for prior arcs + last N `PhaseMilestone` entries from the current arc only.

6. **Configurable injection depth** — `RolePlayMemoryOptions.MaxMilestonesToInject` (global default 5) with nullable per-session override set at session creation.

---

## User Scenarios & Testing

### User Story 1 — AI Recalls Prior Arc Intimate Acts in Current Prompt (Priority: P1)

A user runs two arcs in the same session. At the start of the second arc, the AI continuation includes references to what happened in the first arc — kissing, what positions were used, how the encounter ended — without the user needing to re-describe it.

**Why this priority**: This is the core value of the feature. Everything else supports it.

**Independent Test**: Complete one arc (Observing→BuildUp→Climax→Reset), start a second arc, trigger a continuation — verify the raw prompt contains a populated "Session Memory" block with prose describing arc 1 acts. Can be tested independently by inspecting the prompt log.

**Acceptance Scenarios**:

1. **Given** a session has completed one arc where the wife performed oral sex and intercourse occurred in missionary position, **When** the next arc reaches BuildUp and a continuation is requested, **Then** the "Session Memory" block in the prompt contains the wife's memory describing oral sex and missionary intercourse from her perspective.

2. **Given** a session is on its first arc with no completed arcs, **When** a continuation is requested, **Then** no "Session Memory" block appears in the prompt.

3. **Given** an arc completed but the LLM enhancement job has not yet finished, **When** a continuation is requested, **Then** the template summary is injected in place of the LLM prose (no empty block, no error).

---

### User Story 2 — Per-Character Memory Is Perspective-Specific (Priority: P2)

The wife's memory describes the encounter from her subjective experience; the other man's memory describes it from his. The AI can write dialogue and behavior consistent with each character's distinct perspective on what happened.

**Why this priority**: Per-character perspective is what prevents the AI from generic "they had sex" summaries and enables accurate character-specific callbacks.

**Independent Test**: After arc completion, query `RolePlayV2EncounterSummaries` for the session — verify distinct `LlmSummary` rows exist for each character with different first-person perspective text.

**Acceptance Scenarios**:

1. **Given** an arc completes with three characters (wife, other man, husband), **When** the ArcCompletion LLM job finishes, **Then** three separate summary rows exist in storage — one per character — each with prose describing the same events from that character's perspective.

2. **Given** the wife's arc memory describes "she allowed him to take her from behind" and the other man's describes "he finished with her from behind," **When** both are injected into a prompt, **Then** both appear under the same "Session Memory" block tagged by character name.

---

### User Story 3 — Phase Milestones Track Current-Arc Escalation (Priority: P3)

Within an ongoing arc, each phase transition generates a lightweight milestone note per character that describes their stat state and narrative position. These milestones appear in the prompt so the AI understands where each character currently stands in the escalation.

**Why this priority**: Milestones support continuity within an arc, not just across arcs. Lower priority than arc history but still improves coherence in long arcs.

**Independent Test**: Advance a session from BuildUp to Approaching — verify a `PhaseMilestone` row exists per character in storage and appears in the next continuation prompt's "Session Memory" block.

**Acceptance Scenarios**:

1. **Given** a session transitions from BuildUp to Approaching, **When** the transition completes, **Then** one `PhaseMilestone` row is written per character in `RolePlayV2EncounterSummaries` with `FromPhase=BuildUp, ToPhase=Approaching`.

2. **Given** more phase milestones exist for the current arc than `MaxMilestonesToInject`, **When** the prompt is built, **Then** only the most recent N milestones are included (oldest dropped first).

---

### User Story 4 — Configurable Memory Depth (Priority: P4)

A user can control how many prior phase milestones are injected per prompt. The global default applies to all sessions, with an optional per-session override set at session creation.

**Why this priority**: Token budget management — some users with smaller context models will want fewer entries.

**Independent Test**: Set `MaxMilestonesToInject` to 2 globally, create a session that produces 4 phase milestones, verify the prompt contains exactly 2 milestone entries.

**Acceptance Scenarios**:

1. **Given** `MaxMilestonesToInject` is set to 2 in global config, **When** a session with no per-session override produces a prompt, **Then** at most 2 phase milestones appear in the "Session Memory" block.

2. **Given** a session was created with `MaxMilestonesToInject` overridden to 8, **When** a prompt is built for that session, **Then** up to 8 phase milestones are injected regardless of the global default.

---

### Edge Cases

- What happens when `CharacterSnapshots` is empty at a phase transition (e.g., session creation edge case)? → Template generation must produce a minimal non-throwing summary; no crash.
- What happens if the ArcCompletion LLM job fails? → Log Warning, leave `TemplateSummary` intact, clear any partial `LlmSummary` write; next prompt injection uses template text.
- What happens if the same transition fires twice (engine re-evaluation edge case)? → Job deduplication key `$"enc-summary:{sessionId}:{cycleIndex}"` prevents double-processing.
- What if a session has 20+ completed arcs? → Inject at most `MaxArcCompletionsToInject` (default 10) most recent `ArcCompletion` entries; milestone injection is bounded by `MaxMilestonesToInject`.
- What if `MaxMilestonesToInject` is set to 0? → No milestones injected; only arc completions appear in the block. If no arc completions exist either, block is omitted.

---

## Requirements

### Functional Requirements

- **FR-001**: System MUST generate a `PhaseMilestone` summary immediately at every phase transition that is not Climax→Reset, once per character in the session.
- **FR-002**: System MUST generate a `PhaseMilestone` summary immediately at Climax→Reset per character (template), before the ArcCompletion LLM job is enqueued.
- **FR-003**: System MUST enqueue a single `EncounterSummaryEnhancement` background job at Climax→Reset (one job per arc transition, not per character). When processed, the job reads all interactions for the completed arc once and generates per-character intimate act prose in a single LLM call, describing: initial physical contact (kissing, touching), escalation acts (oral sex), intercourse (positions used), and the finishing move if reached. It writes one `LlmSummary` update per character row.
- **FR-004**: The background job MUST run fire-and-forget and MUST NOT block the session continuation. On failure, it MUST retry once after approximately 5 seconds; if the retry also fails, it MUST log a Warning and abandon, leaving `TemplateSummary` intact.
- **FR-005**: System MUST persist all encounter summaries in a dedicated `RolePlayV2EncounterSummaries` SQLite table — one row per character per phase transition.
- **FR-006**: System MUST load encounter summaries into `AdaptiveScenarioState.EncounterSummaries` at session load time.
- **FR-007**: System MUST inject a "Session Memory" block into the continuation prompt, positioned after the Recent Interaction History block. The block MUST include at most M prior-arc `ArcCompletion` entries (most recent first, where M = `MaxArcCompletionsToInject`) and at most N `PhaseMilestone` entries from the current arc only (where N = `MaxMilestonesToInject`).
- **FR-008**: The "Session Memory" block MUST be omitted entirely when there are no summaries to inject.
- **FR-009**: Injection MUST use `LlmSummary` when available, falling back to `TemplateSummary` for each row.
- **FR-010**: System MUST support global `MaxMilestonesToInject` (default: 5) and `MaxArcCompletionsToInject` (default: 10) configuration values in `appsettings.json`, bounded to the `RolePlayMemory` settings section.
- **FR-011**: System MUST support a per-session `MaxMilestonesToInject` override (nullable int) set at session creation, applied only to that session.
- **FR-012**: System MUST enable/disable the LLM enhancement job via `RolePlayMemoryOptions.EnableLlmSummaryEnhancement` flag (default: true) without code changes.
- **FR-013**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store.
- **FR-014**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-015**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-016**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities

- **`EncounterSummaryRecord`**: Represents one character's memory of a single phase transition. Fields: Id, SessionId, CharacterId, SummaryType (`PhaseMilestone` | `ArcCompletion`), CycleIndex, FromPhase, ToPhase, OccurredUtc, InteractionCountInPhase, SceneLocation, ActiveThemeId, FinishingMoveId (arc completion only), PositionIdsJson (arc completion only), CharacterStatsSnapshotJson (this character's stats), TemplateSummary, LlmSummary, LlmEnhancedUtc. Computed: `ActiveSummary → LlmSummary ?? TemplateSummary`.

- **`RolePlayMemoryOptions`**: Configuration class. Fields: `MaxMilestonesToInject` (int, default 5), `MaxArcCompletionsToInject` (int, default 10), `EnableLlmSummaryEnhancement` (bool, default true). Bound from `appsettings.json` section `"RolePlayMemory"`.

- **`EncounterSummaryJobPayload`**: Background job payload. Fields: `SessionId`, `CycleIndex`. One job per arc transition; handler generates prose for all characters in one LLM call.

---

## Success Criteria

### Measurable Outcomes

- **SC-001**: After completing one arc in a session, the raw prompt for any subsequent continuation contains a populated "Session Memory" block with at least one entry describing the prior arc from each character's perspective.
- **SC-002**: The intimate act prose injected into prompts causes the AI to produce callbacks to prior encounters in at least 80% of continuations in the second arc of a test session (manual spot-check).
- **SC-003**: Phase milestone template generation completes synchronously within the existing phase transition handling time — no measurable latency increase on the interaction response path.
- **SC-004**: The LLM enhancement job completes within 30 seconds of arc completion under normal model load and enriches the summary for the next continuation prompt in the same session.
- **SC-005**: Setting `MaxMilestonesToInject` to any value 0–20 produces the correct number of milestone entries in the prompt; setting `MaxArcCompletionsToInject` to any value 0–20 produces the correct number of arc completion entries (both verified by automated test).
- **SC-006**: All existing passing tests continue to pass after implementation (no regressions).

---

## Assumptions

- B-036 (position catalog) may not be complete by the time this is implemented. `PositionIdsJson` is nullable; the arc completion job extracts position descriptions from prose if no structured position IDs are available.
- B-029 (finishing move matrix) may not be complete. `FinishingMoveId` is nullable; finishing move description is extracted from prose if no structured ID is available.
- The LLM used for arc-completion summary generation is the same model manager as semantic analysis (not the main continuation model). If a dedicated model for summaries is desired, that is a follow-up item.
- `AdaptiveScenarioState.EncounterSummaries` is loaded on session load and held in memory for the session duration. Sessions with very many arcs (10+) may carry a larger in-memory list; this is acceptable for the current local desktop scope.
