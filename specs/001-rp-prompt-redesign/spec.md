# Feature Specification: RP Prompt Redesign

**Feature Branch**: `001-rp-prompt-redesign`
**Created**: 2026-07-17
**Status**: Draft
**Input**: User description: "Comprehensive redesign of how role-play prompts are built — replacing the current 900-line procedural BuildPromptAsync method with a slot-based template architecture across 3 attention zones (primacy/context/recency), with actor-aware filtering, token budget enforcement, deduplication, tiered history compression, Narrative-as-first-class-variant, and World State slot ready for B-062."

## Clarifications

### Session 2026-07-17

- Q: Where should NPC Agency content live in the prompt architecture? → A: B — fold NPC Agency into behavioral frames rather than treating it as a separate slot/directive. **Design insight**: "NPC Agency" as a distinct concept is a design flaw. All characters are either NPC or You (Persona); NPC Agency is really an aspect of good behavioral frame writing (initiative, internal life, desires) rather than a separate directive or feature. The slot architecture should not introduce an "NPC Agency" slot — agency qualities belong inside each character's behavioral frame content.
- Q: Should the 17-slot architecture be enumerated and frozen as a spec contract? → A: Yes (Option A) — enumerate all 17 slots in the spec with zone (A/B/C), order, and trim eligibility, frozen as part of the spec contract. The slot list, zone assignment, ordering, and trim eligibility are now normative: implementation must not add, remove, reorder, or re-zone slots without a spec change. World State is a conditional sub-slot (4a) of Scene Location Lock, not counted among the 17 mandatory ordered slots.
- Q: Should compression thresholds (turn bands for scenario compression, tiered history compression, session memory tiers) be hardcoded constants or UI-backed persisted configuration? → A: Option A — all compression thresholds MUST be UI-backed persisted configuration with no hardcoded defaults; the system MUST fail fast with explicit diagnostics when required threshold configuration is missing.
- Q: How should missing phase Rule of Thumb text be handled — fallback to the writing style profile's default, or fail fast? → A: Option A — fail fast with an explicit diagnostic when the phase Rule of Thumb is missing, aligning with the repo Hard Rule. The writing style profile's default Rule of Thumb becomes a separate always-present slot element (not a fallback path). If the profile itself lacks a default, that also fails fast.
- Q: Should `MaxPromptChars` have a hardcoded code default (e.g., 35,000), or be UI-backed persisted config only with fail-fast when missing? → A: Option A — `MaxPromptChars` MUST be UI-backed persisted configuration only, with no hardcoded code default. The system MUST fail fast with an explicit diagnostic when `MaxPromptChars` is missing or invalid. The 35,000-character value is a documented recommended initial config value (see Assumptions), NOT a code default.

## Slot Architecture Contract (17 Slots) — Frozen

This section is the normative contract for the slot-based prompt architecture. The 17 slots, their zone assignment, order, and trim eligibility are frozen as part of this specification. Implementation MUST NOT add, remove, reorder, or re-zone slots without a spec amendment. Each slot is an independently testable unit (FR-003, FR-036).

**Zone A — Primacy (Scene Grounding)** — never trimmed; opens the prompt.

| Order | Slot | FR | Trim Eligibility | Notes |
|-------|------|----|------------------|-------|
| 1 | Scene Anchor | FR-005 | Never | Location + phase one-liner; replaces "You are continuing..." header. |
| 2 | Actor Assignment | FR-006 | Never | "Continue as: X (Role)" or "Write as omniscient narrator" for Narrative. |
| 3 | Turn Context | FR-007 | Never | Turn number, response position, pacing-aware position guidance. |
| 4 | Scene Location Lock | FR-008 | Never | Hard constraint: current location + continuity rule. |
| 4a | World State (conditional sub-slot) | FR-009 | N/A (conditional) | Sub-slot of Slot 4; fires only when B-062 data available; silently omitted otherwise. Not counted in the 17. |

**Zone B — Context (World + History)** — trimmable per FR-029 priority order.

| Order | Slot | FR | Trim Eligibility | Notes |
|-------|------|----|------------------|-------|
| 5 | Character Data | FR-010, FR-011 | Trimmable (priority 2) | Non-present character data trimmed before present; merged appearance + behavioral. |
| 6 | Scenario Context | FR-012 | Trimmable (priority 3) | Progressive compression: full turns 1-10, compressed 2-3 lines turns 10+. |
| 7 | Location | FR-013 | Trimmable (low) | Current scene full; occupied locations one-line; others omitted. |
| 8 | Writing Style | FR-014 | Trimmable (last resort) | Timeless description/example always kept; phase Rule of Thumb trimmed only under extreme pressure. |
| 9 | Interaction History | FR-015 | Trimmable (priority 1) | Oldest trimmed first; tiered compression (full → narrative-only → encounter memory). |
| 10 | Session Memory | FR-016 | Trimmable (priority 4) | Three tiers: long-term backstory, medium-term encounter summaries, short-term phase milestones. |
| 11 | Scene Continuity Anchor | FR-017 | Trimmable (low) | Cross-perceptions only; self-perceptions dropped. |

**Zone C — Recency (Directives + Instruction)** — directives + final instruction close the prompt.

| Order | Slot | FR | Trim Eligibility | Notes |
|-------|------|----|------------------|-------|
| 12 | Theme Contract | FR-018 | Never | Appears exactly once; active theme + phase guidance + steering rank. |
| 13 | Behavioral Frames | FR-019 | Trimmable (non-present frames) | Exactly once; filtered by actor; NPC agency lives here, not as a separate slot. |
| 14 | Scenario Guidance | FR-020 | Trimmable (low) | Phase steering; resistance band suppressed when threshold already crossed. |
| 15 | Intensity & Pacing | FR-021 | Never | Merged escalation + scene-time-direction; resolved intensity label + contract + pacing + positions. |
| 16 | User Direction | FR-022 | Never (when present) | Only fires when user provided real direction; generic "Continue naturally" omitted. |
| 17 | Final Writing Instruction | FR-023 | Never | Last content before generation; POV, word target, variant constraints; Narrative adds zero-dialogue + physical detail checklist. |

**Trim priority order (FR-029)**: Slot 9 (oldest history) → Slot 5 (non-present char data) → Slot 6 (scenario metadata) → Slot 10 (session memory) → remaining Zone B low-priority slots (7, 11, 8). Slots 1-4, 12, 15, 16 (when present), and 17 are never trimmed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Scene-Grounded Character Continuation (Priority: P1)

As a role-play user continuing a session as a specific character, I want the AI to immediately understand where I am, who I am, and what's happening in the current moment, so that my character's continued response feels naturally grounded in the scene without generic meta-instructions cluttering the experience.

**Why this priority**: This is the core prompt path — every character continuation flows through this. The current prompt wastes the highest-attention position on dead tokens ("You are continuing an interactive role-play scene") and injects wrong-character POV data. Fixing this impacts every single turn for every session.

**Independent Test**: Start any role-play session, advance several turns, and continue as a character. Verify that the prompt begins with immediate scene grounding (location + phase), followed by actor assignment (character name + role), and contains no "You are continuing..." header. The prompt's opening lines should feel like stepping back into a living scene, not reading system boilerplate.

**Acceptance Scenarios**:

1. **Given** a session in the Climax phase with Becky at the campground, **When** the user continues as Becky, **Then** the prompt opens with the current scene location and phase, immediately followed by "Continue as: Becky" with her role, and contains no "You are continuing an interactive role-play scene" text.
2. **Given** a session with multiple characters (Becky, Dean, Ken), **When** the user continues as Dean (an NPC), **Then** the prompt contains Dean's character data in full, Becky's and Ken's data filtered to what Dean needs, and no "POV Persona: Ken" text appears in Dean's prompt.
3. **Given** a session where the user is the player character (You), **When** continuing as the player character, **Then** the prompt includes the player character's persona and full character data for scene partners.

---

### User Story 2 — Narrative Scene Synthesis (Priority: P1)

As a role-play user generating a narrative synthesis after character responses, I want the omniscient narrator to produce a rich, unified scene description that captures all character perspectives with physical detail and atmosphere, without being confused by character-POV instructions or dialogue.

**Why this priority**: The Narrative prompt is a fundamentally different prompt type (omniscient, 3rd person, no POV persona, no dialogue) that the current system treats as a character prompt with a different ending. It's a first-class variant that needs its own content filtering throughout every section of the prompt. Narrative responses also serve as the primary source for encounter memory enrichment, so their quality directly impacts long-term session continuity.

**Independent Test**: In any session with multiple characters, generate a Narrative response. Verify: the prompt contains no POV persona, character data appears in lighter format for all characters, the final instruction specifies third-person omniscient with a zero-dialogue constraint, and the output is a unified scene synthesis without quoted speech.

**Acceptance Scenarios**:

1. **Given** a session with characters Becky, Dean, and Ken, **When** generating a Narrative response, **Then** the prompt contains no "POV Persona: [character]" text, character data appears in a lighter summary format for all characters, and the final instruction explicitly states third-person omniscient narration with a hard constraint against dialogue.
2. **Given** character responses have just been generated for Becky, Dean, and Ken, **When** the Narrative prompt is built, **Then** the prompt includes all character interactions from the current turn for synthesis and the final instruction includes a physical detail checklist (positions, contact, sensations, sounds, rhythm).
3. **Given** a session after a Narrative response is generated, **When** encounter memory enrichment runs, **Then** the Narrative response text feeds into the enrichment process as the primary source for encounter summaries.

---

### User Story 3 — Token Budget Enforcement (Priority: P2)

As a system operator, I want the role-play prompt to stay within a configurable token budget so that prompt sizes remain compatible with the target model's context window, preventing silent truncation or context overflow that degrades output quality.

**Why this priority**: Without a budget, every content improvement adds characters and the prompt regresses. The budget is the mechanism that forces discipline — every section must earn its place. A configurable budget also allows users with larger context models to allocate more space.

**Independent Test**: Configure a session with a 35,000-character prompt budget. Run continuations through multiple turns. Verify: the prompt never exceeds 35,000 characters, trimming warnings appear in logs when content is trimmed, and the prompt prioritizes keeping recent history and directives over older context when trimming.

**Acceptance Scenarios**:

1. **Given** a session with `MaxPromptChars` set to 35000, **When** the prompt builder assembles content that would exceed the limit, **Then** the oldest interaction history is trimmed first and a warning is logged with the session ID, actor, and final character count.
2. **Given** a session with `MaxPromptChars` set to 80000 (for a 32K+ context model), **When** building prompts, **Then** wider history windows are preserved without trimming.
3. **Given** any prompt build, **When** content must be trimmed, **Then** Zone A primacy slots, theme contract, and final instruction are never trimmed.

---

### User Story 4 — Long-Running Session Continuity (Priority: P2)

As a role-play user in a long-running session (30+ turns), I want the AI to remember key encounters and character development from earlier in the session while maintaining detailed awareness of the most recent turns, so that the narrative feels cumulative rather than amnesiac.

**Why this priority**: The current flat history window shows only ~4 turns and encounter memory captures plot but not the emotional/sexual texture that makes callbacks feel authentic. For multi-encounter sessions, the model needs tiered memory that preserves recent detail while carrying forward compressed summaries of earlier events.

**Independent Test**: Run a session through 15+ turns spanning multiple encounters. Verify: the prompt includes full detail for the last 2-3 turns, compressed narrative-only summaries for turns 4-6, and encounter memory summaries for turns 7+. Character callbacks (e.g., "this time was different from the shower") appear naturally in responses.

**Acceptance Scenarios**:

1. **Given** a session with 15+ turns spanning multiple encounters, **When** building the prompt, **Then** the interaction history section shows full detail (character + narrative responses) for the last 2-3 turns, narrative-only summaries for turns 4-6, and compressed encounter summaries for turns 7+.
2. **Given** a session where a character has had multiple sexual encounters, **When** encounter memory enrichment processes a new encounter, **Then** the enriched summary captures emotional texture, what the character learned, how it compared to previous encounters, and what changed in the relationship dynamic.
3. **Given** a session with compressed long-term memory (after turn 10), **When** building the prompt, **Then** scenario metadata appears as a 2-3 line compressed summary instead of the full ~3,000-character scenario block.

---

### User Story 5 — Dynamic World State Awareness (Priority: P3)

As a role-play user, I want the AI to be aware of the current time of day, weather, day tracking, and environmental conditions so that the world feels alive and characters behave appropriately for the circumstances.

**Why this priority**: World state awareness transforms the setting from static backdrop to active influencer of behavior. Rain makes outdoor encounters impossible, night makes certain locations risky, and time pressure (a spouse returning from a hike) creates narrative tension. This slot is designed to be populated by the B-062 Weather & Environmental System when ready.

**Independent Test**: With world state data available (time of day, weather, day number), verify that the prompt's primacy zone includes a World State section showing current conditions. Verify that character responses reference the time/weather naturally (e.g., "the afternoon heat made the trailer feel like an oven").

**Acceptance Scenarios**:

1. **Given** a session with world state data populated, **When** building the prompt, **Then** the primacy zone (Zone A) includes a World State slot showing day number, time of day, weather conditions, and any active temporal pressures.
2. **Given** the B-062 system is not yet implemented, **When** world state data is unavailable, **Then** the World State slot is simply omitted without error — the slot is ready for B-062 but does not block prompt generation.

---

### User Story 6 — Content Deduplication (Priority: P1)

As a system operator, I want each piece of prompt content to appear exactly once, so that the prompt is as concise as possible and authoritative directives are not diluted by repetition.

**Why this priority**: The current prompt duplicates ~5,000 characters of content (Turn Context, Behavioral Frames, Theme Hard Constraints, Intensity Contract each appear 2-3 times). This wastes budget, dilutes the authority of directives, and creates confusion when duplicated content has slight variations.

**Independent Test**: Build any prompt and search for duplicate blocks. Verify: each content category (theme contract, behavioral frames, turn context, intensity directives, final instruction) appears exactly once. The deduplication does not cause any loss of information.

**Acceptance Scenarios**:

1. **Given** any prompt build, **When** inspecting the output, **Then** the theme contract appears exactly once in Zone C (recency), not also in Zone B or at the end of the prompt.
2. **Given** any prompt build, **When** inspecting the output, **Then** behavioral frames appear exactly once in Zone C, filtered by the current actor's needs, not duplicated inline and via coordinator injectors.
3. **Given** any prompt build, **When** inspecting the output, **Then** the final writing instruction appears exactly once as the last content before generation, with no duplicate directive preceding it.

---

### Edge Cases

- **Empty session**: When a session has zero interactions (first turn), the interaction history slot is empty and the session memory slot contains only long-term character backstory.
- **Single-character session**: When only one character is present, actor filtering simplifies — no non-present character comparisons are needed, and no cross-perception continuity is required.
- **All locations occupied**: When every location has a tracked character, the location slot shows summaries for all locations (no location is omitted).
- **Budget overflow with minimal content**: If the budget is set so low that even Zone A + Zone C mandatory slots exceed it, the builder logs a critical warning and still produces the prompt (mandatory slots are never trimmed).
- **Actor profile mismatch**: If the requested actor is not found in the session's character roster, the system fails fast with an explicit diagnostic rather than silently defaulting to a different actor.
- **Missing phase definition**: If the current phase has no configured Rule of Thumb text, the system MUST fail fast with an explicit diagnostic (session ID, phase name, missing Rule of Thumb identifier). The writing style profile's default Rule of Thumb is a separate always-present slot element, NOT a fallback path for a missing phase Rule of Thumb. If the writing style profile itself lacks a default Rule of Thumb, the system MUST also fail fast.
- **Narrative prompt with zero character responses**: If the Narrative prompt is requested but no character responses exist for the current turn, the prompt still builds but the history slot reflects the gap.
- **Concurrent prompt builds**: Two prompt builds for different actors in the same session must not interfere — each reads the same session state and produces actor-filtered output.

## Requirements *(mandatory)*

### Functional Requirements

#### Architecture & Structure

- **FR-001**: The system MUST build role-play prompts using a slot-based template architecture with exactly 17 ordered slots distributed across three attention zones: Primacy (Zone A), Context (Zone B), and Recency (Zone C).
- **FR-002**: The system MUST generate two distinct prompt variants — Character (first-person, single-character POV) and Narrative (third-person omniscient, all-character synthesis) — using the same 17-slot architecture with variant-specific content filtering per slot.
- **FR-003**: The prompt builder MUST be implemented as a composable pipeline where each slot is an independently testable unit that receives build context (session, actor profile, phase, intent) and produces its section of the prompt.
- **FR-004**: The system MUST enforce a configurable character-level token budget (`MaxPromptChars`) that prevents prompts from exceeding the specified limit. `MaxPromptChars` MUST be sourced from UI-backed persisted configuration only — there MUST be no hardcoded code default. If `MaxPromptChars` is missing or invalid for a session, the system MUST fail fast with an explicit diagnostic (session ID, missing/invalid `MaxPromptChars` identifier) rather than substituting a default. The documented recommended initial config value is 35,000 characters (see Assumptions); this value is a configuration recommendation, NOT a code default.

#### Zone A — Primacy (Scene Grounding)

- **FR-005**: The prompt MUST open with an immediate scene-anchoring line identifying the current location and narrative phase (e.g., "Current scene: Campground — Climax phase"), replacing the current generic "You are continuing..." header.
- **FR-006**: The prompt MUST include an actor assignment line specifying which character is being written (e.g., "Continue as: Becky (Wife)") or "Write as omniscient narrator" for Narrative variant, immediately after scene anchoring.
- **FR-007**: The prompt MUST include turn context showing turn number, response position, and position-specific guidance that adapts to the current pacing mode.
- **FR-008**: The prompt MUST include a scene location lock (hard constraint) stating the current location and continuity rule, positioned in Zone A.
- **FR-009**: The prompt MUST include a World State slot in Zone A (after location lock) that displays current day number, time of day, weather conditions, world rhythm, and any active temporal pressures — this slot MUST be designed to consume data from the B-062 Weather & Environmental System when available and be silently omitted when data is unavailable.

#### Zone B — Context (World + History)

- **FR-010**: The character data slot MUST filter content by actor profile: full character sheets for the writing actor and scene partners, comparison-only reference for non-present characters (endowment, stamina, skill), and lighter summary format for all characters in Narrative variant.
- **FR-011**: The character data slot MUST merge appearance descriptions with behavioral constraint text into a single block per character, eliminating the current per-character duplication.
- **FR-012**: The scenario context slot MUST apply progressive compression — full scenario block for turns within the configured early-game band (default-configured band: turns 1-10), compressed 2-3 line world context summary for turns beyond the configured threshold. The turn-band threshold MUST be read from UI-backed persisted configuration; no hardcoded default. If the threshold configuration is missing or invalid, the system MUST fail fast with an explicit diagnostic rather than substituting a default band.
- **FR-012a**: The compression turn-band thresholds for scenario context (FR-012), tiered interaction history (FR-015), and session memory tiers (FR-016) MUST all be sourced from UI-backed persisted configuration. There MUST be no hardcoded runtime defaults for these thresholds. Missing or invalid threshold configuration MUST cause a fail-fast diagnostic with the session ID, slot name, and missing threshold identifier.
- **FR-013**: The location slot MUST filter to show the full description of the current scene location only; locations with tracked characters get a one-line summary; all other locations are omitted.
- **FR-014**: The writing style slot MUST include the timeless style description and example text always, a phase-aware Rule of Thumb that changes based on the current narrative phase (Opening, BuildUp, Committed, Approaching, Climax, Reset), AND the writing style profile's default Rule of Thumb as a separate always-present slot element. The phase Rule of Thumb and the profile default Rule of Thumb are distinct slot elements — the profile default is NOT a fallback for a missing phase Rule of Thumb. Missing phase Rule of Thumb configuration MUST fail fast with an explicit diagnostic (session ID, phase name). Missing profile default Rule of Thumb MUST also fail fast. No fallback path exists for either.
- **FR-015**: The interaction history slot MUST use tiered compression with turn bands sourced from UI-backed persisted configuration (no hardcoded defaults; fail fast if missing):
  - **Layer 1** — Full detail (character + narrative responses) for the most recent `HistoryFullDetailTurnBand` interactions.
  - **Layer 2** — Not implemented. Narrative fragment snippets at any truncation length carried no useful signal. Long-term continuity is handled by Slot 10 (Session Memory, FR-016) via encounter summaries and phase milestones instead.
  - Interactions beyond `ContextWindowTurns` are omitted from this slot entirely.
  - `HistoryNarrativeOnlyTurnBand` is retained as a stored config field but is not consumed.
- **FR-016**: The session memory slot MUST present three tiers with their boundaries sourced from UI-backed persisted configuration (no hardcoded defaults; fail fast if missing): long-term (compressed character backstory), medium-term (enriched encounter summaries with emotional/sexual texture), and short-term (phase transition milestones for the current cycle).
- **FR-017**: The scene continuity anchor MUST show cross-perceptions only (character A sees character B at location X), dropping redundant self-perception lines.

#### Zone C — Recency (Directives + Instruction)

- **FR-018**: The theme contract MUST appear exactly once in the prompt, positioned in Zone C, containing the active theme name, description, phase guidance prose, theme directives, and steering rank statement.
- **FR-019**: The behavioral frames slot MUST appear exactly once in Zone C, filtered to show frames only for characters the current actor will interact with (for Character variant) or all frames (for Narrative variant).
- **FR-020**: The scenario guidance slot MUST provide phase-appropriate steering direction and suppress or weaken the resistance band when narrative state shows the threshold has already been crossed.
  - **Revision note (debug#13)**: Narrative variant intentionally omits Scenario Guidance — Narrative only summarizes the turn's interactions and does not receive phase steering.
- **FR-021**: The intensity and pacing slot MUST merge the previously separate escalation and scene-time-direction injectors into a single block providing resolved intensity label, intensity writing contract, pacing directive, and available positions.
- **FR-022**: The user direction slot MUST only appear when the user has provided actual direction content; the generic "Continue naturally" default MUST be omitted.
- **FR-023**: The final writing instruction MUST be the last content the model reads before generating, specifying POV (first-person for Character, third-person omniscient for Narrative), word target range, and variant-specific constraints. The Narrative variant MUST include a zero-dialogue hard constraint and a physical detail checklist.
  - **Revision note (debug#12)**: Phase Directive now intentionally placed AFTER the Writing Instruction to improve model compliance — the model was not following Phase Guidance when positioned inside the Theme Contract slot. FR-023 should be revisited to allow this ordering.

#### Actor Awareness

- **FR-024**: The prompt builder MUST resolve an actor profile (Player/NPC-present/NPC-non-present/Narrative/Custom) at build time and pass it to every slot, enabling each slot to filter content by what the current actor needs.
- **FR-025**: For Non-Present NPC actor profiles, the system MUST show the actor's full character sheet plus comparison-only references for present characters, with reduced directive scope.
- **FR-026**: For Narrative actor profile, the system MUST suppress all POV persona injection, use lighter character data format for all characters, include all behavioral frames, and apply the physical detail checklist with zero-dialogue constraint.

#### Deduplication

- **FR-027**: Each category of prompt content (theme contract, behavioral frames, turn context, intensity directives, final writing instruction) MUST appear exactly once in any built prompt.
  - **Revision note (debug#16)**: Static behavioral frames and dynamic runtime state texts coexist in the same slot and can contradict (e.g. character baseline "completely unaware" vs current state "actively engaged"). Need to revisit dynamic behavior texts to address contradictions.
- **FR-028**: The system MUST remove the duplicate Turn Context injector, the duplicate Behavioral Frame injector stub, the duplicate Narrative directive, and all other coordinator-injected duplicates of inline content.

#### Token Budget & Trimming

- **FR-029**: When a prompt would exceed the configured character budget, the system MUST trim in priority order: oldest interaction history first, then non-present character data, then scenario metadata compression, then session memory; Zone A slots, theme contract, and final instruction MUST never be trimmed.
- **FR-030**: The system MUST log a warning when trimming occurs, including session ID, actor name, pre-trim and post-trim character counts.

#### Engine Data Hygiene

- **FR-031**: The prompt MUST NOT contain raw engine data that the model cannot productively use, including: raw adaptive character stat numbers, raw intensity profile GUIDs, confidence values, and uninterpreted resistance band labels.
- **FR-032**: The system MUST replace the "HARD CONSTRAINT" label dilution (currently ~25 instances) with targeted use only where the constraint genuinely warrants it.

#### Memory & Encounter Enrichment

- **FR-033**: Encounter summary enrichment MUST capture six dimensions per encounter: what happened (plot), what the character felt (emotional texture), what they learned (sexual self-knowledge), what changed (relationship dynamic), what risk was taken (near-miss/discovery), and what the other character now knows.
- **FR-034**: Encounter detection MUST use secondary signals beyond male orgasm in narrative, including: scene change after intimacy, significant time passage, explicit encounter boundary language, and phase transition from Climax to Reset.
- **FR-035**: Narrative response text MUST feed into encounter summary enrichment as the primary source material, supplemented by character responses for emotional/POV detail.

#### Testability & Diagnostics

- **FR-036**: Each prompt slot MUST be independently unit-testable, accepting build context and producing verifiable text output.
- **FR-037**: The prompt builder MUST log at Information level for each build: session ID, actor name, phase, final character count, and which slots fired.
- **FR-038**: Missing or invalid required configuration for prompt building MUST fail fast with explicit diagnostics rather than silently continuing with substituted defaults.
- **FR-039**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store.
- **FR-040**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.

### Key Entities

- **Prompt Slot**: An ordered, independently testable content producer that writes exactly one section of the final prompt. Each slot has an identifier, zone assignment (A/B/C), order within zone, eligibility for budget trimming, and produces variant-specific content filtered by actor profile.
- **Actor Profile**: A resolved configuration at build time that determines content filtering across all slots. Profiles include Player (first-person, full character data), NPC-Present (scene partner perspective), NPC-Non-Present (self-focused with comparisons), Narrative (omniscient, all characters, no persona), and Custom.
- **Prompt Build Context**: The collection of data passed to each slot during building: session state, current actor profile, narrative phase, prompt intent (Character or Narrative), and turn metadata.
- **Token Budget**: A configurable character limit with defined trimming priority that ensures prompts fit within model context windows. Configurable per session via `MaxPromptChars`.
- **Encounter Memory**: Enriched summaries stored per character per encounter, capturing plot, emotional texture, learning, relationship change, risk, and comparison anchors. Serves as Tier 2 of the three-tier memory architecture.
- **World State**: Live environmental data (day, time, weather, temporal pressures) consumed by the World State slot in Zone A. Designed to be populated by the B-062 Weather & Environmental System.
- **Writing Style Profile**: Configuration containing timeless description and example text, plus phase-specific Rule of Thumb variants for each of the six narrative phases.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Prompt character count is reduced by at least 30% compared to the current ~50,000-character baseline, measured on equivalent sessions at similar turn counts.
- **SC-002**: No content category (theme contract, behavioral frames, turn context, intensity directives, final instruction) appears more than once in any generated prompt.
- **SC-003**: The first 1,500 characters of every prompt contain only scene-grounding content (location, actor, turn, location constraint, world state) with zero meta-instruction boilerplate.
- **SC-004**: Narrative prompts contain zero instances of POV persona text (no "POV Persona: [character]" anywhere in the prompt).
- **SC-005**: Actor-inappropriate character data (e.g., Ken's full intimate attributes in Becky's prompt) is reduced to comparison-only reference lines no longer than one line per non-present character.
- **SC-006**: Prompts for sessions with `MaxPromptChars` set to 35000 never exceed 35,000 characters, verified across 100 consecutive turns of automated testing.
- **SC-007**: Prompt build time does not increase by more than 20% compared to the current prompt building baseline, measured as average milliseconds over 100 builds.
- **SC-008**: Each of the 17 prompt slots can be tested independently — a test can assert on the output of a single slot without constructing the full prompt.
- **SC-009**: Encounter memory summaries contain at least 4 of the 6 enrichment dimensions (plot, emotion, learning, change, risk, comparison) for encounters detected after implementation.
- **SC-010**: Prompt building is performed entirely through the new slot-based architecture; the previous monolithic prompt construction approach is fully replaced with no residual legacy code path.
- **SC-011**: Users of 128K-context models can configure `MaxPromptChars` to 80000 and receive prompts with wider history windows and less aggressive compression, without code changes.
- **SC-012**: Sessions spanning 30+ turns with multiple encounters maintain coherent character development — characters make natural callbacks to earlier encounters without contradicting established history.

## Assumptions

1. **The slot-based architecture replaces, not augments, the current pipeline**: The existing `BuildPromptAsync` method, coordinator injectors, and inline content blocks are fully replaced. No hybrid mode where both old and new systems run side by side.
2. **The B-062 Weather & Environmental System will provide World State data**: The World State slot is designed to consume B-062 output. Until B-062 is implemented, the slot is silently omitted. The prompt builder does not need fallback weather/time logic.
3. **The 35,000-character value is the recommended initial config value for `MaxPromptChars` (not a code default)**: At ~4 characters per token, 35K chars ≈ 8,750 tokens, leaving ~1,250 tokens for model output within an 8K window. This value is a documented configuration recommendation only — `MaxPromptChars` MUST be UI-backed persisted configuration with no hardcoded code default, and missing/invalid configuration MUST fail fast (see FR-004). New sessions should be seeded with 35,000 as the initial config value unless the operator chooses otherwise.
4. **Phase-specific Rule of Thumb text is UI-backed persisted configuration**: The six phase-specific Rule of Thumb variants MUST be stored in UI-backed persisted configuration, not hardcoded, allowing tuning without code changes. Missing Rule of Thumb configuration for a phase MUST fail fast. The writing style profile's default Rule of Thumb is a separate always-present slot element (not a fallback for a missing phase Rule of Thumb); if the profile lacks a default, that also fails fast. No fallback path exists for either value.
8. **Compression thresholds are UI-backed persisted configuration**: All turn-band thresholds for scenario compression (FR-012), tiered history compression (FR-015), and session memory tiers (FR-016) MUST be UI-backed persisted configuration with no hardcoded defaults. Missing or invalid threshold configuration MUST fail fast with explicit diagnostics.
5. **Narrative responses averaging ~5,000 characters remain the norm**: The tiered compression strategy depends on Narrative responses being substantially larger than character responses. If Narrative output shrinks, the tiered compression ratios may need adjustment.
6. **Encounter detection reliability will improve with secondary signals**: The current male-orgasm-only detection misses encounters. Secondary signals (scene change, time passage, boundary language, phase transition) will increase detection rate.
7. **Actor profiles cover all current use cases**: The five defined profiles (Player, NPC-Present, NPC-Non-Present, Narrative, Custom) cover all current continuation scenarios. New actor types may require new profiles.

## Dependencies

- **B-062 — Weather & Environmental System**: Provides the data that populates the World State slot (Slot 4a). The slot is designed to consume B-062 output but the prompt redesign does not depend on B-062 being complete — the slot is simply omitted until B-062 is ready.
- **Existing RP configuration infrastructure**: Writing style profiles, theme contracts, behavioral frames, scenario metadata, and character data must remain accessible via their current configuration paths. The slot builder reads from the same sources as the current `BuildPromptAsync` method.
- **Encounter summary storage**: The enriched encounter memory format requires the existing `RolePlayV2EncounterSummaries` table. No schema migration is needed — only the enrichment prompt changes.
- **Session `MaxPromptChars` setting**: The token budget requires a configurable UI-backed persisted session-level property. If this property doesn't exist yet, it must be added as part of this feature. There MUST be no hardcoded code default — new sessions should be seeded with the documented recommended initial value of 35,000 characters (see Assumptions), but the runtime MUST read the persisted value and fail fast if it is missing or invalid (see FR-004).

## Out of Scope

- B-062 Weather & Environmental System engine implementation (only the prompt-side slot is in scope)
- Changes to how the model processes or responds to prompts (only prompt construction is in scope)
- Changes to the RP phase transition engine, intensity profile selection, or pacing calculation logic
- UI changes for prompt configuration or preview
- Changes to non-RP prompt types (model manager, story analysis, etc.)
- Migration of historical encounter summaries to the new enrichment format (existing summaries remain as-is; new encounters use the new enrichment prompt)
