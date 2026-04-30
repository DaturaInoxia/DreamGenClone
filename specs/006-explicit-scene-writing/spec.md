# Feature Specification: Explicit Scene Writing Directives

**Feature Branch**: `006-explicit-scene-writing`
**Created**: 2026-04-27
**Status**: Draft
**Input**: User description: "Explicit scene writing directives for Climax phase at Explicit/Hardcore intensity: detailed paced scene writing, act variety, turn-spanning pacing, narrative urgency handling, and configurable SceneDirective per IntensityProfile."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Detailed Multi-Turn Climax Scene (Priority: P1)

A user is running an adult roleplay session. The scenario reaches the Climax phase with Explicit intensity active. Currently the AI responds with one or two rushed sentences that jump straight to consummation without building through kissing, touching, or oral stimulation. The user wants each AI response to advance the scene by one meaningful beat—kissing in one turn, progressing to oral stimulation in the next, penetration in a later turn—each described with physical and sensory detail.

**Why this priority**: This is the core problem. The current single-directive prompt actively instructs the AI to rush, producing the very behavior users experience as unsatisfying. Fixing this delivers immediate, visible improvement to the majority of explicit sessions without any user configuration required.

**Independent Test**: Start an RP session with an adult scenario, advance to Climax phase at Explicit intensity, and send a continuation request. Verify the response describes specific physical acts with body positioning and sensation, and that a second continuation request advances to a new act rather than repeating or resolving the scene.

**Acceptance Scenarios**:

1. **Given** a session in the Climax phase at Explicit intensity, **When** the user sends a continuation request, **Then** the AI response describes at least one physical act with specific sensory detail and does not resolve the entire scene in a single response.
2. **Given** a continuation session already mid-scene, **When** the user sends a second continuation request, **Then** the AI advances to the next physical stage (e.g., from kissing to oral stimulation) rather than ending the scene or jumping to penetration prematurely.
3. **Given** a session in the Climax phase at Hardcore intensity, **When** the user sends a continuation request, **Then** the AI response explicitly describes multiple body areas and physical sensations within that beat, matching the higher explicitness level.

---

### User Story 2 — Narrative Urgency Does Not Abbreviate the Scene (Priority: P2)

A user's roleplay scenario establishes that the characters are in a hurry—"they only have five minutes" or "quickie in the break room." When the AI sees this urgency context, it currently shortens the scene to match the character's haste, writing only two or three sentences and skipping acts entirely. The user wants the AI to keep urgency in the dialogue and character behavior while still writing each beat of the encounter in full physical detail.

**Why this priority**: This is a distinct failure mode from the rush-to-consummation issue. Urgency cues in the story feed the AI's tendency to abbreviate, and users who set up spontaneous or time-limited scenarios are disproportionately affected.

**Independent Test**: Add narrative urgency text to a scenario ("they only have ten minutes") and advance to Climax phase at Explicit intensity. Verify that the AI writes multiple turns of detailed physical description and expresses urgency through character dialogue and energy, not by skipping acts.

**Acceptance Scenarios**:

1. **Given** a scenario containing urgency language ("only a few minutes", "quickie"), **When** the user sends a continuation request at Climax phase Explicit intensity, **Then** the AI response contains character urgency expressed through fast-paced dialogue or emotional tone AND the physical description is still multi-paragraph with body and sensation detail.
2. **Given** the same urgency scenario, **When** the user sends a second continuation request, **Then** the AI writes a new physical beat rather than jumping to an abrupt ending.

---

### User Story 3 — Custom Scene Writing Directive Per Intensity Profile (Priority: P3)

A user wants to supply their own scene-writing instructions for the Explicit and Hardcore intensity levels—for example, emphasizing particular acts or tones that match their preferred play style. They want to enter this text through the existing Intensity Profile edit form in the application and have it take effect immediately on the next turn, without modifying code or configuration files.

**Why this priority**: This enables personalization and covers edge cases not addressed by the built-in defaults. The system works correctly without it—the static defaults deliver value for all users—so it is lower priority than fixing the core behavior.

**Independent Test**: Open the Intensity Profile edit page, enter custom scene-writing text in the new Scene Writing Directive field for the Explicit level, save, and run a continuation at Climax phase. Verify the custom text appears in the assembled prompt on the next turn.

**Acceptance Scenarios**:

1. **Given** the Intensity Profile edit form, **When** the user enters text in the Scene Writing Directive field and saves, **Then** the next continuation at Climax phase Explicit intensity includes the saved text as the scene-writing instruction block.
2. **Given** a saved custom SceneDirective, **When** the user clears the field and saves, **Then** the next continuation uses the system-default static directive text instead.
3. **Given** a profile at a non-explicit intensity level (e.g., Moderate), **When** the user views that profile's edit form, **Then** the SceneDirective field is visible but disabled/grayed out, with a tooltip indicating it only applies at Explicit or Hardcore intensity.

---

### Edge Cases

- What happens when the scene is already past foreplay in the conversation history? The Scene Writing Directive must not force a restart of the act sequence—the AI should continue from the established position in the narrative.
- How does the system handle a user message like "Skip ahead, just have sex quickly"? A direct user instruction to abbreviate takes precedence; the scene-writing directive does not override an explicit user command.
- What happens if the BuildUp phase is active with Explicit intensity? The Scene Writing Directive block must not fire—the existing consummation guard for BuildUp applies regardless of intensity.
- What happens when a profile's SceneDirective is empty? The system applies the built-in static fallback directive automatically, with no error and no prompt shown to the user.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST produce detailed, multi-paragraph physical scene descriptions when the Climax phase is active at Explicit or Hardcore intensity.
- **FR-002**: System MUST structure erotic scene progression across multiple AI turns, with each turn advancing physical intimacy by one measured increment; major transitions (e.g., kissing → oral stimulation → penetration, position changes) warrant their own turns.
- **FR-003**: Scene writing at Explicit/Hardcore intensity MUST address the full range of physical intimacy relevant to the scenario—including kissing, oral contact, manual stimulation, and varied positions—with each act receiving dedicated, detailed attention rather than being summarized or skipped.
- **FR-004**: System MUST express narrative urgency (e.g., characters hurrying, limited time) through action intensity, breathless dialogue, and emotional tone—NOT by abbreviating or collapsing the physical scene description.
- **FR-005**: Scene Writing Directive block MUST NOT be injected during the BuildUp phase; the existing consummation guard is unchanged.
- **FR-006**: Scene-writing behavior MUST be configurable per intensity profile level through the UI; no code change required to update directives.
- **FR-007**: When a profile's SceneDirective field is empty, system MUST automatically apply the built-in static directive for that intensity level.
- **FR-008**: System MUST use exactly one SceneDirective source per turn: the active profile's saved field, or the static fallback if the field is empty. No silent substitution from another profile.
- **FR-009**: Scene Writing Directive MUST apply to all intent types: Continuation, Narrative, and NPC responses.
- **FR-010**: Explicit and Hardcore intensity level descriptions MUST clearly communicate detailed pacing, act variety, and multi-turn scene expectations.
- **FR-011**: System MUST apply a revised Climax phase escalation directive set in place of the previous single-line rush directive; the new directive set MUST include pacing, act variety, and urgency-handling instructions.
- **FR-012**: Persisted data (SceneDirective) MUST use SQLite via EF Core.
- **FR-013**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-014**: Major execution paths MUST emit Information-level logs; actionable failure/error logs MUST be provided for configuration and prompt assembly paths.
- **FR-015**: Log levels MUST be configurable via settings without code changes.
- **FR-016**: The SceneDirective field MUST enforce a maximum character limit (enforced at both the UI and server side before persistence).
- **FR-017**: The SceneDirective value MUST be sanitized (special markup and injection-pattern tokens stripped) before being injected into the LLM system prompt.
- **FR-018**: In the Intensity Profile edit form, the SceneDirective field MUST be visible for all intensity levels but MUST be disabled/grayed out with a tooltip for levels below Explicit, indicating the field only applies at Explicit or Hardcore intensity.

### Key Entities

- **IntensityProfile**: Represents a named intensity level configuration for a scenario (e.g., Explicit, Hardcore). Gains a new `SceneDirective` field—a longer-form text block describing how the AI should write physical scenes at that intensity level. Separate from `Description`, which labels the prose style.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A session in Climax phase at Explicit intensity produces AI responses that describe at least two distinct physical acts across consecutive turns, each with body positioning and sensory language—not summarized in a single sentence.
- **SC-002**: A complete Climax scene from initiation to resolution spans a minimum of three AI turns at Explicit intensity without the scene collapsing to a single response.
- **SC-003**: Sessions with narrative urgency cues in the scenario maintain the same multi-turn beat structure as sessions without urgency cues; urgency is detectable only in character dialogue and behavior, not in scene compression.
- **SC-004**: A custom SceneDirective entered and saved through the UI is reflected in the assembled system prompt on the very next turn—no app restart required.
- **SC-005**: Clearing a saved SceneDirective causes the static fallback directive text to appear in the system prompt on the next turn automatically.
- **SC-006**: BuildUp phase sessions at Explicit intensity continue to withhold consummation; the Scene Writing Directive block does not appear in the assembled prompt for BuildUp turns.
- **SC-007**: The build passes clean after Phase 1 changes and after Phase 2 changes independently.

---

## Clarifications

### Session 2026-04-27

- Q: What security posture applies to the user-configurable SceneDirective field that is injected verbatim into the LLM system prompt? → A: Enforce a max character limit with UI + server-side validation AND sanitize/escape the field value (strip special markup or injection-style tokens) before prompt injection.
- Q: How should the SceneDirective field appear in the Intensity Profile edit form for non-Explicit/Hardcore intensity levels? → A: Show the field but disable/gray it out with a tooltip explaining it only applies at Explicit or Hardcore intensity.
- Q: Does the Scene Writing Directive block or the new escalation directive changes apply to the Approaching phase? → A: Approaching phase is out of scope for the Scene Writing Directive block; existing escalation guidance for Approaching is unchanged by this feature.

---

## Assumptions

- Phase 1 (static prompt improvements) delivers the majority of user-visible improvement and ships before Phase 2 (configurable SceneDirective per profile).
- "Turn-spanning pacing" relies entirely on prompt guidance and conversation history. If the AI's context window is truncated or the session is restarted mid-scene, it may re-evaluate and compress. This is a known limitation of prompt-only enforcement; mechanical act-tracking would require a separate future feature.
- Direct user instructions to abbreviate a scene (e.g., "Skip ahead, they have sex quickly") override the Scene Writing Directive. The directive governs default AI behavior, not explicit user overrides.
- The SceneDirective field is meaningful only at Explicit and Hardcore intensity levels, where the scene-writing gate is active. Profiles at lower intensity levels do not display or use this field.
- Multiple directives may co-exist in the assembled prompt (e.g., theme PhaseGuidance[Climax] alongside Scene Writing Directive). Theme guidance governs scenario framing (the WHAT); Scene Writing Directive governs prose style and pacing (the HOW). Mild duplication is acceptable; no conflict-resolution code is required.
- The Approaching phase is explicitly out of scope for the Scene Writing Directive block and for the revised escalation directive set. Existing Approaching phase escalation guidance is unchanged by this feature.
