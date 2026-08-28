# Feature Specification: RP Session Opening Period

**Feature Branch**: `001-opening-period`  
**Created**: 2026-06-22  
**Status**: Draft  
**Input**: User description: "Formalize RP session opening period as a lifecycle stage with theme guidance suppression and turn-based counting"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Husband-wife dynamic established before love interest enters (Priority: P1)

As a user starting a new roleplay session, I want the first few turns to focus exclusively on establishing the husband-wife relationship, setting, and current situation, so that when the love interest character enters the narrative, the story has a grounded foundation rather than jumping straight into the theme arc.

**Why this priority**: This is the core purpose of the opening period. Without it, sessions jump into love-interest dynamics in the very first turn, skipping the baseline relationship establishment that makes the story work. Every session starts with this flow.

**Independent Test**: Create a new RP session and verify that for the first 3 complete turns, only the husband and wife characters write responses, and the narrative focuses on their dynamic, routines, and setting. The love interest character should not appear as a named participant or response writer until turn 4.

**Acceptance Scenarios**:

1. **Given** a new RP session is created with a scenario that includes an OtherMan character, **When** the first turn completes, **Then** only the husband and wife write responses; the love interest does not appear as a named character in the generated narrative.
2. **Given** a new RP session, **When** turns 1 through 3 complete, **Then** the love interest is never selected as an overflow actor (does not write any responses).
3. **Given** a new RP session, **When** turn 4 begins, **Then** the love interest becomes eligible to be named in the narrative and selected as an overflow actor.

---

### User Story 2 - No contradictory instructions in the prompt during opening (Priority: P1)

As a developer, I want the LLM prompt during the opening period to contain only opening-period guidance (no theme phase guidance), so there is no contradiction between "establish the husband-wife dynamic" and "execute exposure beats centered on the other man."

**Why this priority**: The current implementation injects both opening-peripheral-focus constraints AND theme phase guidance simultaneously, creating contradictory instructions that degrade LLM output quality. This is the root cause of the prompt coherence problem.

**Independent Test**: Extract the LLM prompt for a turn within the opening period and verify that the theme phase guidance (AppendActiveThemeContract, BuildFramingGuards, theme hard constraints) is absent, and only opening-period guidance is present.

**Acceptance Scenarios**:

1. **Given** a new RP session with an active theme (e.g., exhibitionism-v2), **When** the prompt is built for turn 1 of the opening period, **Then** the theme phase guidance text ("first exposure = accidental nudity", "exposure beats centered on the other man") is NOT present in the prompt.
2. **Given** a new RP session, **When** the prompt is built for turn 1 of the opening period, **Then** opening-period guidance ("establish the husband-wife dynamic...") IS present in the prompt.
3. **Given** a new RP session that has completed its opening period (turn 4+), **When** the prompt is built, **Then** theme phase guidance IS present and opening-period guidance is NOT present.

---

### User Story 3 - Opening period uses turn count, not interaction count (Priority: P2)

As a developer maintaining the roleplay engine, I want the opening period to be measured in complete turns (not individual interactions), so the threshold is intuitive and aligns with the natural unit of narrative progression.

**Why this priority**: The current interaction-count-based threshold (6 interactions) is confusing because a single turn produces multiple interactions (opening narrative + character responses + narrative close). Turn count is the natural unit users and developers think in. This also aligns the OPF constraint and OtherMan exclusion to use the same counter.

**Independent Test**: Verify that the opening-period threshold check uses `ObservedTurnCount` rather than `session.Interactions.Count`. Verify that the opening period ends and guidance switches over precisely at turn 4, regardless of how many interactions occurred within each prior turn.

**Acceptance Scenarios**:

1. **Given** a new RP session, **When** turn 1 starts (ObservedTurnCount=1), **Then** the opening period is active (OPF or equivalent guidance fires).
2. **Given** a session in turn 3 (ObservedTurnCount=3), **When** the overflow actor candidates are resolved, **Then** OtherMan is still excluded.
3. **Given** a session in turn 4 (ObservedTurnCount=4), **When** the overflow actor candidates are resolved, **Then** OtherMan is eligible for selection.

---

### Edge Cases

- What happens when a session has `AutoNarrative` disabled? The opening period still applies — the first character responses establish the dynamic.
- What happens when there is no OtherMan character in the scenario? The opening period still runs — theme guidance is suppressed and opening guidance is injected, just with no character to exclude.
- What happens if a new session is created mid-story (not from Reset)? The opening period always applies to new sessions — it's tied to session creation, not the phase lifecycle.
- What happens after a Reset→BuildUp cycle? The opening period does NOT re-run. It only applies at the very start of a new session. The Reset phase provides its own "return to ordinary life" guidance, and the husband-wife dynamic is already established.
- What happens when the opening period ends mid-turn? The opening period is checked per-turn, not per-interaction. The entire turn either is or is not in the opening period.
- What happens when the scenario has only 1 active theme? The observation window is not engaged (`SelectionMinimumTurns = 0`). The opening period runs for turns 1-3, then theme guidance begins immediately at turn 4 with no observer candidate menu in between.
- What happens when the scenario has multiple active themes (e.g., 4 themes, `ThemeSelectionTurnsPerTheme=2`, observation window = 6 turns)? The opening period runs for turns 1-3 (observer silent), then the observer candidate menu injects for turns 4-6, then theme guidance begins at turn 7.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST define an opening period of 3 complete turns for every new RP session, measured by the number of turns started since session creation. The opening period runs only once per session — it does NOT re-run after Reset→BuildUp cycles.
- **FR-002**: During the opening period (first 3 turns), system MUST NOT inject theme or phase guidance into the LLM prompt (no phase constraints, no theme contract directives, no theme hard constraints, no framing guards).
- **FR-003**: During the opening period, system MUST inject opening-period guidance text focused on establishing the husband-wife dynamic, relationship, setting, and routines. The guidance text MUST be modifiable/editable (not hardcoded) so users can customize it per scenario — for example, adding directives like "include brief sex life description" or "infer details from current character profiles."
- **FR-004**: The opening-period guidance text MUST be stored at the scenario level (on the scenario definition). Each scenario can have its own opening guidance text, since different scenarios establish different baselines (e.g., campground vs. office).
- **FR-005**: During the opening period, system MUST exclude characters with role "OtherMan" from being selected to write responses.
- **FR-006**: After the opening period (turn 4 onward), system MUST resume normal theme/phase guidance injection and OtherMan eligibility.
- **FR-007**: The opening period MUST use turn count as its counter, not individual interaction count.
- **FR-008**: The old hardcoded peripheral-focus constraint text (injected at the end of prompts during early turns) MUST be removed entirely, as it is superseded by the opening-period guidance.
- **FR-009**: The opening period threshold MUST be a simple fixed constant of 3 turns (no DB configuration, no UI control for this iteration).
- **FR-010**: During the opening period, theme selection MUST continue running in the background — evidence accumulates and candidates are evaluated — but no scenario is committed (no `ActiveScenarioId` is set) until the opening period ends. This preserves the evidence-gathering pipeline so a scenario is ready to activate when the opening period lifts.
- **FR-011**: During the opening period, the theme observation window MUST NOT inject its candidate menu into the prompt. The observer is silent during the opening period — only opening-period guidance is injected. The observation window's candidate menu begins injecting (if applicable) only after the opening period lifts.
- **FR-012**: The opening period and the theme observation window MUST use independent counters. The opening period does NOT consume observation turns. After the opening period lifts (turn 4+), the observation window runs its full configured window.
- **FR-013**: The theme observation window only applies when there is more than 1 active theme in the scenario. With a single active theme, the observation window is not engaged (`SelectionMinimumTurns = 0`), and theme guidance begins immediately after the opening period lifts.
- **FR-014**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-015**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-016**: All existing scenario definitions MUST be seeded with the default opening-period guidance text: *"Focus on the couple's relationship and their current life together. Include a brief sense of their intimate life from her point of view — the rhythm of it, what she feels about it, what she wants or doesn't get — grounding these details in the character profiles and their descriptions. Describe their routines, interactions, and daily rhythms. Establish the setting, mood, and any relevant history. Other characters remain in the background."*

### Key Entities *(include if feature involves data)*

- **Opening Period**: A per-session lifecycle stage lasting 3 complete turns, during which the husband-wife dynamic is established before theme-driven narrative arcs begin. Defined by a single constant threshold, enforced at the prompt-building and actor-selection levels.
- **Turn Counter**: An existing counter that increments at the start of each turn. Used to determine whether the session is in the opening period (turns 1-3) or past it (turn 4+).
- **Opening Period Direction**: Modifiable text stored on the scenario definition that directs the LLM during the opening period (emitted as a `HARD CONSTRAINT` in the prompt). Each scenario can define its own opening direction to establish its specific baseline (e.g., campground setting vs. office setting). May include directives like "include brief sex life description" or "infer from current character profiles."

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a new RP session, the love interest (OtherMan) character does not appear as a named participant or response writer for the first 3 complete turns.
- **SC-002**: Extracted LLM prompts for turns 1-3 contain zero theme phase guidance text (no exposure beat directives, no BuildUp framing guards, no theme hard constraints).
- **SC-003**: Extracted LLM prompts after the opening period AND observation window (if applicable) contain full theme phase guidance text (exposure beat directives, BuildUp framing guards, theme hard constraints) and no opening-period guidance. For single-theme scenarios this is turn 4; for multi-theme scenarios this may be later (e.g., turn 7 with 4 themes).
- **SC-004**: The opening period threshold is expressed as a single constant value (3 turns) with no scattered magic numbers or duplicated threshold logic across different parts of the system.
- **SC-005**: The old peripheral-focus constraint text no longer appears in any LLM prompt.

## Assumptions

- The turn counter is incremented before actor selection and prompt building in all code paths, so the opening period check always sees the correct turn number.
- The first turn's opening narrative (the scene-setting prose generated before any character responses) is a separate concern and is not affected by the opening period changes — it continues to fire as before.
- The persona-lead-during-setup behavior is a separate concern from the opening period and may be aligned to the same threshold optionally.
- The opening period applies to all new RP sessions regardless of scenario, theme profile, or phase state.
- The scenario definition already has a payload structure that can be extended to hold the opening-period guidance text, or a new dedicated field can be added.
- If a scenario has no opening-period guidance text defined, the system uses the seed default (documented in FR-016).

## Out of Scope

- **Scenario UI for editing opening-period guidance**: A user-facing UI to edit the opening-period guidance text on scenario definitions is out of scope for this feature. All scenarios are seeded with the default text (FR-016). Manual database edits or a future feature can update the text.

## Clarifications

### Session 2026-06-22

- Q: What should happen to theme selection during the opening period? → A: Option A — Theme selection continues running in the background (accumulating evidence, evaluating candidates), but no scenario is committed until the opening period ends.
- Q: How should the opening period interact with the theme observation window? → A: Option A — Independent counters. The opening period runs first (turns 1-3) and the observer is silent during this time (no candidate menu injected). After the opening period lifts, the observation window runs its full configured window starting from turn 4. The opening period does NOT consume observation turns. Note: the observation window only applies when there is more than 1 active theme in the scenario; with a single theme, `SelectionMinimumTurns = 0` and the observer never engages.
- Q: Should the opening period re-run after a Reset→BuildUp cycle? → A: Option A — Once per session. The opening period only runs at the very start of a new session and does NOT re-run after Reset→BuildUp cycles. The husband-wife dynamic is already established by that point; the Reset phase already provides its own "return to ordinary life" guidance.
- Q: Should opening-period guidance include an explicit "do not advance the theme arc" instruction? → A: Option A (with caveat) — Since theme data is fully suppressed during the opening period, the model cannot advance an arc it doesn't see, making the explicit negative largely redundant. The opening guidance should focus on positive direction (establish the dynamic, setting, routines). The opening guidance text itself MUST be modifiable/editable so the user can customize it per scenario — e.g., adding "include brief sex life description" or "infer from current character profiles."
- Q: Where should the modifiable opening-period guidance text be stored? → A: Option A — Scenario-level. Stored on the scenario definition; each scenario can have its own opening guidance text. Different scenarios establish different baselines (e.g., campground vs. office), so the guidance belongs with the scenario.
- Q: What should the seeded default opening-period guidance text be? → A: *"Focus on the couple's relationship and their current life together. Include a brief sense of their intimate life from her point of view — the rhythm of it, what she feels about it, what she wants or doesn't get — grounding these details in the character profiles and their descriptions. Describe their routines, interactions, and daily rhythms. Establish the setting, mood, and any relevant history. Other characters remain in the background."* This text is seeded into all existing scenarios.
